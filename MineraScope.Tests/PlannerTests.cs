using MineraScope;

namespace MineraScope.Tests
{
    // 260907Codex: Exercise the production planner with small boundary cases and the normal 1000-spectrum preset.
    internal static class PlannerTests
    {
        // 260907Codex: Cover candidate counts above, equal to and below the requested total without invoking simulation.
        public static void Run()
        {
            LargestRemainderPreservesTotals();
            TooFewSpectraRejectsSchedule();
            foreach (var (totalCount, gridCount, timeCount) in new[]
            {
                (8, 10, 5),
                (10, 10, 5),
                (8, 3, 5),
                (11, 4, 3),
                (1000, 101, 5)
            })
            {
                var allocations = new SpectrumTimeSchedule(
                    totalCount,
                    Enumerable.Range(1, timeCount).Select(time => new SpectrumTimeWeight(time, 1)).ToArray())
                    .CreateAllocations();
                VerifyPlan(totalCount, gridCount, allocations);
            }
            UnevenTimeSlotsAvoidCompositionBias();
        }

        // 260907Codex: Deliberately supply unsorted times to test the documented remainder tie-break rather than input order.
        private static void LargestRemainderPreservesTotals()
        {
            var equal = new SpectrumTimeSchedule(11, [new(20, 1), new(1, 1), new(5, 1)])
                .CreateAllocations();
            TestAssert.SequenceEqual(new[] { 1.0, 5.0, 20.0 }, equal.Select(item => item.LiveTime),
                "Largest-remainder allocations must be sorted by live time.");
            TestAssert.SequenceEqual(new[] { 4, 4, 3 }, equal.Select(item => item.TargetSpectrumCount),
                "Equal remainder ties must go to the smaller live times.");

            var weighted = new SpectrumTimeSchedule(11, [new(20, 3), new(1, 1), new(5, 2)])
                .CreateAllocations();
            TestAssert.SequenceEqual(new[] { 2, 4, 5 }, weighted.Select(item => item.TargetSpectrumCount),
                "Weights 1:2:3 must allocate eleven spectra as 2:4:5.");
            VerifyPlan(11, 4, weighted, useShuffledTimeSlots: true);
        }

        // 260907Codex: N=3, G=10, K=5 must fail at schedule validation before a planner can reserve any rows.
        private static void TooFewSpectraRejectsSchedule()
        {
            var solution = TestFixture.CreateSolution();
            TestAssert.Equal(10, solution.EnumerateCandidateFractionsLazy(1.0 / 9).Count(),
                "The N=3 rejection fixture must contain ten candidate compositions.");
            var schedule = new SpectrumTimeSchedule(3,
                Enumerable.Range(1, 5).Select(time => new SpectrumTimeWeight(time, 1)).ToArray());
            TestAssert.Throws<InvalidOperationException>(() => schedule.CreateAllocations(),
                "A total below the number of live-time conditions must be rejected.");
        }

        // 260907Codex: A fixed 750:250 shuffle must spread the minority time beyond the old fifty 5:5 / fifty 10:0 pattern.
        private static void UnevenTimeSlotsAvoidCompositionBias()
        {
            var allocations = new SpectrumTimeSchedule(1000, [new(1, 3), new(5, 1)]).CreateAllocations();
            var plan = VerifyPlan(1000, 100, allocations, useShuffledTimeSlots: true);
            var solution = TestFixture.CreateSolution();
            var groups = plan.Entries.GroupBy(entry => solution.ComposeFractionKey(entry.EndmemberFractions)).ToArray();
            TestAssert.True(groups.All(group => group.Count() == 10),
                "One hundred compositions must each retain ten occurrences.");
            int[] minorityCounts = groups.Select(group => group.Count(entry => entry.LiveTime == 5)).ToArray();
            TestAssert.True(minorityCounts.Any(count => count is > 0 and < 5),
                "Shuffled slots must include compositions between the old 0- and 5-minority-time extremes.");
            TestAssert.True(minorityCounts.Distinct().Count() >= 3,
                "Uneven time quotas must not collapse into two blocks of composition-dependent allocations.");
            TestAssert.True(!(minorityCounts.Count(count => count == 5) == 50
                && minorityCounts.Count(count => count == 0) == 50),
                "The old fifty 5:5 / fifty 10:0 composition split must not recur.");
        }

        // 260907Codex: Share invariant checks across grids so exact quotas, physical rows and deterministic repeats are always checked together.
        private static SpectrumCompositionTimePlan VerifyPlan(
            int totalCount,
            int gridCount,
            IReadOnlyList<SpectrumTimeAllocation> allocations,
            bool useShuffledTimeSlots = false)
        {
            var solution = TestFixture.CreateSolution();
            double resolution = 1.0 / (gridCount - 1);
            double[][] candidates = solution.EnumerateCandidateFractionsLazy(resolution).ToArray();
            string context = $"N={totalCount}, G={gridCount}, K={allocations.Count}";
            TestAssert.Equal(gridCount, candidates.Length, $"{context}: unexpected fixture grid size.");
            var candidateKeys = candidates
                .Select(fractions => solution.ComposeFractionKey(solution.CreateEndmemberFractionMap(fractions, resolution)))
                .ToHashSet(StringComparer.Ordinal);
            TestAssert.Equal(gridCount, candidateKeys.Count, $"{context}: percent normalization merged candidate compositions.");

            var conditionKeys = allocations.ToDictionary(item => item.LiveTime,
                item => "test-condition-" + item.LiveTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var planner = new SpectrumCompositionTimePlanner();
            // 260907Codex: Equal-weight coverage also proves the optional argument retains the existing default path.
            var plan = useShuffledTimeSlots
                ? planner.Create(solution, resolution, allocations, conditionKeys, useShuffledTimeSlots: true)
                : planner.Create(solution, resolution, allocations, conditionKeys);
            var repeated = planner.Create(solution, resolution, allocations, conditionKeys,
                useShuffledTimeSlots: useShuffledTimeSlots);
            TestAssert.Equal(useShuffledTimeSlots
                    ? SpectrumCompositionTimePlan.ShuffledTimePlannerVersion
                    : SpectrumCompositionTimePlan.CurrentPlannerVersion,
                plan.PlannerVersion, $"{context}: planner version does not match the requested assignment mode.");
            TestAssert.Equal(totalCount, plan.TotalSpectrumCount, $"{context}: plan total changed.");
            TestAssert.Equal(totalCount, plan.Entries.Count, $"{context}: row count differs from the requested total.");
            TestAssert.SequenceEqual(allocations.OrderBy(item => item.LiveTime), plan.TimeAllocations,
                $"{context}: stored time quotas changed.");
            TestAssert.Equal(plan.PlanId, repeated.PlanId, $"{context}: plan ID is not deterministic.");
            TestAssert.Equal(plan.PlanFingerprint, repeated.PlanFingerprint, $"{context}: fingerprint is not deterministic.");
            // 260907Codex: Captured by comparing full plans against the original Release assembly built 2026-09-07 16:37 JST,
            // SHA256 00A3FD7DCCDE6B9C4AC5D31C74E04E09470FDB9DF390AB169DA097CED3EB02F3. Keep these historical IDs testable without that binary.
            if (!useShuffledTimeSlots)
            {
                string? historicalFingerprint = (totalCount, gridCount, allocations.Count) switch
                {
                    (8, 10, 5) => "d774d79f3ad11a26deb362ec20d762aaf249ce1e93555618e4213921fa0c54f2",
                    (8, 3, 5) => "eb9e15bf5fbec84fbf862eff28d460075fcedf3236f030e813410db12729f186",
                    (11, 4, 3) => "7361c774400694fed00f5974a92664b0ab268da7c42980d6601895db1dbf671e",
                    (1000, 101, 5) => "a46ae80fbd858358446c3df5735f9def69fc0f6cce66223eb6223aab489b438c",
                    _ => null
                };
                if (historicalFingerprint is not null)
                {
                    TestAssert.Equal(historicalFingerprint, plan.PlanFingerprint, $"{context}: equal-plan fingerprint differs from the verified original release.");
                    TestAssert.Equal($"preset-b-v1-{historicalFingerprint[..16]}", plan.PlanId, $"{context}: equal-plan ID differs from the verified original release.");
                }
            }
            TestAssert.SequenceEqual(
                plan.Entries.Select(entry => (entry.PlanEntryId, entry.RowHash, entry.LiveTime, entry.RepeatOrdinal,
                    solution.ComposeFractionKey(entry.EndmemberFractions))),
                repeated.Entries.Select(entry => (entry.PlanEntryId, entry.RowHash, entry.LiveTime, entry.RepeatOrdinal,
                    solution.ComposeFractionKey(entry.EndmemberFractions))),
                $"{context}: deterministic replay changed the planned rows.");
            TestAssert.Equal(totalCount, plan.Entries.Select(entry => entry.PlanEntryId).Distinct().Count(),
                $"{context}: duplicate entry IDs.");
            TestAssert.Equal(totalCount, plan.Entries.Select(entry => entry.RowHash).Distinct().Count(),
                $"{context}: duplicate physical rows.");
            foreach (var allocation in allocations)
                TestAssert.Equal(allocation.TargetSpectrumCount,
                    plan.Entries.Count(entry => entry.LiveTime == allocation.LiveTime),
                    $"{context}: live time {allocation.LiveTime} does not meet its exact quota.");

            var groups = plan.Entries.GroupBy(entry => solution.ComposeFractionKey(entry.EndmemberFractions)).ToArray();
            TestAssert.Equal(Math.Min(totalCount, gridCount), groups.Length,
                $"{context}: selected composition count must equal min(N, G).");
            TestAssert.True(groups.Max(group => group.Count()) - groups.Min(group => group.Count()) <= 1,
                $"{context}: repeat counts must differ by at most one.");
            // 260907Codex: Equal-weight schedules must balance every composition across times, including times with zero occurrences.
            if (!useShuffledTimeSlots)
                foreach (var group in groups)
                {
                    int[] timeCounts = allocations.Select(allocation => group.Count(entry => entry.LiveTime == allocation.LiveTime)).ToArray();
                    TestAssert.True(timeCounts.Max() - timeCounts.Min() <= 1,
                        $"{context}: equal-time counts within a composition must differ by at most one.");
                    foreach (var block in group.Chunk(allocations.Count))
                        TestAssert.Equal(block.Length, block.Select(entry => entry.LiveTime).Distinct().Count(),
                            $"{context}: each composition's successive K-row blocks must use distinct live times.");
                }
            // 260907Codex: The normal 1000-row preset uses ninety-one ten-repeat groups and ten nine-repeat groups.
            if (totalCount == 1000 && gridCount == 101)
            {
                TestAssert.Equal(91, groups.Count(group => group.Count() == 10), "N=1000, G=101 must have 91 ten-repeat compositions.");
                TestAssert.Equal(10, groups.Count(group => group.Count() == 9), "N=1000, G=101 must have 10 nine-repeat compositions.");
            }
            foreach (var group in groups)
                TestAssert.SequenceEqual(Enumerable.Range(1, group.Count()),
                    group.Select(entry => entry.RepeatOrdinal).Order(),
                    $"{context}: repeats must be unique and consecutive within a composition.");
            foreach (var entry in plan.Entries)
            {
                TestAssert.SequenceEqual(solution.Members.Select(member => member.Name).Order(),
                    entry.EndmemberFractions.Keys.Order(), $"{context}: endmember labels changed.");
                double[] fractions = solution.Members.Select(member => entry.EndmemberFractions[member.Name]).ToArray();
                TestAssert.True(fractions.All(value => double.IsFinite(value) && value >= 0 && value <= 1),
                    $"{context}: fractions must remain finite and inside [0, 1].");
                TestAssert.True(Math.Abs(fractions.Sum() - 1) < 1e-12,
                    $"{context}: endmember fractions must sum to one.");
                TestAssert.True(solution.CapableComposition(fractions), $"{context}: composition violates its constraints.");
                TestAssert.True(candidateKeys.Contains(solution.ComposeFractionKey(entry.EndmemberFractions)),
                    $"{context}: a planned composition is outside the normalized candidate grid.");
            }
            return plan;
        }
    }
}
