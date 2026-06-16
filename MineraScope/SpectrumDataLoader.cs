using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
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

        // 260607Codex: Bound classification file-open parallelism and allow measurement fallback without code changes.
        private const int MaxClassificationLoadParallelism = 8;
        private const string ClassificationLoadParallelismEnvironmentVariable = "MINERASCOPE_CLASSIFICATION_LOAD_PARALLELISM";

        // 260607Codex: Classification load diagnostics make parallel data-load measurements comparable across runs.
        internal readonly record struct ClassificationLoadStats(
            int InputSamples,
            int LoadedSamples,
            int SkippedSamples,
            int ParallelDegree);

        // 260606Codex: 1 回の学習 run 内で分類から回帰へ正規化済み spectrum を受け渡し、同一ファイルの再読込を避けます。
        internal sealed class NormalizedSpectrumCache
        {
            private readonly HashSet<string> _cacheablePaths;
            private readonly Dictionary<string, float[]> _spectra;

            public NormalizedSpectrumCache(IEnumerable<string> filePaths)
            {
                ArgumentNullException.ThrowIfNull(filePaths);

                _cacheablePaths = new HashSet<string>(
                    filePaths
                        .Where(path => !string.IsNullOrWhiteSpace(path)),
                    StringComparer.OrdinalIgnoreCase);
                _spectra = new Dictionary<string, float[]>(_cacheablePaths.Count, StringComparer.OrdinalIgnoreCase);
            }

            public int CacheablePathCount => _cacheablePaths.Count;

            public int CachedCount => _spectra.Count;

            public int HitCount { get; private set; }

            public int MissCount { get; private set; }

            public int StoreCount { get; private set; }

            public bool TryGet(string filePath, out float[]? values)
            {
                if (!_cacheablePaths.Contains(filePath))
                {
                    values = null;
                    return false;
                }

                if (_spectra.TryGetValue(filePath, out values))
                {
                    HitCount++;
                    return true;
                }

                MissCount++;
                return false;
            }

            public bool ShouldStore(string filePath) => _cacheablePaths.Contains(filePath);

            public void StoreIfNeeded(string filePath, float[] values)
            {
                if (!_cacheablePaths.Contains(filePath) || _spectra.ContainsKey(filePath))
                    return;

                _spectra.Add(filePath, values);
                StoreCount++;
            }
        }

        // 260430Codex: 2048 点スペクトルの読み込みと正規化を学習・予測で共通利用します。
        public static float[]? LoadNormalizedSpectrum(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            // 260613Claude: バイナリ .eds は専用リーダーで int カウント列を取り、テキスト .msa と同じ正規化に合流させる。
            float[]? values = EdsSpectrumReader.IsEdsFile(filePath)
                ? LoadEdsValues(filePath)
                : LoadMsaValues(filePath);
            if (values is null)
                return null;

            NormalizeSpectrum(values);
            return values;
        }

        // 260613Claude: .eds の 2048ch int カウントを float 配列へ写し、欠損時は null で判定をスキップする。
        private static float[]? LoadEdsValues(string filePath)
        {
            int[]? counts = EdsSpectrumReader.TryReadCounts(filePath);
            if (counts is null)
                return null;

            var values = new float[SpectrumLength];
            for (int i = 0; i < SpectrumLength; i++)
                values[i] = counts[i];

            return values;
        }

        // 260613Claude: .msa の数値行を読み、点数が一致しない場合は null を返す。
        private static float[]? LoadMsaValues(string filePath)
        {
            var data = LoadMsaFile(filePath);
            return data.shape[0] != SpectrumLength ? null : data.ToArray<float>();
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

        // 260526Claude: ブロックカウントを batch 行列の指定行へ max 正規化して書き込む。全ゼロなら false（未判定扱い）。
        // 除算の仕方を CreateNormalizedSpectrum と揃え、同一カウントならクリック側とビット一致する正規化にする。
        public static bool NormalizeInto(ReadOnlySpan<int> counts, float[,] destination, int row)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (counts.Length != SpectrumLength)
                throw new ArgumentException($"{SpectrumLength} 点のスペクトルだけを正規化できます。", nameof(counts));

            int max = 0;
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > max)
                    max = counts[i];

            if (max <= 0)
                return false;

            float maxValue = max;
            for (int i = 0; i < counts.Length; i++)
                destination[row, i] = counts[i] / maxValue;

            return true;
        }

        // 260514Codex: spectrum 1 ファイルの読み込み前後をキャンセル確認の最小単位としてそろえます。
        private static float[]? LoadNormalizedSpectrumWithCancellation(
            string filePath,
            CancellationToken cancellationToken,
            NormalizedSpectrumCache? cache = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache?.TryGet(filePath, out var cachedData) == true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return cachedData;
            }

            var data = LoadNormalizedSpectrum(filePath);
            if (data is not null)
                cache?.StoreIfNeeded(filePath, data);

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
        public static (NDArray Spectra, List<string> Labels, ClassificationLoadStats Stats) LoadClassificationData(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            CancellationToken cancellationToken = default,
            NormalizedSpectrumCache? cache = null)
        {
            var samples = trainingPools
                .SelectMany(pool => pool.Samples.Select(sample => new ClassificationLoadSample(sample, pool.MineralName)))
                .ToArray();
            var labels = new List<string>(samples.Length);
            int parallelDegree = GetClassificationLoadParallelDegree(samples.Length);

            var loadedSpectra = LoadClassificationSpectraInParallel(samples, parallelDegree, cancellationToken);
            var spectraList = new List<float[]>(samples.Length);
            var spectraToCache = cache is null ? null : new List<(string FilePath, float[] Values)>();

            for (int i = 0; i < samples.Length; i++)
            {
                var data = loadedSpectra[i];
                if (data == null)
                    continue;

                spectraList.Add(data);
                labels.Add(samples[i].Label);
                if (cache?.ShouldStore(samples[i].Sample.FilePath) == true)
                    spectraToCache!.Add((samples[i].Sample.FilePath, data));
            }

            var stats = new ClassificationLoadStats(
                samples.Length,
                spectraList.Count,
                samples.Length - spectraList.Count,
                parallelDegree);

            if (spectraList.Count == 0)
                return (np.zeros(new Shape(0, SpectrumLength)), labels, stats);

            var spectra = CreateSpectraArray(spectraList);
            if (spectraToCache is not null)
            {
                foreach (var item in spectraToCache)
                    cache!.StoreIfNeeded(item.FilePath, item.Values);
            }

            return (spectra, labels, stats);
        }

        // 260507Codex: manifest の endmemberFractions を回帰ラベルの正本として使います。
        // 260514Codex: spectrum ファイル単位の境界で manifest 回帰データ読み込みのキャンセルを確認します。
        public static (NDArray Spectra, NDArray Labels, Dictionary<string, int>? ComponentIndex) LoadRegressionData(
            SpectrumTrainingPool trainingPool,
            CancellationToken cancellationToken = default,
            NormalizedSpectrumCache? cache = null)
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
                var data = LoadNormalizedSpectrumWithCancellation(sample.FilePath, cancellationToken, cache);
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

        // 260607Codex: Keep the parser unchanged while overlapping the many small classification file opens.
        private static float[]?[] LoadClassificationSpectraInParallel(
            IReadOnlyList<ClassificationLoadSample> samples,
            int parallelDegree,
            CancellationToken cancellationToken)
        {
            var loadedSpectra = new float[]?[samples.Count];
            if (samples.Count == 0)
                return loadedSpectra;

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelDegree
            };

            try
            {
                Parallel.For(0, samples.Count, options, index =>
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    loadedSpectra[index] = LoadNormalizedSpectrumWithCancellation(
                        samples[index].Sample.FilePath,
                        options.CancellationToken);
                });
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return loadedSpectra;
        }

        // 260607Codex: Limit parallel file opens conservatively, with an environment override for measurement fallback.
        private static int GetClassificationLoadParallelDegree(int sampleCount)
        {
            if (sampleCount <= 1)
                return 1;

            string? configured = Environment.GetEnvironmentVariable(ClassificationLoadParallelismEnvironmentVariable);
            if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int configuredDegree)
                && configuredDegree > 0)
                return Math.Min(sampleCount, configuredDegree);

            return Math.Min(sampleCount, Math.Clamp(Environment.ProcessorCount, 2, MaxClassificationLoadParallelism));
        }

        // 260607Codex: Pair each manifest sample with its classification label before parallel loading.
        private sealed record ClassificationLoadSample(
            SpectrumTrainingSample Sample,
            string Label);

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
