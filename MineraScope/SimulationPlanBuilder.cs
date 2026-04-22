using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MineraScope
{
    // 260416Codex: Keep each DTSA-II script execution request as a small immutable job model.
    internal sealed record SimulationExecutionJob(
        string ScriptPath,
        string DtsaFolder,
        SimulationProperty Property,
        int ParallelIndex);

    // 260416Codex: Group jobs by solution name so the UI can execute and inspect them batch by batch.
    internal sealed record SimulationExecutionBatch(
        string SolutionName,
        IReadOnlyList<SimulationExecutionJob> Jobs);

    // 260416Codex: Represent the final execution plan as a list of per-solution batches.
    internal sealed record SimulationExecutionPlan(
        IReadOnlyList<SimulationExecutionBatch> Batches);

    // 260416Codex: Build the DTSA-II execution plan from the current form request.
    internal sealed class SimulationPlanBuilder
    {
        // 260416Codex: A single composition still needs one script per parallel slot, so repeat it when needed.
        private static T[][] CreateSimulationChunks<T>(T[] source, int parallelCount) =>
            source.Length == 1
                ? Enumerable.Range(0, parallelCount).Select(_ => new[] { source[0] }).ToArray()
                : SplitIntoChunks(source, parallelCount);

        // 260416Codex: Split the chunk into the two arrays the DTSA-II script generator consumes.
        private static ((string ElementName, double Weight)[][] Elements, string[] OutputFiles) GetChunkData(
            (string FileName, (string ElementName, double Weight)[] Compositions)[] chunk) =>
            (
                chunk.Select(item => item.Compositions).ToArray(),
                chunk.Select(item => item.FileName).ToArray()
            );

        // 260416Codex: Resolve the output folder from the few values this decision actually needs.
        private static string GetSimulationOutputFolder(
            string spectrumOutputFolder,
            double resolutionStep,
            SolidSolution solution) =>
            solution.Members.Length == 1
                ? Path.Combine(spectrumOutputFolder, solution.Name)
                : Path.Combine(spectrumOutputFolder, $"{solution.Name}_{resolutionStep * 100}mol%");

        // 260416Codex: Build the DTO directly from the SEM-EDX and simulation settings without passing the whole request.
        private static SimulationProperty CreateSimulationProperty(
            SemEdxCondition semEdxCondition,
            SimulationExecutionSettings simulation,
            string mineralGroupName,
            (string ElementName, double Weight)[][] atoms,
            string outputFolder,
            string[] outputFiles) =>
            new()
            {
                // 260416Codex: 内部利用は正しい綴りの別名プロパティへ寄せます。
                MineralGroupName = mineralGroupName,
                Atoms1 = atoms,
                DetectorName = semEdxCondition.DetectorName,
                CarbonCoatThickness = semEdxCondition.CarbonCoatThickness,
                BeamEnergy = semEdxCondition.BeamEnergy,
                Count = simulation.RunCount,
                Division = (int)(simulation.ResolutionStep * 100),
                LiveTime = semEdxCondition.LiveTime,
                ProbeCurrent = semEdxCondition.ProbeCurrent,
                ParallelCount = simulation.ParallelCount,
                OutputFolder = outputFolder,
                OutputFiles = outputFiles
            };

        // 260416Codex: Build one batch per selected solution and skip solutions that do not yield compositions.
        public SimulationExecutionPlan CreatePlan(ModelCreationRequest request)
        {
            if (request.SelectedMineralSolutions.Count == 0)
            {
                return new SimulationExecutionPlan([]);
            }

            // 260416Codex: Cache nested request members once so the loop reads as plan assembly rather than object traversal.
            var paths = request.Paths;
            var semEdxCondition = request.SemEdxCondition;
            var simulation = request.Simulation;
            var batches = new List<SimulationExecutionBatch>(request.SelectedMineralSolutions.Count);

            foreach (var solution in request.SelectedMineralSolutions)
            {
                var solutionName = solution.Name;
                var compositions = solution.Divide(simulation.ResolutionStep, simulation.TargetCompositionCount);
                if (compositions is not { Length: > 0 })
                {
                    continue;
                }

                var outputFolder = GetSimulationOutputFolder(paths.SpectrumOutputFolder, simulation.ResolutionStep, solution);
                var chunks = CreateSimulationChunks(compositions, simulation.ParallelCount);
                var jobs = new SimulationExecutionJob[chunks.Length];

                for (int i = 0; i < chunks.Length; i++)
                {
                    var (elements, outputFiles) = GetChunkData(chunks[i]);
                    var property = CreateSimulationProperty(
                        semEdxCondition,
                        simulation,
                        solutionName,
                        elements,
                        outputFolder,
                        outputFiles);

                    jobs[i] = new SimulationExecutionJob(
                        Path.Combine(paths.ScriptOutputFolder, $"test{i + 1}.py"),
                        paths.DtsaFolder,
                        property,
                        i);
                }

                batches.Add(new SimulationExecutionBatch(solutionName, jobs));
            }

            return new SimulationExecutionPlan(batches);
        }

        // 260416Codex: Distribute source items round-robin so parallel workers get balanced chunk sizes.
        public static T[][] SplitIntoChunks<T>(T[] source, int chunkCount) =>
            source
                .Select((value, index) => new { value, index })
                .GroupBy(item => item.index % chunkCount)
                .Select(group => group.Select(item => item.value).ToArray())
                .ToArray();
    }
}
