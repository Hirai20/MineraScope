using System.Collections.Generic;

namespace MineraScope
{
    // 260416Codex: 画面のファイル系入力を 1 つの値オブジェクトへまとめ、ワークフロー側へそのまま渡せる形にします。
    internal sealed record ModelCreationPaths(
        string SpectrumOutputFolder,
        string ScriptOutputFolder,
        string DtsaFolder,
        string TeacherDataFolder,
        string ModelOutputFolder);

    // 260416Codex: SEM-EDX 条件を UI から分離し、将来の一致判定ロジックでも再利用しやすくします。
    internal sealed record SemEdxCondition(
        string DetectorName,
        double CarbonCoatThickness,
        double BeamEnergy,
        double LiveTime,
        double ProbeCurrent);

    // 260416Codex: シミュレーション側の実行条件をまとめ、ボタンごとの差し替えを簡単にします。
    internal sealed record SimulationExecutionSettings(
        int TargetCompositionCount,
        double ResolutionStep,
        int RunCount,
        int ParallelCount);

    // 260416Codex: 学習設定を DTO 化して、Form から DeepLearning 呼び出しまでの依存を弱めます。
    internal sealed record ModelTrainingSettings(
        int Epochs,
        int BatchSize,
        int EarlyStoppingPatience,
        float ValidationSplit);

    // 260416Codex: 現在の GeneratorForm の状態を 1 回で表せる request を作り、段階的な workflow 化の入口にします。
    internal sealed record ModelCreationRequest(
        ModelCreationPaths Paths,
        SemEdxCondition SemEdxCondition,
        SimulationExecutionSettings Simulation,
        ModelTrainingSettings Training,
        IReadOnlyList<SolidSolution> SelectedMineralSolutions,
        IReadOnlyList<string> SelectedTrainingMinerals);
}
