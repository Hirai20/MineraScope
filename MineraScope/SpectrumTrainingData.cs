using System.Collections.Generic;

namespace MineraScope
{
    // 260901Codex: Preserve the manifest identity of every selected spectrum for stratified splitting and provenance.
    internal sealed record SpectrumTrainingSource(
        double LiveTime,
        string ConditionKey,
        int SimulationId,
        string ManifestPath,
        // 260907Codex: Planned rows carry their plan identity into provenance; legacy samples stay null.
        string? CompositionTimePlanId = null,
        // 260907Codex: Distinguish quota shuffling from the original equal-time planner in model provenance.
        string? CompositionTimePlannerVersion = null);

    // 260507Codex: manifest で選ばれた Completed spectrum と回帰ラベルを学習側へ渡します。
    internal sealed record SpectrumTrainingSample(
        string FilePath,
        IReadOnlyDictionary<string, double> EndmemberFractions,
        // 260901Codex: Null keeps legacy single-condition callers and their original split behavior compatible.
        SpectrumTrainingSource? Source = null);

    // 260507Codex: 分類ラベル単位の学習入力をまとめ、DeepLearning がフォルダ走査に依存しない形にします。
    internal sealed record SpectrumTrainingPool(
        string MineralName,
        IReadOnlyList<string> EndmemberNames,
        IReadOnlyList<SpectrumTrainingSample> Samples);
}
