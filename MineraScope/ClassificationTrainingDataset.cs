using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MineraScope
{
    // 260908Codex: 後で再利用する正規化行列とは独立して、元の行の識別情報とバイト列を保持する。
    internal sealed record ClassificationTrainingSampleInfo(
        string FilePath,
        string MineralName,
        int Label,
        string SourceSha256,
        SpectrumTrainingSource? Source,
        // 260917Codex: 行とともに組成を固定し、nullを省略して旧形式のデータセットダイジェストを保持する。
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, double>? EndmemberFractions = null);

    // 260908Codex: データセットのダイジェストに、正規化値・元の識別情報・ラベル・前処理・明示的な分割順序を結び付ける。
    internal sealed record ClassificationTrainingDatasetInfo(
        int SchemaVersion,
        int SampleCount,
        int SpectrumLength,
        string[] ClassNames,
        float ValidationSplit,
        int SplitSeed,
        int[] TrainingIndices,
        int[] ValidationIndices,
        ClassificationTrainingSampleInfo[] Samples,
        SpectrumPreprocessing Preprocessing,
        string NormalizedDataSha256,
        string DatasetSha256);

    // 260908Codex: 測定条件ごとに不変の入力スナップショットを1つ作り、後続の試行では実poolのファイルを読まない。
    // 261001Codex: 通常のhead作成はSeed試行の実行処理を取り込まず、不変スナップショットを所有する。
    internal static class ClassificationTrainingDataset
    {
        public const string InfoFileName = "dataset.json";
        public const string DataFileName = "normalizedSpectra.bin";
        private const int BinaryMagic = 0x4D535344;
        // 260917Claude: DeepLearning.SeedExperimentのSeed試行レコードと共用する。
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static ClassificationTrainingDatasetInfo Create(
            IReadOnlyList<SpectrumTrainingPool> pools,
            ModelTrainingSettings settings,
            string datasetFolder,
            CancellationToken cancellationToken,
            Action<string> log)
        {
            ArgumentNullException.ThrowIfNull(pools);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(datasetFolder);
            ArgumentNullException.ThrowIfNull(log);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSettings(settings);
            if (Directory.Exists(datasetFolder) && Directory.EnumerateFileSystemEntries(datasetFolder).Any())
                throw new IOException($"学習データの保存先が空ではありません: {datasetFolder}");

            // 260908Codex: RunTrainingの既存の鉱物順と、カルチャに依存するエンコーダーの順序を一度だけ確定して保存する。
            var orderedPools = pools.Where(pool => pool.Samples.Count > 0).OrderBy(pool => pool.MineralName).ToArray();
            var rows = orderedPools.SelectMany(pool => pool.Samples.Select(sample => (pool.MineralName, Sample: sample))).ToArray();
            DeepLearning.ResolveSourceMetadataMode(rows.Select(row => row.Sample).ToArray(), "classification:OCN");
            string[] classNames = rows.Select(row => row.MineralName).Distinct().OrderBy(name => name).ToArray();
            if (classNames.Length < 2 || classNames.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("分類＋OCNの学習には2種類以上の鉱物が必要です。");
            var encoder = classNames.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index);
            var (trainIndices, validationIndices) = CreateSplitIndices(rows.Length, settings.ValidationSplit);
            if (trainIndices.Length < settings.BatchSize || validationIndices.Length == 0)
                throw new InvalidDataException("学習データには1バッチ以上、検証データには1件以上のスペクトルが必要です。");
            var preprocessing = SpectrumPreprocessing.ForTraining();
            var samples = new ClassificationTrainingSampleInfo[rows.Length];
            for (int index = 0; index < rows.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(rows[index].Sample.FilePath);
                samples[index] = new(path, rows[index].MineralName, encoder[rows[index].MineralName],
                    ComputeFileSha256(path, cancellationToken), rows[index].Sample.Source, rows[index].Sample.EndmemberFractions);
            }
            log($"学習データを固定しています: {rows.Length}件、{classNames.Length}クラス");
            Directory.CreateDirectory(datasetFolder);
            string dataPath = Path.Combine(datasetFolder, DataFileName);

            // 260908Codex: 既存の読込・正規化を維持し、行の失敗時はラベルをずらさずスナップショット作成を中断する。
            TensorFlowExecutor.Run(() =>
            {
                var loaded = SpectrumDataLoader.LoadClassificationData(orderedPools, cancellationToken, cache: null, preprocessing);
                using var spectra = loaded.Spectra;
                if (loaded.Failures.Count > 0 || (int)spectra.shape[0] != rows.Length)
                    throw new InvalidDataException($"学習データを読み込めませんでした: {loaded.Failures.Count}件。"
                        + string.Join(Environment.NewLine, loaded.Failures.Take(10).Select(failure => failure.FilePath)));
                if (!loaded.Labels.SequenceEqual(samples.Select(sample => sample.MineralName)))
                    throw new InvalidDataException("読み込んだスペクトルと正解ラベルの順序が一致しません。");
                float[] values = spectra.ToArray<float>();
                using var stream = new FileStream(dataPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);
                writer.Write(BinaryMagic);
                writer.Write(1);
                writer.Write(rows.Length);
                writer.Write(SpectrumDataLoader.SpectrumLength);
                for (int row = 0; row < rows.Length; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rowValues = values.AsSpan(row * SpectrumDataLoader.SpectrumLength, SpectrumDataLoader.SpectrumLength);
                    foreach (float value in rowValues)
                    {
                        if (!float.IsFinite(value))
                            throw new InvalidDataException($"学習スペクトルに有限でない値があります: {samples[row].FilePath}");
                    }
                    // 260908Codex: x64 Windows形式はリトルエンディアンなので、値ごとの膨大なI/Oを避けて1行をまとめて書き込む。
                    writer.Write(MemoryMarshal.AsBytes(rowValues));
                }
                return true;
            });

            // 260908Codex: 先頭側の行も含め、読込後に全入力元を再検査して並行した入力変更を暗黙に受け入れない。
            foreach (var sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(sample.SourceSha256, ComputeFileSha256(sample.FilePath, cancellationToken), StringComparison.Ordinal))
                    throw new TrainingInputChangedException($"学習データの固定中にファイルが変更されました: {sample.FilePath}");
            }
            var info = new ClassificationTrainingDatasetInfo(1, rows.Length, SpectrumDataLoader.SpectrumLength, classNames,
                settings.ValidationSplit, DeepLearningDataSplitter.DefaultRandomState, trainIndices, validationIndices,
                samples, preprocessing, ComputeFileSha256(dataPath, cancellationToken), "");
            info = info with { DatasetSha256 = ComputeInfoSha256(info) };
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(Path.Combine(datasetFolder, InfoFileName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, info, JsonOptions);
            log($"学習データの固定完了: 学習{trainIndices.Length}件、検証{validationIndices.Length}件");
            return info;
        }

        // 260908Codex: 再開では完了マーカーの存在だけでなく、両ファイルを確認する。
        public static ClassificationTrainingDatasetInfo ReadInfo(string datasetFolder, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = JsonSerializer.Deserialize<ClassificationTrainingDatasetInfo>(File.ReadAllText(Path.Combine(datasetFolder, InfoFileName)), JsonOptions)
                ?? throw new InvalidDataException("固定した学習データの記録が空です。");
            ValidateInfo(info);
            if (!string.Equals(info.DatasetSha256, ComputeInfoSha256(info), StringComparison.Ordinal)
                || !string.Equals(info.NormalizedDataSha256, ComputeFileSha256(Path.Combine(datasetFolder, DataFileName), cancellationToken), StringComparison.Ordinal))
                throw new InvalidDataException("固定した学習データが変更または破損しています。");
            return info;
        }

        // 260908Codex: 明示的なインデックスで、分割Seed 42固定の既存の非層化Fisher-Yates分割を再現する。
        internal static (int[] Training, int[] Validation) CreateSplitIndices(int count, float validationSplit)
        {
            if (count < 2 || !float.IsFinite(validationSplit) || validationSplit <= 0 || validationSplit >= 1)
                throw new ArgumentOutOfRangeException(nameof(validationSplit), "学習件数と検証割合を確認してください。");
            // 260917Claude: ビット単位で一致させる複製を持たず、通常の分類用分割へ委譲する。
            return DeepLearningDataSplitter.CreateLegacySplitIndices(count, validationSplit, DeepLearningDataSplitter.DefaultRandomState);
        }

        // 260918Claude: 固定した正規化行をサンプル順（info.Samples[i] ↔ i番目の行）で返し、同じ読み取りロック済みハンドルで
        //   記録したダイジェストと照合する。Baselineは固定分割を使い、OCNはSpectrum IDで識別して鉱物ごとに分割する。
        //   各行で同じバッファーを返すため、行を保持する呼び出し側はコピーする必要がある。
        internal static IEnumerable<float[]> ReadRows(string datasetFolder, ClassificationTrainingDatasetInfo info, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(Path.Combine(datasetFolder, DataFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            // 260908Codex: 後段と同じ読み取りロック済みハンドルでハッシュを計算し、再開検証と行列読込の間の隙間をなくす。
            if (ComputeStreamSha256(stream, cancellationToken) != info.NormalizedDataSha256)
                throw new InvalidDataException("固定した学習データが読み込み前に変更されました。");
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != BinaryMagic || reader.ReadInt32() != 1 || reader.ReadInt32() != info.SampleCount
                || reader.ReadInt32() != info.SpectrumLength || stream.Length != 16L + 4L * info.SampleCount * info.SpectrumLength)
                throw new InvalidDataException("固定した学習データの配列形式が一致しません。");
            float[] rowValues = new float[info.SpectrumLength];
            for (int source = 0; source < info.SampleCount; source++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.ReadExactly(MemoryMarshal.AsBytes(rowValues.AsSpan()));
                foreach (float value in rowValues)
                    if (!float.IsFinite(value))
                        throw new InvalidDataException("固定した学習データに有限でない値があります。");
                yield return rowValues;
            }
        }

        // 260908Codex: 大きな固定行列でも、分割したハッシュ計算の途中で中止できるようにする。
        internal static string ComputeFileSha256(string filePath, CancellationToken cancellationToken = default)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ComputeStreamSha256(stream, cancellationToken);
        }

        // 260908Codex: 中止可能なダイジェスト処理を、ファイルとロック済みスナップショットストリームで共用する。
        private static string ComputeStreamSha256(Stream stream, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static string ComputeInfoSha256(ClassificationTrainingDatasetInfo info) =>
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(info with { DatasetSha256 = "" }, JsonOptions)));

        // 260908Codex: TensorFlow配列の確保前に、空の分割・ラベル不整合・インデックスの重複や欠落を拒否する。
        private static void ValidateInfo(ClassificationTrainingDatasetInfo info)
        {
            if (info.SchemaVersion != 1 || info.SpectrumLength != SpectrumDataLoader.SpectrumLength || info.SampleCount < 2
                || info.ClassNames is null || info.ClassNames.Length < 2 || info.ClassNames.Any(string.IsNullOrWhiteSpace)
                || info.ClassNames.Distinct().Count() != info.ClassNames.Length || info.Samples?.Length != info.SampleCount
                || info.TrainingIndices is not { Length: > 0 } || info.ValidationIndices is not { Length: > 0 }
                || info.Preprocessing is null || info.Preprocessing.MaskChannelCount < 0 || info.Preprocessing.MaskChannelCount > info.SpectrumLength
                || !Enum.IsDefined(info.Preprocessing.MaskOrder) || info.SplitSeed != DeepLearningDataSplitter.DefaultRandomState
                || !float.IsFinite(info.ValidationSplit) || info.ValidationSplit <= 0 || info.ValidationSplit >= 1)
                throw new InvalidDataException("固定した学習データの設定が不正です。");
            int[] indices = info.TrainingIndices.Concat(info.ValidationIndices).OrderBy(index => index).ToArray();
            if (!indices.SequenceEqual(Enumerable.Range(0, info.SampleCount)) || info.Samples.Any(sample => sample is null
                || string.IsNullOrWhiteSpace(sample.FilePath) || string.IsNullOrWhiteSpace(sample.SourceSha256)
                || sample.Label < 0 || sample.Label >= info.ClassNames.Length || info.ClassNames[sample.Label] != sample.MineralName))
                throw new InvalidDataException("固定した学習データのラベルまたは分割に重複・欠落があります。");
            var split = CreateSplitIndices(info.SampleCount, info.ValidationSplit);
            if (!split.Training.SequenceEqual(info.TrainingIndices) || !split.Validation.SequenceEqual(info.ValidationIndices))
                throw new InvalidDataException("固定した学習データの分割順序が一致しません。");
        }

        internal static void ValidateSettings(ModelTrainingSettings settings)
        {
            // 260909Codex: データ作成やTensorFlow起動の前に、有限でない学習率や0以下の学習率を拒否する。
            if (settings.Epochs <= 0 || settings.BatchSize <= 0 || settings.EarlyStoppingPatience < 0
                || !float.IsFinite(settings.ValidationSplit) || settings.ValidationSplit <= 0 || settings.ValidationSplit >= 1
                || !double.IsFinite(settings.UnknownDistanceScale) || settings.UnknownDistanceScale <= 0
                || !float.IsFinite(settings.LearningRate) || settings.LearningRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "学習条件を確認してください。");
        }
    }

    // 261001Codex: 任意のhead処理の失敗とは異なり、並行した入力変更は処理全体を中断する。
    internal sealed class TrainingInputChangedException(string message) : IOException(message);
}
