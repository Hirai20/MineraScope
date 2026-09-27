using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MineraScope
{
    // 260901Codex: Keep the requested schedule and deterministic seeds with the training plan until promotion.
    internal sealed record TrainingDataProvenanceSpecification(
        IReadOnlyList<SpectrumTimeAllocation> TimeAllocations,
        int SelectionSeed,
        int SplitSeed,
        float ValidationSplit)
    {
        public const int DefaultSelectionSeed = 42;
        // 260906Codex: The provenance and recorded training result must state the same seed used by the splitter.
        public const int DefaultSplitSeed = DeepLearningDataSplitter.DefaultRandomState;
    }

    // 260901Codex: Write selected manifest identities as one model-root artifact without changing existing child-model files.
    internal static class TrainingDataProvenanceWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static void Write(
            string modelRootFolder,
            TrainingDataProvenanceSpecification specification,
            IReadOnlyList<SpectrumTrainingPool> trainingPools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelRootFolder);
            ArgumentNullException.ThrowIfNull(specification);
            ArgumentNullException.ThrowIfNull(trainingPools);

            var minerals = trainingPools
                .OrderBy(pool => pool.MineralName, StringComparer.OrdinalIgnoreCase)
                .Select(pool => new MineralProvenance(
                    pool.MineralName,
                    pool.Samples.Count,
                    pool.Samples
                        .GroupBy(sample => new SourceGroupKey(
                            sample.Source!.LiveTime,
                            sample.Source.ConditionKey,
                            sample.Source.ManifestPath))
                        .OrderBy(group => group.Key.LiveTime)
                        .ThenBy(group => group.Key.ConditionKey, StringComparer.Ordinal)
                        .Select(group => new SourceProvenance(
                            group.Key.LiveTime,
                            group.Key.ConditionKey,
                            group.Key.ManifestPath,
                            group.Count(),
                            group.Select(sample => sample.Source!.SimulationId).OrderBy(id => id).ToArray()))
                        .ToArray()))
                .ToArray();
            var document = new ProvenanceDocument(
                SchemaVersion: 2,
                CreatedUtc: DateTimeOffset.UtcNow,
                SelectionSeed: specification.SelectionSeed,
                SplitSeed: specification.SplitSeed,
                ValidationSplit: specification.ValidationSplit,
                SelectionMethod: GetSelectionMethod(trainingPools),
                // 260907Codex: The planner guarantees source composition/time coverage; the legacy train/validation split stays independent.
                SplitMethod: "legacy-nonstratified-seed-42",
                RequestedTimeAllocations: specification.TimeAllocations
                    .OrderBy(allocation => allocation.LiveTime)
                    .Select(allocation => new TimeAllocationProvenance(
                        allocation.LiveTime,
                        allocation.TargetSpectrumCount))
                    .ToArray(),
                TotalSpectrumCount: minerals.Sum(mineral => mineral.SpectrumCount),
                CompositionTimePlanIds: trainingPools
                    .SelectMany(pool => pool.Samples)
                    .Select(sample => sample.Source?.CompositionTimePlanId)
                    .Where(planId => !string.IsNullOrWhiteSpace(planId))
                    .Select(planId => planId!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(planId => planId, StringComparer.Ordinal)
                    .ToArray(),
                Minerals: minerals);

            Directory.CreateDirectory(modelRootFolder);
            string outputPath = Path.Combine(modelRootFolder, ModelArtifactPaths.TrainingDataProvenanceFileName);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, JsonOptions));
        }

        private sealed record ProvenanceDocument(
            int SchemaVersion,
            DateTimeOffset CreatedUtc,
            int SelectionSeed,
            int SplitSeed,
            float ValidationSplit,
            string SelectionMethod,
            string SplitMethod,
            IReadOnlyList<TimeAllocationProvenance> RequestedTimeAllocations,
            int TotalSpectrumCount,
            IReadOnlyList<string> CompositionTimePlanIds,
            IReadOnlyList<MineralProvenance> Minerals);

        // 260907Codex: A persisted plan, rather than a pool-local sampler, is the authoritative selection method for new Preset B models.
        private static string GetSelectionMethod(IReadOnlyList<SpectrumTrainingPool> trainingPools)
        {
            // 260907Codex: Use the source plan's version, including unequal-time quota shuffling, rather than a hard-coded planner name.
            string[] methods = trainingPools.SelectMany(pool => pool.Samples)
                .Where(sample => !string.IsNullOrWhiteSpace(sample.Source?.CompositionTimePlanId))
                .Select(sample => sample.Source!.CompositionTimePlannerVersion ?? SpectrumCompositionTimePlan.CurrentPlannerVersion)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray();
            return methods.Length == 0 ? "composition-maximin-v1" : string.Join(";", methods);
        }

        private sealed record TimeAllocationProvenance(
            double LiveTime,
            int SpectrumCountPerMineral);

        private sealed record MineralProvenance(
            string MineralName,
            int SpectrumCount,
            IReadOnlyList<SourceProvenance> Sources);

        private sealed record SourceProvenance(
            double LiveTime,
            string ConditionKey,
            string ManifestPath,
            int SpectrumCount,
            IReadOnlyList<int> SimulationIds);

        private sealed record SourceGroupKey(
            double LiveTime,
            string ConditionKey,
            string ManifestPath);
    }
}
