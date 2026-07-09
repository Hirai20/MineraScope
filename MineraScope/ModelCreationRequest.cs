using System.Collections.Generic;

namespace MineraScope
{
    // 260507Codex: 画面上の共通パスを、pool 作成・DTSA 実行・モデル保存へ渡す入力としてまとめます。
    internal sealed record ModelCreationPaths(
        string SpectrumOutputFolder,
        string ScriptOutputFolder,
        string DtsaFolder,
        string ModelOutputFolder);

    // 260507Codex: pool の conditionKey に含める現行 EDX 条件を明示します。
    internal sealed record SemEdxCondition(
        string DetectorName,
        double CarbonCoatThickness,
        double BeamEnergy,
        double LiveTime,
        double ProbeCurrent)
    {
        public DetectorProfile? DetectorProfile { get; init; } = MineraScope.DetectorProfile.CreateLegacyTest(DetectorName);

        // 260626Codex: System.Text.Json needs an unambiguous constructor when reading manifests with the added detector profile field.
        public SemEdxCondition()
            : this(string.Empty, 0, 0, 0, 0)
        {
        }

        // 260626Codex: New callers pass the editable detector profile while the old DetectorName field remains for JSON compatibility.
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

    // 260507Codex: 学習条件は conditionKey から外し、モデル作成時だけ使う設定として分離します。
    // 260622Claude: UnknownDistanceScale は未知判定で既知とみなす距離に掛ける倍率 (大きいほど Unknown を出しにくい)。
    internal sealed record ModelTrainingSettings(
        int Epochs,
        int BatchSize,
        int EarlyStoppingPatience,
        float ValidationSplit,
        double UnknownDistanceScale);

    // 260507Codex: モデル作成対象は checkedListBoxMineral のチェック済み SolidSolution だけに統一します。
    internal sealed record ModelCreationRequest(
        ModelCreationPaths Paths,
        // 260508Codex: 学習成果物はモデル保存先の直下ではなく、画面のモデル名フォルダ配下へ保存します。
        string ModelName,
        SemEdxCondition SemEdxCondition,
        SimulationExecutionSettings Simulation,
        ModelTrainingSettings Training,
        IReadOnlyList<SolidSolution> SelectedMineralSolutions);
}
