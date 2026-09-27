using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineraScope
{
    // 260907Codex: Persist one immutable, per-mineral composition/time schedule so Preset B can resume without reselecting compositions per live-time pool.
    internal sealed class SpectrumCompositionTimePlan
    {
        public const int CurrentSchemaVersion = 1;
        public const string CurrentPlannerVersion = "preset-b-composition-time-v1";
        // 260907Codex: Unequal weights use a distinct identity; existing equal-weight plans keep their original version and row IDs.
        public const string ShuffledTimePlannerVersion = "preset-b-composition-time-shuffle-v1";

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string PlannerVersion { get; set; } = CurrentPlannerVersion;
        public string PlanId { get; set; } = string.Empty;
        public string PlanFingerprint { get; set; } = string.Empty;
        public string MineralName { get; set; } = string.Empty;
        public int SelectionSeed { get; set; } = 42;
        public double CompositionResolution { get; set; }
        public int TotalSpectrumCount { get; set; }
        public List<SpectrumTimeAllocation> TimeAllocations { get; set; } = [];
        public List<SpectrumCompositionTimePlanEntry> Entries { get; set; } = [];
    }

    // 260907Codex: This reference is duplicated in normal manifests, allowing a deleted plan document to be safely reconstructed from planned rows.
    internal sealed class SpectrumCompositionTimePlanReference
    {
        public string PlanId { get; set; } = string.Empty;
        public string PlanFingerprint { get; set; } = string.Empty;
        public string PlannerVersion { get; set; } = string.Empty;
        public int TotalSpectrumCount { get; set; }
        public List<SpectrumTimeAllocation> TimeAllocations { get; set; } = [];
    }

    internal sealed class SpectrumCompositionTimePlanEntry
    {
        public string PlanEntryId { get; set; } = string.Empty;
        public string RowHash { get; set; } = string.Empty;
        public double LiveTime { get; set; }
        public int RepeatOrdinal { get; set; }
        public Dictionary<string, double> EndmemberFractions { get; set; } = [];

        // 260907Codex: Verify the stored composition itself, not only a copied hash string that may outlive a damaged payload.
        public bool MatchesFractions(IReadOnlyDictionary<string, double> fractions) =>
            fractions.Count == EndmemberFractions.Count
            && EndmemberFractions.All(item => fractions.TryGetValue(item.Key, out double value)
                && double.IsFinite(value)
                && Math.Abs(value - item.Value) <= 1e-9);
    }

    // 260907Codex: Build the composition set once, then spend exact live-time quotas while avoiding repeat use of a time for the same composition where possible.
    internal sealed class SpectrumCompositionTimePlanner
    {
        public const int DefaultSelectionSeed = 42;
        private const int MaxRandomAttemptsPerComposition = 10000;

        public SpectrumCompositionTimePlan Create(
            SolidSolution solution,
            double resolutionStep,
            IReadOnlyList<SpectrumTimeAllocation> allocations,
            IReadOnlyDictionary<double, string> conditionKeys,
            int selectionSeed = DefaultSelectionSeed,
            // 260907Codex: The schedule, not rounded quota differences, determines whether weights are unequal.
            bool useShuffledTimeSlots = false,
            // 260907Codex: Progress describes raw composition candidates; cancellation never changes a completed plan's random sequence.
            CancellationToken cancellationToken = default,
            Action<int>? onCandidateExamined = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(solution);
            ArgumentNullException.ThrowIfNull(allocations);
            ArgumentNullException.ThrowIfNull(conditionKeys);
            if (allocations.Count == 0)
                throw new InvalidOperationException("測定時間の配分がありません。");
            if (allocations.Any(item => item.TargetSpectrumCount <= 0))
                throw new InvalidOperationException("各測定時間には1件以上を割り当ててください。");

            int totalCount = allocations.Sum(item => item.TargetSpectrumCount);
            int plannerSeed = CreateStableSeed(selectionSeed, solution.Name, resolutionStep, totalCount);
            var random = new Random(plannerSeed);
            double[][] candidates = SelectCompositions(
                solution, resolutionStep, totalCount, random, cancellationToken, onCandidateExamined);
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Length == 0)
                throw new InvalidOperationException($"{solution.Name} に有効な組成候補がありません。");

            Shuffle(candidates, random, cancellationToken);
            var occurrences = new List<CompositionOccurrence>(totalCount);
            for (int i = 0; i < totalCount; i++)
            {
                // 260907Codex: Repeating compositions can create many rows even when the candidate set is small.
                cancellationToken.ThrowIfCancellationRequested();
                // 260907Codex: Use the same selected composition for its key and endmember map.
                double[] fractions = candidates[i % candidates.Length];
                occurrences.Add(new CompositionOccurrence(
                    solution.ComposeFractionKey(fractions),
                    solution.CreateEndmemberFractionMap(fractions, resolutionStep),
                    i / candidates.Length + 1));
            }

            // 260907Codex: Preserve equal-weight schedules exactly while shuffling exact quotas for unequal weights.
            var assignments = useShuffledTimeSlots
                ? AssignShuffledLiveTimes(occurrences, allocations, random, cancellationToken)
                : AssignLiveTimes(occurrences, allocations, random, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var entries = assignments
                .Select((assignment, index) => new SpectrumCompositionTimePlanEntry
                {
                    PlanEntryId = $"entry-{index + 1:D6}",
                    LiveTime = assignment.LiveTime,
                    RepeatOrdinal = assignment.Occurrence.RepeatOrdinal,
                    EndmemberFractions = assignment.Occurrence.Fractions,
                    RowHash = CreateRowHash(
                        solution,
                        assignment.Occurrence.Fractions,
                        assignment.LiveTime,
                        conditionKeys[assignment.LiveTime],
                        assignment.Occurrence.RepeatOrdinal)
                })
                .ToList();
            cancellationToken.ThrowIfCancellationRequested();
            var plan = new SpectrumCompositionTimePlan
            {
                // 260907Codex: Include the assignment algorithm in the persisted fingerprint and training provenance.
                PlannerVersion = useShuffledTimeSlots
                    ? SpectrumCompositionTimePlan.ShuffledTimePlannerVersion
                    : SpectrumCompositionTimePlan.CurrentPlannerVersion,
                MineralName = solution.Name,
                SelectionSeed = selectionSeed,
                CompositionResolution = resolutionStep,
                TotalSpectrumCount = totalCount,
                TimeAllocations = allocations.OrderBy(item => item.LiveTime).ToList(),
                Entries = entries
            };
            plan.PlanFingerprint = CreatePlanFingerprint(plan);
            plan.PlanId = $"preset-b-v1-{plan.PlanFingerprint[..16]}";
            cancellationToken.ThrowIfCancellationRequested();
            return plan;
        }

        // 260907Codex: Keep progress cumulative across the bounded probe and random retries without materializing rejected candidates.
        private static double[][] SelectCompositions(
            SolidSolution solution,
            double resolutionStep,
            int totalCount,
            Random random,
            CancellationToken cancellationToken,
            Action<int>? onCandidateExamined)
        {
            int examined = 0;
            Action<int>? reportProbeProgress = onCandidateExamined == null ? null : count =>
            {
                examined = count;
                onCandidateExamined(count);
            };
            double[][] probe = solution.EnumerateCandidateFractionsLazy(
                    resolutionStep, cancellationToken, reportProbeProgress)
                .Take(totalCount + 1)
                .ToArray();
            if (probe.Length <= totalCount)
                return probe;

            var selected = new Dictionary<string, double[]>(StringComparer.Ordinal);
            for (int attempt = 0; selected.Count < totalCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt >= MaxRandomAttemptsPerComposition * totalCount)
                    throw new InvalidOperationException(
                        $"{solution.Name} の組成を{totalCount}件選べませんでした。constraint または組成候補を確認してください。");
                double[] fractions = solution.SampleRandomFraction(resolutionStep, random);
                bool capable = solution.CapableComposition(fractions);
                if (onCandidateExamined != null && ++examined % 128 == 0)
                    onCandidateExamined(examined);
                if (!capable)
                    continue;
                selected.TryAdd(solution.ComposeFractionKey(fractions), fractions);
            }

            if (onCandidateExamined != null && examined % 128 != 0)
                onCandidateExamined(examined);
            return selected.Values.ToArray();
        }

        // 260907Codex: Shuffle a fixed number of time tickets so totals are exact without exhausting minority times on early compositions.
        private static IReadOnlyList<LiveTimeAssignment> AssignShuffledLiveTimes(
            IReadOnlyList<CompositionOccurrence> occurrences,
            IReadOnlyList<SpectrumTimeAllocation> allocations,
            Random random,
            CancellationToken cancellationToken)
        {
            // 260907Codex: Check before allocating tickets and throughout their shuffle while preserving the same random draws.
            cancellationToken.ThrowIfCancellationRequested();
            double[] timeSlots = allocations
                .SelectMany(item => Enumerable.Repeat(item.LiveTime, item.TargetSpectrumCount))
                .ToArray();
            Shuffle(timeSlots, random, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return occurrences.Select((occurrence, index) => new LiveTimeAssignment(occurrence, timeSlots[index])).ToArray();
        }

        // 260907Codex: Cancellation is checked at each composition/time assignment without changing ordering or tie-breaks.
        private static IReadOnlyList<LiveTimeAssignment> AssignLiveTimes(
            IReadOnlyList<CompositionOccurrence> occurrences,
            IReadOnlyList<SpectrumTimeAllocation> allocations,
            Random random,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = allocations.ToDictionary(item => item.LiveTime, item => item.TargetSpectrumCount);
            var assignments = new List<LiveTimeAssignment>(occurrences.Count);
            foreach (var group in occurrences
                .GroupBy(item => item.CompositionKey, StringComparer.Ordinal)
                .OrderByDescending(item => item.Count())
                .ThenBy(_ => random.Next()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var usedCounts = allocations.ToDictionary(item => item.LiveTime, _ => 0);
                foreach (var occurrence in group.OrderBy(_ => random.Next()))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double liveTime = allocations
                        .Where(item => remaining[item.LiveTime] > 0)
                        .OrderBy(item => usedCounts[item.LiveTime])
                        .ThenByDescending(item => (double)remaining[item.LiveTime] / item.TargetSpectrumCount)
                        .ThenBy(_ => random.Next())
                        .ThenBy(item => item.LiveTime)
                        .First()
                        .LiveTime;
                    remaining[liveTime]--;
                    usedCounts[liveTime]++;
                    assignments.Add(new LiveTimeAssignment(occurrence, liveTime));
                }
            }

            if (remaining.Values.Any(value => value != 0))
                throw new InvalidOperationException("測定時間の割り当てが目標本数と一致しませんでした。");
            return assignments;
        }

        private static string CreateRowHash(
            SolidSolution solution,
            IReadOnlyDictionary<string, double> fractions,
            double liveTime,
            string conditionKey,
            int repeatOrdinal) =>
            CreateHash(string.Join("\n",
                "physical-row-v1",
                solution.Name,
                solution.ComposeFractionKey(fractions),
                liveTime.ToString("G17", CultureInfo.InvariantCulture),
                conditionKey,
                repeatOrdinal.ToString(CultureInfo.InvariantCulture)));

        private static string CreatePlanFingerprint(SpectrumCompositionTimePlan plan)
        {
            string text = string.Join("\n", new[]
            {
                plan.PlannerVersion,
                plan.MineralName,
                plan.SelectionSeed.ToString(CultureInfo.InvariantCulture),
                plan.CompositionResolution.ToString("G17", CultureInfo.InvariantCulture),
                plan.TotalSpectrumCount.ToString(CultureInfo.InvariantCulture),
                string.Join(";", plan.TimeAllocations.Select(item =>
                    $"{item.LiveTime.ToString("G17", CultureInfo.InvariantCulture)}:{item.TargetSpectrumCount}")),
                string.Join(";", plan.Entries.Select(item =>
                    $"{item.PlanEntryId}:{item.RowHash}"))
            });
            return CreateHash(text);
        }

        private static int CreateStableSeed(int baseSeed, string mineralName, double resolutionStep, int totalCount)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                baseSeed.ToString(CultureInfo.InvariantCulture),
                mineralName,
                resolutionStep.ToString("G17", CultureInfo.InvariantCulture),
                totalCount.ToString(CultureInfo.InvariantCulture))));
            return BinaryPrimitives.ReadInt32LittleEndian(hash.AsSpan(0, sizeof(int))) & int.MaxValue;
        }

        private static string CreateHash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        // 260907Codex: Cancellation checks do not consume random values or alter the Fisher-Yates permutation.
        private static void Shuffle<T>(IList<T> values, Random random, CancellationToken cancellationToken)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int swapIndex = random.Next(index + 1);
                (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
            }
        }

        private sealed record CompositionOccurrence(
            string CompositionKey,
            Dictionary<string, double> Fractions,
            int RepeatOrdinal);

        private sealed record LiveTimeAssignment(CompositionOccurrence Occurrence, double LiveTime);
    }

    // 260907Codex: Plan documents are durable copies; manifests retain plan references and rows so deleting only this file never requires regenerating spectra.
    internal static class SpectrumCompositionTimePlanStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string GetPlanPath(string spectrumOutputFolder, string mineralName, string planId) =>
            Path.Combine(
                spectrumOutputFolder,
                SpectrumPoolRepository.SanitizeFileName(mineralName),
                "_plans",
                $"{planId}.json");

        public static SpectrumCompositionTimePlan LoadOrCreate(
            string spectrumOutputFolder,
            SpectrumCompositionTimePlan proposed,
            IReadOnlyList<SpectrumPoolManifest> manifests,
            // 260907Codex: Preview and baseline checks validate existing plans without creating or restoring files.
            bool persist = true)
        {
            string path = GetPlanPath(spectrumOutputFolder, proposed.MineralName, proposed.PlanId);
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<SpectrumCompositionTimePlan>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidOperationException($"組成・測定時間計画を読み込めませんでした: {path}");
                // 260907Codex: Metadata and unique row payloads must agree with the deterministic proposal before accepting a saved plan.
                if (!string.Equals(loaded.PlanFingerprint, proposed.PlanFingerprint, StringComparison.Ordinal)
                    || loaded.SchemaVersion != proposed.SchemaVersion
                    || loaded.PlannerVersion != proposed.PlannerVersion
                    || loaded.PlanId != proposed.PlanId
                    || loaded.MineralName != proposed.MineralName
                    || loaded.SelectionSeed != proposed.SelectionSeed
                    || loaded.CompositionResolution != proposed.CompositionResolution
                    || loaded.TotalSpectrumCount != proposed.TotalSpectrumCount
                    || !loaded.TimeAllocations.SequenceEqual(proposed.TimeAllocations))
                    throw new InvalidOperationException($"既存の組成・測定時間計画が現在の条件と一致しません: {path}");
                var expectedRows = proposed.Entries.ToDictionary(entry => entry.PlanEntryId, StringComparer.Ordinal);
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                bool rowsMatch = loaded.Entries.Count == proposed.Entries.Count
                    && loaded.Entries.All(entry => seenIds.Add(entry.PlanEntryId)
                        && expectedRows.TryGetValue(entry.PlanEntryId, out var expected)
                        && string.Equals(expected.RowHash, entry.RowHash, StringComparison.Ordinal)
                        && expected.LiveTime == entry.LiveTime
                        && expected.RepeatOrdinal == entry.RepeatOrdinal
                        && expected.MatchesFractions(entry.EndmemberFractions));
                if (!rowsMatch)
                    throw new InvalidOperationException($"既存の組成・測定時間計画の行が壊れているか、現在の条件と一致しません: {path}");
                return loaded;
            }

            var plannedRows = manifests
                // 260907Codex: A reused spectrum may reference this plan through an additive association rather than its original PlanId.
                .SelectMany(manifest => manifest.Spectra.SelectMany(entry => entry.GetPlanBindings()
                    .Where(identity => string.Equals(identity.PlanId, proposed.PlanId, StringComparison.Ordinal))
                    .Select(identity => new { Entry = entry, Identity = identity, LiveTime = manifest.Condition.SemEdxCondition.LiveTime })))
                .ToArray();
            // 260907Codex: A reference without all rows is evidence of an incomplete lost plan, not permission to silently create a new one.
            bool hasReference = manifests.Any(manifest => manifest.CompositionTimePlans
                .Any(reference => reference.PlanId == proposed.PlanId));
            if (plannedRows.Length > 0 || hasReference)
            {
                var expected = proposed.Entries.ToDictionary(entry => entry.PlanEntryId, StringComparer.Ordinal);
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                bool isComplete = plannedRows.Length == expected.Count
                    && plannedRows.All(row => row.Identity.PlanEntryId is not null
                        && seenIds.Add(row.Identity.PlanEntryId)
                        && expected.TryGetValue(row.Identity.PlanEntryId, out var expectedEntry)
                        && string.Equals(row.Identity.PlanRowHash, expectedEntry.RowHash, StringComparison.Ordinal)
                        && row.LiveTime == expectedEntry.LiveTime
                        && row.Identity.PlanRepeatOrdinal == expectedEntry.RepeatOrdinal
                        && expectedEntry.MatchesFractions(row.Entry.EndmemberFractions));
                if (!isComplete)
                    throw new InvalidOperationException("計画ファイルが失われ、manifest 内の計画行も不完全です。既存データを自動で組み替えません。");
            }

            if (persist)
                DurableJsonFile.Replace(path, JsonSerializer.Serialize(proposed, JsonOptions));
            return proposed;
        }

        public static void AttachReference(SpectrumPoolManifest manifest, SpectrumCompositionTimePlan plan)
        {
            manifest.CompositionTimePlans ??= [];
            if (manifest.CompositionTimePlans.Any(item => string.Equals(item.PlanId, plan.PlanId, StringComparison.Ordinal)))
                return;
            manifest.CompositionTimePlans.Add(new SpectrumCompositionTimePlanReference
            {
                PlanId = plan.PlanId,
                PlanFingerprint = plan.PlanFingerprint,
                PlannerVersion = plan.PlannerVersion,
                TotalSpectrumCount = plan.TotalSpectrumCount,
                TimeAllocations = plan.TimeAllocations.ToList()
            });
        }
    }
}
