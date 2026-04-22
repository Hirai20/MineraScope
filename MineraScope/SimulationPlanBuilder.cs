using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MineraScope
{
    // 260416Codex: 実行単位ごとの DTSA 呼び出し情報を 1 つの job にまとめ、UI から詳細ループを外しやすくします。
    internal sealed record SimulationExecutionJob(
        string ScriptPath,
        string DtsaFolder,
        SimulationPropety Property,
        int ParallelIndex);

    // 260416Codex: 鉱物ごとの job 群を batch 化して、現状どおり鉱物単位では順次実行できるようにします。
    internal sealed record SimulationExecutionBatch(
        string SolutionName,
        IReadOnlyList<SimulationExecutionJob> Jobs);

    // 260416Codex: シミュレーション実行前のプレビューと実行計画を 1 つにまとめ、将来の不足分判定を差し込みやすくします。
    internal sealed record SimulationExecutionPlan(
        IReadOnlyList<SimulationExecutionBatch> Batches);

    // 260416Codex: request から現在のシミュレーション実行計画を組み立て、Form は plan を使うだけに寄せます。
    internal sealed class SimulationPlanBuilder
    {
        // 260416Codex: 現在の単一組成時の複製ルールを builder 側へ移し、Form 本体から分離します。
        private static T[][] CreateSimulationChunks<T>(T[] source, int parallelCount) =>
            source.Length == 1
                ? Enumerable.Range(0, parallelCount).Select(_ => new[] { source[0] }).ToArray()
                : SplitIntoChunks(source, parallelCount);

        // 260416Codex: スクリプト生成に必要な配列を同じ形で取り出し、既存ロジックをそのまま再利用します。
        private static ((string ElementName, double Weight)[][] Elements, string[] OutputFiles) GetChunkData(
            (string FileName, (string ElementName, double Weight)[] Compositions)[] chunk) =>
            (
                chunk.Select(item => item.Compositions).ToArray(),
                chunk.Select(item => item.FileName).ToArray()
            );

        // 260416Codex: 出力先命名規則を builder 側へ集約し、完成 UI で経路が変わっても差し替えやすくします。
        private static string GetSimulationOutputFolder(ModelCreationRequest request, SolidSolution solution) =>
            solution.Members.Length == 1
                ? Path.Combine(request.Paths.SpectrumOutputFolder, solution.Name)
                : Path.Combine(request.Paths.SpectrumOutputFolder, $"{solution.Name}_{request.Simulation.ResolutionStep * 100}mol%");

        // 260416Codex: 現在の Form 値依存だった SimulationPropety 組み立てを request ベースへ置き換えます。
        private static SimulationPropety CreateSimulationProperty(
            ModelCreationRequest request,
            string mineralGroupName,
            (string ElementName, double Weight)[][] atoms,
            string outputFolder,
            string[] outputFiles) =>
            new()
            {
                MineralGropName = mineralGroupName,
                Atoms1 = atoms,
                DetectorName = request.SemEdxCondition.DetectorName,
                CarbonCoatThickness = request.SemEdxCondition.CarbonCoatThickness,
                BeamEnergy = request.SemEdxCondition.BeamEnergy,
                Count = request.Simulation.RunCount,
                Division = (int)(request.Simulation.ResolutionStep * 100),
                LiveTime = request.SemEdxCondition.LiveTime,
                ProbeCurrent = request.SemEdxCondition.ProbeCurrent,
                ParallelCount = request.Simulation.ParallelCount,
                OutPutFolder = outputFolder,
                OutputFile = outputFiles
            };

        // 260416Codex: request 全体から preview と batch 群を作り、後でキャッシュ判定を入れる入口にします。
        public SimulationExecutionPlan CreatePlan(ModelCreationRequest request)
        {
            if (request.SelectedMineralSolutions.Count == 0)
            {
                return new SimulationExecutionPlan([]);
            }

            var batches = new List<SimulationExecutionBatch>();

            foreach (var solution in request.SelectedMineralSolutions)
            {
                var compositions = solution.Divide(request.Simulation.ResolutionStep, request.Simulation.TargetCompositionCount);
                if (compositions is null || compositions.Length == 0)
                {
                    continue;
                }

                var chunks = CreateSimulationChunks(compositions, request.Simulation.ParallelCount);
                var jobs = new List<SimulationExecutionJob>(chunks.Length);
                for (int i = 0; i < chunks.Length; i++)
                {
                    var chunk = chunks[i];
                    var (elements, outputFiles) = GetChunkData(chunk);
                    var outputFolder = GetSimulationOutputFolder(request, solution);
                    var property = CreateSimulationProperty(request, solution.Name, elements, outputFolder, outputFiles);

                    jobs.Add(new SimulationExecutionJob(
                        Path.Combine(request.Paths.ScriptOutputFolder, $"test{i + 1}.py"),
                        request.Paths.DtsaFolder,
                        property,
                        i));
                }

                batches.Add(new SimulationExecutionBatch(solution.Name, jobs));
            }

            return new SimulationExecutionPlan(batches);
        }

        // 260416Codex: 現在の分割規則をそのまま維持しつつ、builder 単体で計画を立てられるようにします。
        public static T[][] SplitIntoChunks<T>(T[] source, int chunkCount) =>
            source
                .Select((value, index) => new { value, index })
                .GroupBy(item => item.index % chunkCount)
                .Select(group => group.Select(item => item.value).ToArray())
                .ToArray();
    }
}
