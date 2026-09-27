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
        // 260803Codex: 次フェーズで実行時軸を配線できるよう、コンパイル時定数を既定軸由来の値へ置き換えます。
        public static readonly int SpectrumLength = SpectrumAxis.Default.ChannelCount;

        // 260607Codex: Bound classification file-open parallelism.
        // 260626Claude: 実験用 env 上書き (MINERASCOPE_CLASSIFICATION_LOAD_PARALLELISM) を撤去し、コア数由来の既定だけを使う。
        private const int MaxClassificationLoadParallelism = 8;

        // 260901Codex: 一時的なファイルロックに対し、分類学習の読込だけを短時間再試行します。
        private static readonly int[] ClassificationReadRetryDelaysMilliseconds = [100, 200, 400];

        // 260902Codex: 分類学習を止めた原因を、表示と診断ログで同じ分類として扱います。
        internal enum ClassificationLoadFailureKind
        {
            PointCountMismatch,
            EdsLayoutMismatch,
            ReadFailure
        }

        // 260902Codex: 並列読込後も入力順を保って報告できるよう、失敗したファイルと構造化した理由を保持します。
        internal sealed record ClassificationLoadFailure(
            string FilePath,
            ClassificationLoadFailureKind Kind,
            int? ActualPointCount = null,
            string? ExceptionType = null,
            string? ExceptionMessage = null);

        // 260607Codex: Classification load diagnostics make parallel data-load measurements comparable across runs.
        internal readonly record struct ClassificationLoadStats(
            int InputSamples,
            int LoadedSamples,
            int SkippedSamples,
            int ParallelDegree);

        // 260902Codex: 数値統計と修復対象の一覧を分けたまま、分類読込の結果を一まとまりで返します。
        internal sealed record ClassificationLoadResult(
            NDArray Spectra,
            List<string> Labels,
            ClassificationLoadStats Stats,
            IReadOnlyList<ClassificationLoadFailure> Failures);

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
        // 260626Claude: preprocessing 未指定は None = 従来どおりマスク無し正規化。予測経路はモデルの前処理を渡す。
        // 260901Codex: 公開経路は例外を握る従来の EDS リーダーを使い、予測と較正の挙動を維持します。
        public static float[]? LoadNormalizedSpectrum(string filePath, SpectrumPreprocessing? preprocessing = null)
        {
            if (!File.Exists(filePath))
                return null;

            // 260613Claude: バイナリ .eds は専用リーダーで int カウント列を取り、テキスト .msa と同じ正規化に合流させる。
            float[]? values = EdsSpectrumReader.IsEdsFile(filePath)
                ? CreateEdsValues(EdsSpectrumReader.TryReadCounts(filePath))
                : LoadMsaValues(filePath);
            if (values is null)
                return null;

            NormalizeSpectrum(values, preprocessing ?? SpectrumPreprocessing.None);
            return values;
        }

        // 260613Claude: .eds の 2048ch int カウントを float 配列へ写し、欠損時は null で判定をスキップする。
        // 260901Codex: ファイル読込と配列変換を分け、学習用リトライと既存経路で変換を共有します。
        private static float[]? CreateEdsValues(int[]? counts)
        {
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

        // 260521Codex: PTS の 1 画素スペクトルを、学習済みモデルが使うのと同じ 2048 点の正規化済み入力へ変換する。
        // 260626Claude: preprocessing 未指定は None。PTS クリック予測はモデルの前処理を渡す。
        // 260825Claude: 信号の有無を NormalizeInto と同じくここで返す。X 線が 1 つも無い範囲を呼び出し側で判定し直すと、
        //   同じ規則が 2 か所に分かれるうえ、マスク適用後の全ゼロ判定とマスク除外後の max 判定で意味がずれる。
        public static float[]? CreateNormalizedSpectrum(PtsPixelSpectrum spectrum, SpectrumPreprocessing? preprocessing, out bool hasSignal)
        {
            hasSignal = false;
            if (spectrum.ChannelCount != SpectrumLength)
                return null;

            float[] values = new float[SpectrumLength];
            for (int channel = 0; channel < SpectrumLength; channel++)
                values[channel] = spectrum.GetCount(channel);

            hasSignal = NormalizeSpectrum(values, preprocessing ?? SpectrumPreprocessing.None);
            return values;
        }

        // 260526Claude: ブロックカウントを batch 行列の指定行へ max 正規化して書き込む。全ゼロなら false（未判定扱い）。
        // 除算の仕方を CreateNormalizedSpectrum と揃え、同一カウントならクリック側とビット一致する正規化にする。
        // 260626Claude: preprocessing=None なら従来どおり。pre はマスク範囲を max 探索から除外、post は全ch max。
        //   いずれもマスク範囲は出力 0。preprocessing 未指定は None なので PTS マップの既定挙動は不変。
        // 260828Codex: 同一入力の None/pre/post で NormalizeSpectrum と信号判定・正規化出力が一致することを確認済み。
        //   PTS マップは配列生成を避けて行バッファーへ直接書き込むため、性能上の理由から二実装を意図的に維持する。
        public static bool NormalizeInto(ReadOnlySpan<int> counts, float[,] destination, int row, SpectrumPreprocessing? preprocessing = null)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (counts.Length != SpectrumLength)
                throw new ArgumentException($"{SpectrumLength} 点のスペクトルだけを正規化できます。", nameof(counts));

            var pp = preprocessing ?? SpectrumPreprocessing.None;
            int maskCount = pp.HasLowEnergyMask ? Math.Min(pp.MaskChannelCount, counts.Length) : 0;
            int maxSearchStart = pp.HasLowEnergyMask && pp.MaskBeforeNormalize ? maskCount : 0;

            int max = 0;
            if (pp.HasLowEnergyMask && !pp.MaskBeforeNormalize)
            {
                bool hasUnmaskedSignal = false;
                for (int i = 0; i < counts.Length; i++)
                {
                    int count = counts[i];
                    if (count > max)
                        max = count;
                    if (i >= maskCount && count != 0)
                        hasUnmaskedSignal = true;
                }

                if (!hasUnmaskedSignal)
                    return false;
            }
            else
            {
                for (int i = maxSearchStart; i < counts.Length; i++)
                    if (counts[i] > max)
                        max = counts[i];
            }

            if (max <= 0)
                return false;

            float maxValue = max;
            for (int i = 0; i < counts.Length; i++)
                destination[row, i] = i < maskCount ? 0f : counts[i] / maxValue;

            return true;
        }

        // 260514Codex: spectrum 1 ファイルの読み込み前後をキャンセル確認の最小単位としてそろえます。
        // 260626Claude: 学習側も preprocessing を中核 normalize まで素通しする。未指定は None。
        private static float[]? LoadNormalizedSpectrumWithCancellation(
            string filePath,
            CancellationToken cancellationToken,
            NormalizedSpectrumCache? cache = null,
            SpectrumPreprocessing? preprocessing = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache?.TryGet(filePath, out var cachedData) == true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return cachedData;
            }

            var data = LoadNormalizedSpectrum(filePath, preprocessing);
            if (data is not null)
                cache?.StoreIfNeeded(filePath, data);

            cancellationToken.ThrowIfCancellationRequested();
            return data;
        }

        // 260430Codex: 最大値が 0 以下のスペクトルはそのまま返し、ゼロ除算を避けます。
        // 260626Claude: pre は max 正規化の前に低エネルギーを 0 化(基準が C コンタミに引っ張られない)、post は正規化後に 0 化(ablation)。
        //   preprocessing=None なら従来どおり全ch max 正規化のみ。
        // 260825Claude: 除数(max)の求め方は従来どおり。戻り値は「マスク後に値が残ったか」で、false は分類に使えない全ゼロを表す。
        private static bool NormalizeSpectrum(float[] values, SpectrumPreprocessing preprocessing)
        {
            if (preprocessing.HasLowEnergyMask && preprocessing.MaskBeforeNormalize)
                preprocessing.ZeroLeadingChannels(values);

            float max = values.Max();
            if (max <= 0)
                return false;

            for (int i = 0; i < values.Length; i++)
                values[i] /= max;

            if (preprocessing.HasLowEnergyMask && !preprocessing.MaskBeforeNormalize)
                preprocessing.ZeroLeadingChannels(values);

            // 260826Claude: マスク無しと pre マスクでは max を取る前にマスク済みなので、max>0 の時点で信号ありが確定し、
            //   NormalizeInto の max<=0 判定と結論が一致する。走査が要るのは post マスク (ablation 用) だけ。
            //   そこでは低エネルギー側だけにカウントがある場合にここだけ false になり、全ch max で判定する
            //   NormalizeInto は true を返す。分類器へ全ゼロを渡さない点でこちらが厳しい側だが、両者は一致していない。
            // 260828Codex: 上記の差は NormalizeInto の post 専用走査で解消済み。
            if (!preprocessing.HasLowEnergyMask || preprocessing.MaskBeforeNormalize)
                return true;

            for (int i = Math.Min(preprocessing.MaskChannelCount, values.Length); i < values.Length; i++)
                if (values[i] != 0f)
                    return true;

            return false;
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
        public static ClassificationLoadResult LoadClassificationData(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            CancellationToken cancellationToken = default,
            NormalizedSpectrumCache? cache = null,
            SpectrumPreprocessing? preprocessing = null)
        {
            var samples = trainingPools
                .SelectMany(pool => pool.Samples.Select(sample => new ClassificationLoadSample(sample, pool.MineralName)))
                .ToArray();
            var labels = new List<string>(samples.Length);
            int parallelDegree = GetClassificationLoadParallelDegree(samples.Length);

            var loadResults = LoadClassificationSpectraInParallel(samples, parallelDegree, cancellationToken, preprocessing);
            var spectraList = new List<float[]>(samples.Length);
            var failures = new List<ClassificationLoadFailure>();
            var spectraToCache = cache is null ? null : new List<(string FilePath, float[] Values)>();

            for (int i = 0; i < samples.Length; i++)
            {
                var loadResult = loadResults[i];
                if (loadResult.Failure is not null)
                {
                    failures.Add(loadResult.Failure);
                    continue;
                }

                var data = loadResult.Spectrum
                    ?? throw new InvalidOperationException("分類スペクトルの読込結果にデータも失敗理由もありません。");

                spectraList.Add(data);
                labels.Add(samples[i].Label);
                if (cache?.ShouldStore(samples[i].Sample.FilePath) == true)
                    spectraToCache!.Add((samples[i].Sample.FilePath, data));
            }

            var stats = new ClassificationLoadStats(
                samples.Length,
                spectraList.Count,
                failures.Count,
                parallelDegree);

            // 260903Codex: 成功0件と通常時の結果生成を共通化し、失敗一覧の組立てを一度だけにします。
            var spectra = spectraList.Count == 0
                ? np.zeros(new Shape(0, SpectrumLength))
                : CreateSpectraArray(spectraList);
            if (spectraToCache is not null)
            {
                foreach (var item in spectraToCache)
                    cache!.StoreIfNeeded(item.FilePath, item.Values);
            }

            return new ClassificationLoadResult(spectra, labels, stats, failures.ToArray());
        }

        // 260507Codex: manifest の endmemberFractions を回帰ラベルの正本として使います。
        // 260514Codex: spectrum ファイル単位の境界で manifest 回帰データ読み込みのキャンセルを確認します。
        public static (NDArray Spectra, NDArray Labels, Dictionary<string, int>? ComponentIndex) LoadRegressionData(
            SpectrumTrainingPool trainingPool,
            CancellationToken cancellationToken = default,
            NormalizedSpectrumCache? cache = null,
            SpectrumPreprocessing? preprocessing = null)
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
                var data = LoadNormalizedSpectrumWithCancellation(sample.FilePath, cancellationToken, cache, preprocessing);
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
        // 260626Claude: preprocessing を各並列読み込みへ素通しする。未指定は None。
        private static ClassificationSpectrumLoadResult[] LoadClassificationSpectraInParallel(
            IReadOnlyList<ClassificationLoadSample> samples,
            int parallelDegree,
            CancellationToken cancellationToken,
            SpectrumPreprocessing? preprocessing)
        {
            var loadResults = new ClassificationSpectrumLoadResult[samples.Count];
            if (samples.Count == 0)
                return loadResults;

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelDegree
            };

            try
            {
                // 260901Codex: リトライは分類学習の並列読込だけに限定し、回帰は既存キャッシュ経路を維持します。
                Parallel.For(0, samples.Count, options, index =>
                {
                    // 260903Codex: キャンセル確認は直後のリトライ処理へ集約します。
                    loadResults[index] = LoadClassificationSpectrumWithRetry(
                        samples[index].Sample.FilePath,
                        options.CancellationToken,
                        preprocessing);
                });
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return loadResults;
        }

        // 260901Codex: 初回と最大3回の再試行を行い、待機中のキャンセルにも直ちに応答します。
        private static ClassificationSpectrumLoadResult LoadClassificationSpectrumWithRetry(
            string filePath,
            CancellationToken cancellationToken,
            SpectrumPreprocessing? preprocessing)
        {
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = LoadClassificationSpectrum(filePath, preprocessing);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                }
                catch (IOException ex) when (
                    attempt < ClassificationReadRetryDelaysMilliseconds.Length
                    && IsRetryableClassificationRead(ex))
                {
                    // 260903Codex: 次の反復冒頭でキャンセルを確認するため、待機後の重複確認を省きます。
                    cancellationToken.WaitHandle.WaitOne(ClassificationReadRetryDelaysMilliseconds[attempt]);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return CreateReadFailure(filePath, exception);
                }
            }
        }

        // 260902Codex: 形式不一致は再試行せず、I/O 例外だけを外側の短時間リトライへ渡します。
        private static ClassificationSpectrumLoadResult LoadClassificationSpectrum(
            string filePath,
            SpectrumPreprocessing? preprocessing)
        {
            float[] values;
            if (EdsSpectrumReader.IsEdsFile(filePath))
            {
                int[]? counts = EdsSpectrumReader.ReadCounts(filePath);
                if (counts is null)
                    return new ClassificationSpectrumLoadResult(
                        Spectrum: null,
                        Failure: new ClassificationLoadFailure(filePath, ClassificationLoadFailureKind.EdsLayoutMismatch));

                values = CreateEdsValues(counts)!;
            }
            else
            {
                var data = LoadMsaFile(filePath);
                int actualPointCount = (int)data.shape[0];
                if (actualPointCount != SpectrumLength)
                    return new ClassificationSpectrumLoadResult(
                        Spectrum: null,
                        Failure: new ClassificationLoadFailure(
                            filePath,
                            ClassificationLoadFailureKind.PointCountMismatch,
                            ActualPointCount: actualPointCount));

                values = data.ToArray<float>();
            }

            NormalizeSpectrum(values, preprocessing ?? SpectrumPreprocessing.None);
            return new ClassificationSpectrumLoadResult(values, Failure: null);
        }

        // 260902Codex: 予期されたファイル読込失敗だけを型付き結果へ変換し、その他の例外は上へ伝えます。
        private static ClassificationSpectrumLoadResult CreateReadFailure(string filePath, Exception exception) =>
            new(
                Spectrum: null,
                Failure: new ClassificationLoadFailure(
                    filePath,
                    ClassificationLoadFailureKind.ReadFailure,
                    ExceptionType: exception.GetType().Name,
                    ExceptionMessage: exception.Message));

        private static bool IsRetryableClassificationRead(IOException exception) =>
            exception is not FileNotFoundException
            and not DirectoryNotFoundException
            and not DriveNotFoundException
            and not PathTooLongException
            and not EndOfStreamException;

        // 260607Codex: Limit parallel file opens conservatively.
        // 260626Claude: コア数を 2..8 にクランプした既定だけで決める (env 上書きは廃止)。
        private static int GetClassificationLoadParallelDegree(int sampleCount)
        {
            if (sampleCount <= 1)
                return 1;

            return Math.Min(sampleCount, Math.Clamp(Environment.ProcessorCount, 2, MaxClassificationLoadParallelism));
        }

        // 260607Codex: Pair each manifest sample with its classification label before parallel loading.
        private sealed record ClassificationLoadSample(
            SpectrumTrainingSample Sample,
            string Label);

        private readonly record struct ClassificationSpectrumLoadResult(
            float[]? Spectrum,
            ClassificationLoadFailure? Failure);

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
