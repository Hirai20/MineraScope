using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.Binding;

namespace MineraScope
{
    // 260430Codex: スペクトルファイルの列挙、読み込み、教師データ化を DeepLearning 本体から分離します。
    internal static class SpectrumDataLoader
    {
        // 260430Codex: 学習と予測で共通する 2048 点スペクトル長を 1 か所で管理します。
        public const int SpectrumLength = 2048;

        // 260430Codex: スペクトルファイルとして扱う拡張子判定を共通化します。
        public static bool IsSpectrumFile(string filePath) =>
            filePath.EndsWith(".msa", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".emsa", StringComparison.OrdinalIgnoreCase);

        // 260430Codex: 学習・解析で使うスペクトルファイル列挙を共通化します。
        public static string[] GetSpectrumFiles(string folderPath, SearchOption searchOption = SearchOption.AllDirectories) =>
            Directory.EnumerateFiles(folderPath, "*.*", searchOption)
                .Where(IsSpectrumFile)
                .OrderBy(path => path)
                .ToArray();

        // 260430Codex: 指定フォルダ直下にスペクトルがあるかを回帰対象フォルダ解決で使います。
        public static bool HasSpectrumFiles(string folderPath) =>
            Directory.Exists(folderPath) && Directory.EnumerateFiles(folderPath, "*.*").Any(IsSpectrumFile);

        // 260430Codex: 鉱物名から実際の学習フォルダを解決する分岐を学習データ側に集約します。
        public static bool TryResolveMineralFolderPath(string trainingFolder, string mineralName, out string mineralFolderPath)
        {
            string combinedPath = Path.Combine(trainingFolder, mineralName);
            if (Directory.Exists(combinedPath))
            {
                mineralFolderPath = combinedPath;
                return true;
            }

            if (Path.GetFileName(trainingFolder) == mineralName || HasSpectrumFiles(trainingFolder))
            {
                mineralFolderPath = trainingFolder;
                return true;
            }

            mineralFolderPath = string.Empty;
            return false;
        }

        // 260430Codex: 鉱物フォルダ内の端成分ラベルを調べ、固溶体かどうかを判定します。
        public static MineralFolder? AnalyzeMineralFolder(string folderPath)
        {
            var files = GetSpectrumFiles(folderPath);

            if (files.Length == 0)
                return null;

            var mineralFolder = new MineralFolder
            {
                Name = Path.GetFileName(folderPath),
                FolderPath = folderPath
            };

            var allComponents = new HashSet<string>();

            foreach (var file in files)
            {
                var components = GetRegressionLabels(file);
                if (components is not { Count: > 0 })
                    continue;

                foreach (var (component, _) in components)
                    allComponents.Add(component);
            }

            mineralFolder.IsSolidSolution = allComponents.Count >= 2;
            mineralFolder.EndMembers = mineralFolder.IsSolidSolution
                ? allComponents.OrderBy(x => x).ToList()
                : [];

            return mineralFolder;
        }

        // 260430Codex: 2048 点スペクトルの読み込みと正規化を学習・予測で共通利用します。
        public static float[]? LoadNormalizedSpectrum(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var data = LoadMsaFile(filePath);
            if (data.shape[0] != SpectrumLength)
                return null;

            var values = data.ToArray<float>();
            NormalizeSpectrum(values);
            return values;
        }

        // 260514Codex: spectrum 1 ファイルの読み込み前後をキャンセル確認の最小単位としてそろえます。
        private static float[]? LoadNormalizedSpectrumWithCancellation(
            string filePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = LoadNormalizedSpectrum(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return data;
        }

        // 260430Codex: 最大値が 0 以下のスペクトルはそのまま返し、ゼロ除算を避けます。
        private static void NormalizeSpectrum(float[] values)
        {
            float max = values.Max();
            if (max <= 0)
                return;

            for (int i = 0; i < values.Length; i++)
                values[i] /= max;
        }

        // 260430Codex: MSA/EMSA の数値行だけを NDArray に変換します。
        public static NDArray LoadMsaFile(string filePath)
        {
            var yValues = new List<float>();

            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                line = line.TrimEnd(',');

                if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    yValues.Add(value);
            }

            return np.array(yValues.ToArray());
        }

        // 260507Codex: manifest の Completed から選ばれた spectrum だけを分類学習データへ変換します。
        // 260514Codex: spectrum ファイル単位の境界で分類データ読み込みのキャンセルを確認します。
        public static (NDArray Spectra, List<string> Labels) LoadClassificationData(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            CancellationToken cancellationToken = default)
        {
            var spectraList = new List<float[]>();
            var labels = new List<string>();

            foreach (var pool in trainingPools)
            {
                foreach (var sample in pool.Samples)
                {
                    var data = LoadNormalizedSpectrumWithCancellation(sample.FilePath, cancellationToken);
                    if (data == null)
                        continue;

                    spectraList.Add(data);
                    labels.Add(pool.MineralName);
                }
            }

            if (spectraList.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), labels);

            return (CreateSpectraArray(spectraList), labels);
        }

        // 260507Codex: manifest の endmemberFractions を回帰ラベルの正本として使います。
        // 260514Codex: spectrum ファイル単位の境界で manifest 回帰データ読み込みのキャンセルを確認します。
        public static (NDArray Spectra, NDArray Labels, Dictionary<string, int>? ComponentIndex) LoadRegressionData(
            SpectrumTrainingPool trainingPool,
            CancellationToken cancellationToken = default)
        {
            if (trainingPool.EndmemberNames.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 0)), null);

            var componentOrder = trainingPool.EndmemberNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

            if (componentOrder.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 0)), null);

            var componentIndex = componentOrder
                .Select((component, index) => new { component, index })
                .ToDictionary(x => x.component, x => x.index);

            var spectraList = new List<float[]>();
            var labelsList = new List<IReadOnlyDictionary<string, double>>();

            foreach (var sample in trainingPool.Samples)
            {
                var data = LoadNormalizedSpectrumWithCancellation(sample.FilePath, cancellationToken);
                if (data == null)
                    continue;

                spectraList.Add(data);
                labelsList.Add(sample.EndmemberFractions);
            }

            if (spectraList.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, componentOrder.Count)), null);

            float[,] labelsArray = new float[spectraList.Count, componentOrder.Count];
            for (int i = 0; i < labelsList.Count; i++)
            {
                var labelDict = labelsList[i];
                for (int j = 0; j < componentOrder.Count; j++)
                {
                    string componentName = componentOrder[j];
                    labelsArray[i, j] = labelDict.TryGetValue(componentName, out double ratio)
                        ? (float)ratio
                        : 0f;
                }
            }

            return (CreateSpectraArray(spectraList), np.array(labelsArray), componentIndex);
        }

        // 260430Codex: 回帰モデル用のスペクトル行列、ラベル行列、端成分 index を作ります。
        // 260514Codex: フォルダ走査の回帰データ読み込みも spectrum ファイル境界でキャンセルを確認します。
        public static (NDArray Spectra, NDArray Labels, Dictionary<string, int>? ComponentIndex) LoadRegressionData(
            string directory,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(directory))
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 2)), null);

            var files = GetSpectrumFiles(directory);
            var spectraList = new List<float[]>();
            var labelsList = new List<Dictionary<string, float>>();
            var allComponents = new HashSet<string>();

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var components = GetRegressionLabels(filePath);
                cancellationToken.ThrowIfCancellationRequested();
                if (components is not { Count: > 0 })
                    continue;

                foreach (var (component, _) in components)
                    allComponents.Add(component);
            }

            if (allComponents.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 2)), null);

            var componentOrder = allComponents.OrderBy(x => x).ToList();
            var componentIndex = componentOrder.Select((component, index) => new { component, index })
                .ToDictionary(x => x.component, x => x.index);

            foreach (var filePath in files)
            {
                var data = LoadNormalizedSpectrumWithCancellation(filePath, cancellationToken);
                var components = GetRegressionLabels(filePath);
                cancellationToken.ThrowIfCancellationRequested();

                if (data == null || components is not { Count: > 0 })
                    continue;

                spectraList.Add(data);
                labelsList.Add(components.ToDictionary(component => component.Component, component => component.Ratio));
            }

            if (spectraList.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, componentOrder.Count)), null);

            int numComponents = componentOrder.Count;
            float[,] labelsArray = new float[spectraList.Count, numComponents];
            for (int i = 0; i < spectraList.Count; i++)
            {
                var labelDict = labelsList[i];
                for (int j = 0; j < numComponents; j++)
                {
                    string componentName = componentOrder[j];
                    labelsArray[i, j] = labelDict.ContainsKey(componentName) ? labelDict[componentName] : 0f;
                }
            }

            return (CreateSpectraArray(spectraList), np.array(labelsArray), componentIndex);
        }

        // 260430Codex: 回帰用の端成分比率をファイル名から抽出します。
        public static List<(string Component, float Ratio)>? GetRegressionLabels(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            var pattern = @"([a-zA-Z0-9\s]+?)(0\.\d+|1\.0+)";
            var matches = Regex.Matches(fileName, pattern);

            var components = new List<(string, float)>();

            foreach (Match match in matches)
            {
                if (match.Groups.Count < 3)
                    continue;

                string componentName = match.Groups[1].Value;
                string ratioText = match.Groups[2].Value;

                if (float.TryParse(ratioText, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
                    components.Add((componentName, ratio));
            }

            return components.Count > 0 ? components : null;
        }

        // 260430Codex: 分類モデル用のスペクトル行列と鉱物ラベル一覧を作ります。
        // 260514Codex: フォルダ走査の分類データ読み込みも spectrum ファイル境界でキャンセルを確認します。
        public static (NDArray Spectra, List<string> Labels) LoadClassificationData(
            string trainingFolder,
            IReadOnlyCollection<string> targetMinerals,
            CancellationToken cancellationToken = default)
        {
            var spectraList = new List<float[]>();
            var labels = new List<string>();

            foreach (var mineralName in targetMinerals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolveMineralFolderPath(trainingFolder, mineralName, out var mineralFolderPath))
                    continue;

                string labelName = mineralName.Split('_')[0];
                var files = GetSpectrumFiles(mineralFolderPath);

                foreach (var filePath in files)
                {
                    var data = LoadNormalizedSpectrumWithCancellation(filePath, cancellationToken);
                    if (data == null)
                        continue;

                    spectraList.Add(data);
                    labels.Add(labelName);
                }
            }

            if (spectraList.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), labels);

            return (CreateSpectraArray(spectraList), labels);
        }

        // 260430Codex: 分類・回帰で共通のスペクトル配列化処理を 1 か所にまとめます。
        private static NDArray CreateSpectraArray(IReadOnlyList<float[]> spectraList)
        {
            float[,] spectraArray = new float[spectraList.Count, SpectrumLength];
            for (int i = 0; i < spectraList.Count; i++)
            {
                for (int j = 0; j < SpectrumLength; j++)
                    spectraArray[i, j] = spectraList[i][j];
            }

            return np.array(spectraArray);
        }
    }
}
