using System.Collections.Generic;

namespace MineraScope
{
    // 260507Codex: 画面上の共通パスを、pool 作成・DTSA 実行・モデル保存へ渡す入力としてまとめます。
    internal sealed record ModelCreationPaths(
        string SpectrumOutputFolder,
        string ScriptOutputFolder,
        string DtsaFolder,
        string TeacherDataFolder,
        string ModelOutputFolder);

    // 260507Codex: pool の conditionKey に含める現行 EDX 条件を明示します。
    internal sealed record SemEdxCondition(
        string DetectorName,
        double CarbonCoatThickness,
        double BeamEnergy,
        double LiveTime,
        double ProbeCurrent);

    // 260507Codex: target は学習に使用したい件数、parallel は不足分生成のジョブ分割数として扱います。
    internal sealed record SimulationExecutionSettings(
        int TargetSpectrumCount,
        double ResolutionStep,
        int ParallelCount);

    // 260507Codex: 学習条件は conditionKey から外し、モデル作成時だけ使う設定として分離します。
    internal sealed record ModelTrainingSettings(
        int Epochs,
        int BatchSize,
        int EarlyStoppingPatience,
        float ValidationSplit);

    // 260507Codex: モデル作成対象は checkedListBoxMineral のチェック済み SolidSolution だけに統一します。
    internal sealed record ModelCreationRequest(
        ModelCreationPaths Paths,
        SemEdxCondition SemEdxCondition,
        SimulationExecutionSettings Simulation,
        ModelTrainingSettings Training,
        IReadOnlyList<SolidSolution> SelectedMineralSolutions);
}
