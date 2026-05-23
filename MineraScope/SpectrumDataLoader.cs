using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

        // 260521Codex: Converts one PTS pixel spectrum into the same normalized 2048 point input used by trained models.
        public static float[]? CreateNormalizedSpectrum(PtsPixelSpectrum spectrum)
        {
            if (spectrum.ChannelCount != SpectrumLength)
                return null;

            float[] values = new float[SpectrumLength];
            for (int channel = 0; channel < SpectrumLength; channel++)
                values[channel] = spectrum.GetCount(channel);

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
