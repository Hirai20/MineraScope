using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineraScope
{
    // 260924Codex: 学習型Unknown判定を、学習に使った保存済みSoftmax分類器と検知器そのものへ結び付ける。
    internal sealed record SoftmaxOcnHeadArtifact(
        int SchemaVersion,
        string Input,
        string[] ClassNames,
        string ClassifierSha256,
        string DetectorSha256,
        float[] Weights,
        int TrainingSeed,
        int BestEpoch,
        double BestScore,
        string SourceAttempt)
    {
        // 261001Codex: 旧成果物に幅がなければ、元の3距離入力headを意味する。
        public int InputCount { get; init; } = 3;
        public const string FileName = "softmaxOcnHead.json";
        public const int CurrentSchemaVersion = 1;
        // 261001Codex: 保留中のSeed実験データセットを取り込まず、成果物のJSON名を保持する。
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static bool Exists(string modelRoot) => File.Exists(Path.Combine(ModelArtifactPaths.GetClassificationFolder(modelRoot), FileName));

        public static SoftmaxOcnHeadArtifact Load(string modelRoot)
        {
            string classification = ModelArtifactPaths.GetClassificationFolder(modelRoot);
            var artifact = JsonSerializer.Deserialize<SoftmaxOcnHeadArtifact>(File.ReadAllText(Path.Combine(classification, FileName)), JsonOptions)
                ?? throw new InvalidDataException("Softmax＋OCN判定器の保存情報が空です。");
            if (artifact.SchemaVersion != CurrentSchemaVersion || artifact.Input != "Raw" || artifact.ClassNames is not { Length: >= 2 }
                || artifact.InputCount is < 1 or > 3 || artifact.InputCount > artifact.ClassNames.Length
                || artifact.Weights is null || artifact.Weights.Length != SoftmaxOcnHead.GetWeightCount(artifact.InputCount) || !Array.TrueForAll(artifact.Weights, float.IsFinite)
                || artifact.BestEpoch < 1 || !double.IsFinite(artifact.BestScore) || string.IsNullOrWhiteSpace(artifact.SourceAttempt))
                throw new InvalidDataException("Softmax＋OCN判定器の保存形式が不正です。");
            if (artifact.ClassifierSha256 != HashClassifier(classification)
                || artifact.DetectorSha256 != HashFile(Path.Combine(classification, MineralUnknownDetector.FileName)))
                throw new InvalidDataException("Softmax＋OCN判定器と分類モデルまたは距離検知器が一致しません。");

            var labels = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(classification, ModelArtifactPaths.LabelEncoderFileName)))
                ?? throw new InvalidDataException("分類モデルのラベルを読み込めません。");
            if (labels.Count != artifact.ClassNames.Length || artifact.ClassNames.Where((name, index) => !labels.TryGetValue(name, out int id) || id != index).Any())
                throw new InvalidDataException("Softmax＋OCN判定器と分類モデルのラベル順が一致しません。");
            return artifact;
        }

        public static void Save(string modelRoot, IReadOnlyList<string> classNames, float[] weights,
            int trainingSeed, int bestEpoch, double bestScore, string sourceAttempt, int inputCount = 3)
        {
            string classification = ModelArtifactPaths.GetClassificationFolder(modelRoot);
            ArgumentNullException.ThrowIfNull(classNames);
            ArgumentNullException.ThrowIfNull(weights);
            if (classNames.Count < 2 || inputCount is < 1 or > 3 || inputCount > classNames.Count
                || weights.Length != SoftmaxOcnHead.GetWeightCount(inputCount) || !Array.TrueForAll(weights, float.IsFinite)
                || trainingSeed < 0 || bestEpoch < 1 || !double.IsFinite(bestScore) || string.IsNullOrWhiteSpace(sourceAttempt))
                throw new ArgumentException("Softmax＋OCN判定器のクラスまたは重みが不正です。");
            string path = Path.Combine(classification, FileName);
            if (File.Exists(path))
                throw new IOException($"Softmax＋OCN判定器は既に存在します: {path}");
            var artifact = new SoftmaxOcnHeadArtifact(CurrentSchemaVersion, "Raw", classNames.ToArray(),
                HashClassifier(classification), HashFile(Path.Combine(classification, MineralUnknownDetector.FileName)), (float[])weights.Clone(),
                trainingSeed, bestEpoch, bestScore, sourceAttempt) { InputCount = inputCount };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(artifact, JsonOptions), new UTF8Encoding(false));
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string HashClassifier(string classification)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string path in Directory.GetFiles(classification, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != FileName && !Path.GetFileName(path).StartsWith(FileName + ".", StringComparison.Ordinal))
                .OrderBy(path => Path.GetRelativePath(classification, path), StringComparer.Ordinal))
            {
                byte[] name = Encoding.UTF8.GetBytes(Path.GetRelativePath(classification, path).Replace('\\', '/'));
                hash.AppendData(BitConverter.GetBytes(name.Length));
                hash.AppendData(name);
                hash.AppendData(Convert.FromHexString(HashFile(path)));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
