using System.Text.Json;

namespace MineraScope.Tests
{
    // 260907Codex: Validate training input selection and exported provenance without constructing or running a TensorFlow model.
    internal static class TrainingPoolTests
    {
        public static void Run()
        {
            AllMineralsMustBeCompleteBeforeTraining();
            UnequalWeightsUseShuffledPlannerAndProvenance();
        }

        // 260907Codex: A complete first mineral must not leak into training when a later mineral still has shortages.
        private static void AllMineralsMustBeCompleteBeforeTraining()
        {
            using var fixture = new TestFixture(null, TestFixture.CreateSolution("SyntheticFirst"), TestFixture.CreateSolution("SyntheticSecond"));
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            TestAssert.Equal(16, reservations.Length, "Two mineral plans must reserve eight spectra each.");
            fixture.Complete(reservations.Where(row => row.SolutionName == "SyntheticFirst"));
            var incomplete = fixture.Workflow.CreateTrainingPools(fixture.Request, out var shortages);
            TestAssert.Equal(0, incomplete.Count, "Any mineral shortage must prevent all training pools from being returned.");
            TestAssert.Equal(8, shortages.Sum(row => row.MissingCount), "Only the second mineral should be missing.");
            TestAssert.True(shortages.All(row => row.MineralName == "SyntheticSecond"), "Completed first-mineral rows must not appear in shortages.");

            fixture.Complete(reservations.Where(row => row.SolutionName == "SyntheticSecond"));
            var pools = fixture.Workflow.CreateTrainingPools(fixture.Request, out shortages);
            TestAssert.Equal(0, shortages.Count, "Every exact scheduled row is now complete.");
            TestAssert.Equal(2, pools.Count, "Both complete minerals must be returned together.");
            VerifySamplesAndProvenance(fixture, pools, SpectrumCompositionTimePlan.CurrentPlannerVersion);

            var readOnlyWorkflow = new SpectrumPoolWorkflow(fixture.Repository, new SimulationPlanBuilder(), persistManifestRepairs: false);
            foreach (var solution in fixture.Request.SelectedMineralSolutions)
            {
                var plan = fixture.ReadPlan(solution);
                string path = SpectrumCompositionTimePlanStore.GetPlanPath(fixture.Request.Paths.SpectrumOutputFolder, solution.Name, plan.PlanId);
                fixture.AssertOwnedPath(path);
                File.Delete(path);
            }

            var before = fixture.Snapshot();
            TestAssert.Equal(2, readOnlyWorkflow.CreateTrainingPools(fixture.Request, out _).Count, "Read-only training inspection must accept complete manifest plan rows.");
            TestAssert.SequenceEqual(before, fixture.Snapshot(), "Read-only training inspection must not restore deleted plan documents.");
        }

        // 260907Codex: Follow the actual request/workflow route, including unequal weights whose rounded quotas happen to become equal.
        private static void UnequalWeightsUseShuffledPlannerAndProvenance()
        {
            foreach (var schedule in new[]
            {
                new SpectrumTimeSchedule(12, [new(1, 3), new(5, 1)]),
                new SpectrumTimeSchedule(2, [new(1, 2), new(5, 3)])
            })
            {
                using var fixture = new TestFixture(schedule);
                var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
                var solution = fixture.Request.SelectedMineralSolutions.Single();
                var actualPlan = fixture.ReadPlan(solution);
                var allocations = fixture.Request.GetEffectiveSpectrumTimeAllocations();
                var expectedPlan = new SpectrumCompositionTimePlanner().Create(solution, fixture.Request.Simulation.ResolutionStep, allocations,
                    allocations.ToDictionary(item => item.LiveTime, item => fixture.Handle(solution, item.LiveTime).ConditionKey), useShuffledTimeSlots: true);
                TestAssert.Equal(expectedPlan.PlanId, actualPlan.PlanId, "Unequal request weights must take the shuffled assignment route.");
                TestAssert.Equal(SpectrumCompositionTimePlan.ShuffledTimePlannerVersion, actualPlan.PlannerVersion, "Persisted unequal-weight plans must identify the shuffled planner.");
                fixture.Complete(reservations);
                var pools = fixture.Workflow.CreateTrainingPools(fixture.Request, out var shortages);
                TestAssert.Equal(0, shortages.Count, "A completed weighted plan must be trainable.");
                VerifySamplesAndProvenance(fixture, pools, SpectrumCompositionTimePlan.ShuffledTimePlannerVersion);
            }
        }

        // 260907Codex: Verify each selected sample against its manifest before checking that provenance records exactly the same identities and quotas.
        private static void VerifySamplesAndProvenance(TestFixture fixture, IReadOnlyList<SpectrumTrainingPool> pools, string plannerVersion)
        {
            int perMineral = fixture.Request.SpectrumTimeSchedule!.TotalSpectrumCount;
            foreach (var pool in pools)
            {
                var solution = fixture.Request.SelectedMineralSolutions.Single(item => item.Name == pool.MineralName);
                var plan = fixture.ReadPlan(solution);
                TestAssert.Equal(perMineral, pool.Samples.Count, "Training must consume exactly the requested number per mineral.");
                TestAssert.SequenceEqual(solution.Members.Select(member => member.Name), pool.EndmemberNames, "Regression endmember names must retain their database ordering.");
                foreach (var allocation in fixture.Request.GetEffectiveSpectrumTimeAllocations())
                    TestAssert.Equal(allocation.TargetSpectrumCount, pool.Samples.Count(sample => sample.Source!.LiveTime == allocation.LiveTime), "Training samples must retain the exact time quotas.");

                var selectedIds = new List<string>();
                foreach (var sample in pool.Samples)
                {
                    TestAssert.True(sample.Source is not null, "Every planned training spectrum needs source identity.");
                    var source = sample.Source!;
                    fixture.AssertOwnedPath(sample.FilePath);
                    fixture.AssertOwnedPath(source.ManifestPath);
                    var manifest = fixture.Repository.Load(source.ManifestPath)!;
                    var row = manifest.Spectra.Single(entry => entry.SimulationId == source.SimulationId);
                    TestAssert.Equal(SpectrumManifestStatus.Completed, row.Status, "Only manifest Completed spectra may enter training.");
                    TestAssert.Equal(plan.PlanId, source.CompositionTimePlanId, "Training source must carry the mineral's exact plan ID.");
                    TestAssert.Equal(plannerVersion, source.CompositionTimePlannerVersion, "Training source must retain its actual planner version.");
                    TestAssert.Equal(manifest.ConditionKey, source.ConditionKey, "Training source condition key must identify its own manifest.");
                    TestAssert.Equal(manifest.Condition.SemEdxCondition.LiveTime, source.LiveTime, "Training source time must match its physical pool.");
                    TestAssert.Equal(Path.Combine(Path.GetDirectoryName(source.ManifestPath)!, row.FileName), sample.FilePath, "Training file path must retain the selected physical reservation.");
                    TestAssert.Equal(solution.ComposeFractionKey(row.EndmemberFractions), solution.ComposeFractionKey(sample.EndmemberFractions), "Regression fractions must come from the selected manifest row.");
                    TestAssert.True(EmsaSpectrumIntegrityValidator.TryValidate(sample.FilePath, manifest.Condition.SemEdxCondition, out string reason), $"Training selected an invalid EMSA: {reason}");
                    selectedIds.Add(row.PlanEntryId!);
                }

                TestAssert.SequenceEqual(plan.Entries.Select(row => row.PlanEntryId).Order(), selectedIds.Order(), "Training must select every planned entry exactly once.");
            }

            var specification = new TrainingDataProvenanceSpecification(fixture.Request.GetEffectiveSpectrumTimeAllocations(), 42, 42, fixture.Request.Training.ValidationSplit);
            TrainingDataProvenanceWriter.Write(fixture.Request.Paths.ModelOutputFolder, specification, pools);
            string provenancePath = Path.Combine(fixture.Request.Paths.ModelOutputFolder, ModelArtifactPaths.TrainingDataProvenanceFileName);
            using var document = JsonDocument.Parse(File.ReadAllText(provenancePath));
            var root = document.RootElement;
            TestAssert.Equal(2, root.GetProperty("schemaVersion").GetInt32(), "Provenance schema version must cover composition/time plans.");
            TestAssert.Equal(plannerVersion, root.GetProperty("selectionMethod").GetString(), "Provenance selection method must describe the actual planner.");
            TestAssert.Equal(42, root.GetProperty("selectionSeed").GetInt32(), "Provenance must record the planner selection seed.");
            TestAssert.Equal(42, root.GetProperty("splitSeed").GetInt32(), "Provenance must record the preserved legacy split seed.");
            TestAssert.Equal("legacy-nonstratified-seed-42", root.GetProperty("splitMethod").GetString(), "Preset B must not silently claim a new training split method.");
            TestAssert.Equal(pools.Sum(pool => pool.Samples.Count), root.GetProperty("totalSpectrumCount").GetInt32(), "Provenance total must equal the selected training input.");
            TestAssert.SequenceEqual(
                pools.SelectMany(pool => pool.Samples).Select(sample => sample.Source!.CompositionTimePlanId).Distinct().Order(),
                root.GetProperty("compositionTimePlanIds").EnumerateArray().Select(item => item.GetString()).Order(),
                "Provenance must contain all and only selected plan IDs.");
            TestAssert.SequenceEqual(
                fixture.Request.GetEffectiveSpectrumTimeAllocations().Select(item => (item.LiveTime, item.TargetSpectrumCount)),
                root.GetProperty("requestedTimeAllocations").EnumerateArray().Select(item => (item.GetProperty("liveTime").GetDouble(), item.GetProperty("spectrumCountPerMineral").GetInt32())),
                "Provenance requested time quotas must remain exact.");

            foreach (var mineral in root.GetProperty("minerals").EnumerateArray())
            {
                var pool = pools.Single(item => item.MineralName == mineral.GetProperty("mineralName").GetString());
                TestAssert.Equal(pool.Samples.Count, mineral.GetProperty("spectrumCount").GetInt32(), "Provenance mineral count differs from the training input.");
                foreach (var source in mineral.GetProperty("sources").EnumerateArray())
                {
                    double liveTime = source.GetProperty("liveTime").GetDouble();
                    string manifestPath = source.GetProperty("manifestPath").GetString()!;
                    var samples = pool.Samples.Where(item => item.Source!.LiveTime == liveTime && item.Source.ManifestPath == manifestPath).ToArray();
                    TestAssert.True(samples.Length > 0, "Provenance must not introduce an unused source pool.");
                    TestAssert.Equal(samples.Length, source.GetProperty("spectrumCount").GetInt32(), "Provenance source count differs from the selected rows.");
                    TestAssert.Equal(samples[0].Source!.ConditionKey, source.GetProperty("conditionKey").GetString(), "Provenance condition key differs from the selected pool.");
                    TestAssert.SequenceEqual(samples.Select(item => item.Source!.SimulationId).Order(), source.GetProperty("simulationIds").EnumerateArray().Select(item => item.GetInt32()), "Provenance must record the exact selected simulation IDs.");
                }
            }
        }
    }
}
