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
        public SemEdxCondition SemEdxCondition { get; set; } = new(DetectorProfile.CreateLegacyTest(), 0, 0, 0, 0);
        // 260626Codex: Detector profile fields are part of generation identity; legacy unknown-detector pools should not be auto-reused.
        public string DtsaGenerationSchema { get; set; } = "dtsa2-emsa-v2-detector-profile";
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
        // 260907Codex: Optional plan fields preserve legacy manifests while binding new Preset B rows to one global composition/time schedule.
        public string? PlanId { get; set; }
        public string? PlanEntryId { get; set; }
        public string? PlanRowHash { get; set; }
        public int? PlanRepeatOrdinal { get; set; }
        public int GenerationAttemptCount { get; set; }
        public string? LastFailureKind { get; set; }
        // 260907Codex: A physical spectrum can satisfy later plans without erasing its original plan identity.
        public List<SpectrumManifestPlanBinding> AdditionalPlanBindings { get; set; } = [];

        // 260907Codex: Read the original single-plan fields and additive bindings through one compatibility boundary.
        public IEnumerable<SpectrumManifestPlanBinding> GetPlanBindings()
        {
            if (!string.IsNullOrWhiteSpace(PlanId))
                yield return new SpectrumManifestPlanBinding
                {
                    PlanId = PlanId,
                    PlanEntryId = PlanEntryId,
                    PlanRowHash = PlanRowHash,
                    PlanRepeatOrdinal = PlanRepeatOrdinal
                };
            foreach (var binding in AdditionalPlanBindings)
                yield return binding;
        }
    }

    // 260907Codex: Optional per-spectrum associations keep old plan references valid when N or time weights change.
    internal sealed class SpectrumManifestPlanBinding
    {
        public string PlanId { get; set; } = string.Empty;
        public string? PlanEntryId { get; set; }
        public string? PlanRowHash { get; set; }
        public int? PlanRepeatOrdinal { get; set; }
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
        // 260907Codex: Keep plan metadata beside rows so an accidentally deleted plan JSON can be restored without remaking spectra.
        public List<SpectrumCompositionTimePlanReference> CompositionTimePlans { get; set; } = [];
    }
}
