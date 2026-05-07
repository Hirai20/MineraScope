using System.Collections.Generic;

namespace MineraScope
{
    // 260507Codex: manifest 内で使う spectrum 状態を文字列定数にし、JSON 表現を安定させます。
    internal static class SpectrumManifestStatus
    {
        public const string Pending = "Pending";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Missing = "Missing";
    }

    // 260507Codex: pool の同一性判定に使う鉱物・EDX・生成条件のスナップショットです。
    internal sealed class SpectrumGenerationConditionSnapshot
    {
        public string MineralName { get; set; } = string.Empty;
        public string MineralFormula { get; set; } = string.Empty;
        public List<EndmemberConditionSnapshot> Endmembers { get; set; } = [];
        public List<string> Constraints { get; set; } = [];
        public double CompositionResolution { get; set; }
        public SemEdxCondition SemEdxCondition { get; set; } = new(string.Empty, 0, 0, 0, 0);
        public string DtsaGenerationSchema { get; set; } = "dtsa2-emsa-v1";
    }

    // 260507Codex: 端成分名と式を conditionKey と manifest の両方で同じ形に保ちます。
    internal sealed class EndmemberConditionSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
    }

    // 260507Codex: 各 spectrum の予約・生成結果・回帰ラベルを manifest に保持します。
    internal sealed class SpectrumManifestEntry
    {
        public int SimulationId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = SpectrumManifestStatus.Pending;
        public Dictionary<string, double> EndmemberFractions { get; set; } = [];
        public string? FailureReason { get; set; }
    }

    // 260507Codex: 各鉱物・生成条件ごとの spectrum pool を表す manifest のルートです。
    internal sealed class SpectrumPoolManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string ConditionKey { get; set; } = string.Empty;
        public string MineralName { get; set; } = string.Empty;
        public int NextSimulationId { get; set; }
        public SpectrumGenerationConditionSnapshot Condition { get; set; } = new();
        public List<SpectrumManifestEntry> Spectra { get; set; } = [];
    }
}
