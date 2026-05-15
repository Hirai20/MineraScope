using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MineraScope
{
    // 260513Codex: manifest.json の保存場所決定と読み書きを一か所に集約します。
    internal sealed class SpectrumPoolRepository
    {
        private const string ManifestFileName = "manifest.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly SpectrumConditionKeyBuilder _conditionKeyBuilder;

        public SpectrumPoolRepository(SpectrumConditionKeyBuilder conditionKeyBuilder)
        {
            _conditionKeyBuilder = conditionKeyBuilder ?? throw new ArgumentNullException(nameof(conditionKeyBuilder));
        }

        // 260513Codex: 鉱物名と conditionKey から pool フォルダを決め、ファイル名に使えない文字を除きます。
        public SpectrumPoolHandle ResolvePool(
            string spectrumOutputFolder,
            SolidSolution solution,
            double resolutionStep,
            SemEdxCondition semEdxCondition)
        {
            var snapshot = _conditionKeyBuilder.CreateSnapshot(solution, resolutionStep, semEdxCondition);
            string conditionKey = _conditionKeyBuilder.BuildKey(snapshot);
            string poolFolder = Path.Combine(
                spectrumOutputFolder,
                SanitizeFileName(solution.Name),
                conditionKey);

            return new SpectrumPoolHandle(
                poolFolder,
                Path.Combine(poolFolder, ManifestFileName),
                conditionKey,
                snapshot);
        }

        // 260513Codex: 既存 manifest があれば読み込み、なければ新規の空 manifest を作ります。
        public SpectrumPoolManifest LoadOrCreate(SpectrumPoolHandle handle)
        {
            var manifest = File.Exists(handle.ManifestPath)
                ? Load(handle.ManifestPath)
                : null;

            if (manifest is not null)
                return manifest;

            return new SpectrumPoolManifest
            {
                ConditionKey = handle.ConditionKey,
                MineralName = handle.Condition.MineralName,
                Condition = handle.Condition
            };
        }

        // 260513Codex: 実行結果反映では handle がなくても manifest path から直接読めるようにします。
        public SpectrumPoolManifest? Load(string manifestPath) =>
            File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<SpectrumPoolManifest>(File.ReadAllText(manifestPath))
                : null;

        // 260513Codex: pool フォルダを作成してから manifest を保存します。
        public void Save(SpectrumPoolHandle handle, SpectrumPoolManifest manifest)
        {
            Directory.CreateDirectory(handle.PoolFolder);
            Save(handle.ManifestPath, manifest);
        }

        // 260513Codex: 実行結果反映時の manifest 保存も同じ JSON 設定に揃えます。
        public void Save(string manifestPath, SpectrumPoolManifest manifest)
        {
            string? folder = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        }

        // 260513Codex: Completed でも実ファイルを読めないものは Missing に戻し、学習対象から外します。
        public bool RefreshCompletedStatuses(SpectrumPoolHandle handle, SpectrumPoolManifest manifest)
        {
            bool changed = false;
            foreach (var entry in manifest.Spectra.Where(entry => entry.Status == SpectrumManifestStatus.Completed))
            {
                string filePath = Path.Combine(handle.PoolFolder, entry.FileName);
                if (SpectrumDataLoader.LoadNormalizedSpectrum(filePath) is not null)
                    continue;

                entry.Status = SpectrumManifestStatus.Missing;
                // 260513Codex: manifest に残る理由はユーザーが読める自然な日本語にします。
                entry.FailureReason = "Completed と記録されていますが、実ファイルを学習用スペクトルとして読み込めません。";
                changed = true;
            }

            return changed;
        }

        // 260513Codex: ファイルシステム上で安全に使える名前へ鉱物名を変換します。
        public static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Mineral" : sanitized.Trim();
        }
    }

    // 260513Codex: pool の場所と生成条件 snapshot を一緒に運ぶ軽量ハンドルです。
    internal sealed record SpectrumPoolHandle(
        string PoolFolder,
        string ManifestPath,
        string ConditionKey,
        SpectrumGenerationConditionSnapshot Condition);
}
