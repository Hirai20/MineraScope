using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineraScope
{
    // 260507Codex: 画面上の共通パスを、pool 作成・DTSA 実行・モデル保存へ渡す入力としてまとめます。
    internal sealed record ModelCreationPaths(
        string SpectrumOutputFolder,
        string ScriptOutputFolder,
        string DtsaFolder,
        string ModelOutputFolder)
    {
        // 260917Claude: モデル一式を保存するフォルダー。設定が空ならアプリの既定値を使う。
        // 261014Codex: 保存先の既定値を、画面とworkflowで共通に解決する。
        public string ResolveModelRootFolder() =>
            string.IsNullOrWhiteSpace(ModelOutputFolder) ? DefaultStoragePaths.ModelsFolder : ModelOutputFolder;
    }

    // 260507Codex: pool の conditionKey に含める現行 EDX 条件を明示します。
    internal sealed record SemEdxCondition(
        string DetectorName,
        double CarbonCoatThickness,
        double BeamEnergy,
        double LiveTime,
        double ProbeCurrent)
    {
        public DetectorProfile? DetectorProfile { get; init; } = MineraScope.DetectorProfile.CreateLegacyTest(DetectorName);

        // 260626Codex: 検出器プロファイル欄を追加したmanifestを読む際、System.Text.Jsonが曖昧なく使えるコンストラクターを用意する。
        public SemEdxCondition()
            : this(string.Empty, 0, 0, 0, 0)
        {
        }

        // 260626Codex: 新しい呼び出し側は編集可能な検出器プロファイルを渡し、旧DetectorNameはJSON互換性のため保持する。
        public SemEdxCondition(
            DetectorProfile detectorProfile,
            double carbonCoatThickness,
            double beamEnergy,
            double liveTime,
            double probeCurrent)
            : this(
                MineraScope.DetectorProfile.CreateWithDefaults(detectorProfile).Name,
                carbonCoatThickness,
                beamEnergy,
                liveTime,
                probeCurrent)
        {
            DetectorProfile = MineraScope.DetectorProfile.CreateWithDefaults(detectorProfile);
        }

        public DetectorProfile GetDetectorProfile() =>
            MineraScope.DetectorProfile.CreateWithDefaults(DetectorProfile, DetectorName);
    }

    // 260507Codex: target は学習に使用したい件数、parallel は不足分生成のジョブ分割数として扱います。
    // 260622Claude: CarbonThicknessJitterPercent は生成時にカーボン蒸着膜厚を spectrum ごとへ ±x% 振る幅 (0 で無効)。conditionKey には含めない。
    internal sealed record SimulationExecutionSettings(
        int TargetSpectrumCount,
        double ResolutionStep,
        int ParallelCount,
        double CarbonThicknessJitterPercent);

    // 260901Codex: 各測定時間の目標を明示し、1要求で独立した条件キーのpoolを再利用できるようにする。
    internal sealed record SpectrumTimeAllocation(
        double LiveTime,
        int TargetSpectrumCount);

    // 260907Codex: 鉱物ごとの総数と時間重みを分け、1000を特別扱いせずPreset Bの正確な件数を配分する。
    internal sealed record SpectrumTimeWeight(double LiveTime, int Weight);

    // 260907Codex: Preset Bは全ライブタイム共通の組成・時間計画を持ち、後でこのDTOを設定に公開するまでUIは固定の既定値を使える。
    internal sealed record SpectrumTimeSchedule(
        int TotalSpectrumCount,
        IReadOnlyList<SpectrumTimeWeight> TimeWeights)
    {
        public static SpectrumTimeSchedule MeasurementTimeB { get; } = new(
            1000,
            [new(1, 1), new(5, 1), new(10, 1), new(20, 1), new(50, 1)]);

        public IReadOnlyList<SpectrumTimeAllocation> CreateAllocations()
        {
            if (TotalSpectrumCount < TimeWeights.Count)
                throw new InvalidOperationException("総スペクトル数は測定時間条件数以上にしてください。");
            if (TimeWeights.Count == 0 || TimeWeights.Any(item => !double.IsFinite(item.LiveTime) || item.LiveTime <= 0 || item.Weight <= 0))
                throw new InvalidOperationException("測定時間と比率は正の値にしてください。");

            int totalWeight = TimeWeights.Sum(item => item.Weight);
            var quotas = TimeWeights
                .Select(item => new
                {
                    item.LiveTime,
                    Exact = (double)TotalSpectrumCount * item.Weight / totalWeight
                })
                .Select(item => new
                {
                    item.LiveTime,
                    Count = (int)Math.Floor(item.Exact),
                    Remainder = item.Exact - Math.Floor(item.Exact)
                })
                .ToArray();
            int[] counts = quotas.Select(item => item.Count).ToArray();
            int remaining = TotalSpectrumCount - counts.Sum();
            foreach (int index in Enumerable.Range(0, quotas.Length)
                .OrderByDescending(index => quotas[index].Remainder)
                .ThenBy(index => quotas[index].LiveTime)
                .Take(remaining))
                counts[index]++;
            if (counts.Any(count => count <= 0))
                throw new InvalidOperationException("比率により0本となる測定時間があります。総数または比率を見直してください。");

            return Enumerable.Range(0, quotas.Length)
                .OrderBy(index => quotas[index].LiveTime)
                .Select(index => new SpectrumTimeAllocation(quotas[index].LiveTime, counts[index]))
                .ToArray();
        }
    }

    // 260507Codex: 学習条件は conditionKey から外し、モデル作成時だけ使う設定として分離します。
    // 260622Claude: UnknownDistanceScale は未知判定で既知とみなす距離に掛ける倍率 (大きいほど Unknown を出しにくい)。
    // 260918Codex: 逆シリアル化で、新規要求とLearningRate追加前の実験JSONを区別する。
    [JsonConverter(typeof(ModelTrainingSettingsJsonConverter))]
    internal sealed record ModelTrainingSettings(
        int Epochs,
        int BatchSize,
        int EarlyStoppingPatience,
        float ValidationSplit,
        double UnknownDistanceScale)
    {
        // 260918Codex: 新規Softmax分類要求はAdam 1e-4を使い、この欄の追加前に作成したJSONは変換処理で1e-3を保持する。
        public const float DefaultClassificationLearningRate = 1e-4f;
        internal const float LegacyClassificationLearningRate = 1e-3f;

        public float LearningRate { get; init; } = DefaultClassificationLearningRate;
    }

    // 260918Codex: LearningRate欠落は設定追加前のSeed成果物を表し、再開時も記録済みの従来のAdam学習率を保持する。
    internal sealed class ModelTrainingSettingsJsonConverter : JsonConverter<ModelTrainingSettings>
    {
        public override ModelTrainingSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            T ReadRequired<T>(string name, Func<JsonElement, T> read) =>
                TryGetProperty(root, name, out JsonElement property) ? read(property) : default!;

            var settings = new ModelTrainingSettings(
                ReadRequired(nameof(ModelTrainingSettings.Epochs), property => property.GetInt32()),
                ReadRequired(nameof(ModelTrainingSettings.BatchSize), property => property.GetInt32()),
                ReadRequired(nameof(ModelTrainingSettings.EarlyStoppingPatience), property => property.GetInt32()),
                ReadRequired(nameof(ModelTrainingSettings.ValidationSplit), property => property.GetSingle()),
                ReadRequired(nameof(ModelTrainingSettings.UnknownDistanceScale), property => property.GetDouble()));

            return settings with
            {
                LearningRate = TryGetProperty(root, nameof(ModelTrainingSettings.LearningRate), out JsonElement learningRate)
                    ? learningRate.GetSingle()
                    : ModelTrainingSettings.LegacyClassificationLearningRate
            };
        }

        public override void Write(Utf8JsonWriter writer, ModelTrainingSettings value, JsonSerializerOptions options)
        {
            string Name(string property) => options.PropertyNamingPolicy?.ConvertName(property) ?? property;

            writer.WriteStartObject();
            writer.WriteNumber(Name(nameof(value.Epochs)), value.Epochs);
            writer.WriteNumber(Name(nameof(value.BatchSize)), value.BatchSize);
            writer.WriteNumber(Name(nameof(value.EarlyStoppingPatience)), value.EarlyStoppingPatience);
            writer.WriteNumber(Name(nameof(value.ValidationSplit)), value.ValidationSplit);
            writer.WriteNumber(Name(nameof(value.UnknownDistanceScale)), value.UnknownDistanceScale);
            writer.WriteNumber(Name(nameof(value.LearningRate)), value.LearningRate);
            writer.WriteEndObject();
        }

        private static bool TryGetProperty(JsonElement root, string name, out JsonElement property)
        {
            foreach (JsonProperty candidate in root.EnumerateObject())
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }

            property = default;
            return false;
        }
    }

    // 260507Codex: モデル作成対象は checkedListBoxMineral のチェック済み SolidSolution だけに統一します。
    // 260901Codex: 任意の割当指定でモデル作成を拡張し、従来の単一時間用コンストラクターは変えない。
    internal sealed record ModelCreationRequest(
        ModelCreationPaths Paths,
        // 260508Codex: 学習成果物はモデル保存先の直下ではなく、画面のモデル名フォルダ配下へ保存します。
        string ModelName,
        SemEdxCondition SemEdxCondition,
        SimulationExecutionSettings Simulation,
        ModelTrainingSettings Training,
        IReadOnlyList<SolidSolution> SelectedMineralSolutions)
    {
        // 260901Codex: 一覧が空なら、既存のSemEdxCondition.LiveTimeとSimulationの目標を使う。
        public IReadOnlyList<SpectrumTimeAllocation> SpectrumTimeAllocations { get; init; } = [];

        // 260907Codex: null以外なら全体の組成・時間計画を使う。割当一覧だけが空の場合は、以前の明示時間指定の互換性を保つ。
        public SpectrumTimeSchedule? SpectrumTimeSchedule { get; init; }

        public bool UsesCompositionTimePlan => SpectrumTimeSchedule is not null;

        // 260901Codex: pool解決前に共通の予定表を一度検証し、不正・重複条件によるmanifest予約を防ぐ。
        public IReadOnlyList<SpectrumTimeAllocation> GetEffectiveSpectrumTimeAllocations()
        {
            SpectrumTimeAllocation[] allocations = SpectrumTimeSchedule is not null
                ? SpectrumTimeSchedule.CreateAllocations().ToArray()
                : SpectrumTimeAllocations.Count == 0
                    ? [new SpectrumTimeAllocation(SemEdxCondition.LiveTime, Simulation.TargetSpectrumCount)]
                    : SpectrumTimeAllocations.ToArray();

            foreach (var allocation in allocations)
            {
                if (!double.IsFinite(allocation.LiveTime) || allocation.LiveTime <= 0)
                    throw new InvalidOperationException("測定時間は0より大きい有限値にしてください。");
                if (allocation.TargetSpectrumCount <= 0)
                    throw new InvalidOperationException("測定時間ごとのスペクトル数は1以上にしてください。");
            }

            for (int i = 0; i < allocations.Length; i++)
                for (int j = i + 1; j < allocations.Length; j++)
                    if (Math.Abs(allocations[i].LiveTime - allocations[j].LiveTime) <= 1e-9)
                        throw new InvalidOperationException($"測定時間 {allocations[i].LiveTime} 秒が重複しています。");

            // 260901Codex: 鉱物バッチごとに、有効な各時間に対して条件が均一なジョブを最低1つ用意する。
            if (allocations.Length > Math.Max(1, Simulation.ParallelCount))
                throw new InvalidOperationException("並列数は測定時間条件数以上にしてください。");

            return allocations;
        }

        // 260901Codex: 既存の単一条件pool処理には、ライブタイムと目標件数だけを変えた派生要求を渡す。
        public ModelCreationRequest ForSpectrumTimeAllocation(SpectrumTimeAllocation allocation) =>
            this with
            {
                SemEdxCondition = SemEdxCondition with { LiveTime = allocation.LiveTime },
                Simulation = Simulation with { TargetSpectrumCount = allocation.TargetSpectrumCount },
                SpectrumTimeAllocations = [],
                SpectrumTimeSchedule = null
            };
    }
}
