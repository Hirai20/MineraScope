using System.Text.Json;

namespace MineraScope.Tests
{
    // 260907Codex: Exercise durable plan identities using production repository/workflow entry points and disposable synthetic pools.
    internal static class PoolTests
    {
        public static void Run()
        {
            PreviewIsReadOnlyAndLegacyRowsResume();
            InvalidCompletedFilesRemainShortages();
            PendingRecoveryKeepsReservations();
            DeletedPlanRestoresOnlyFromCompleteConsistentRows();
            CompletedRowsCanBelongToMultiplePlans();
        }

        // 260907Codex: Adopt exact legacy rows once, then cancel and retry without allocating new identities or replacing Completed spectra.
        private static void PreviewIsReadOnlyAndLegacyRowsResume()
        {
            using var fixture = new TestFixture();
            var request = fixture.Request;
            var solution = request.SelectedMineralSolutions.Single();
            var initialSnapshot = fixture.Snapshot();
            TestAssert.Equal(8, fixture.Workflow.PreviewMissingSimulationPlan(request, out _).SpectrumCount, "An empty preview must report the complete schedule.");
            TestAssert.SequenceEqual(initialSnapshot, fixture.Snapshot(), "Preview of an empty pool must not create directories or plans.");

            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            var proposed = new SpectrumCompositionTimePlanner().Create(solution, request.Simulation.ResolutionStep, allocations,
                allocations.ToDictionary(item => item.LiveTime, item => fixture.Handle(solution, item.LiveTime).ConditionKey));
            var legacyIdentities = new List<string>();
            int legacyId = 100;
            foreach (var row in proposed.Entries.Take(2))
            {
                var handle = fixture.Handle(solution, row.LiveTime);
                var manifest = fixture.Repository.LoadOrCreate(handle);
                var entry = new SpectrumManifestEntry
                {
                    SimulationId = legacyId++,
                    FileName = $"legacy-{legacyId}.emsa",
                    Status = SpectrumManifestStatus.Completed,
                    EndmemberFractions = new Dictionary<string, double>(row.EndmemberFractions)
                };
                manifest.Spectra.Add(entry);
                manifest.NextSimulationId = entry.SimulationId + 1;
                fixture.Repository.Save(handle, manifest);
                fixture.WriteValidSpectrum(Path.Combine(handle.PoolFolder, entry.FileName), handle.Condition.SemEdxCondition);
                legacyIdentities.Add($"{handle.ManifestPath}:{entry.SimulationId}");
            }

            var seededSnapshot = fixture.Snapshot();
            TestAssert.Equal(2, fixture.Workflow.PreviewPoolStates(request).Sum(item => item.CompletedCount), "Preview must count exactly matching valid legacy spectra.");
            TestAssert.Equal(6, fixture.Workflow.PreviewMissingSimulationPlan(request, out _).SpectrumCount, "Preview must deduct reusable legacy rows.");
            TestAssert.SequenceEqual(seededSnapshot, fixture.Snapshot(), "Preview must not tag legacy rows or write a plan.");

            var executionPlan = fixture.Workflow.CreateMissingSimulationPlan(request, out var shortages);
            var reservations = TestFixture.Reservations(executionPlan);
            // 260907Codex: Time-specific jobs must stay inside the shared parallel budget and must not overwrite another job's script.
            TestAssert.True(executionPlan.Batches.All(batch => batch.Jobs.Count <= request.Simulation.ParallelCount), "Each mineral batch must stay inside the requested parallel job budget.");
            var jobs = executionPlan.Batches.SelectMany(batch => batch.Jobs).ToArray();
            TestAssert.Equal(jobs.Length, jobs.Select(job => job.ScriptPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Each execution job must have a distinct script path.");
            TestAssert.True(jobs.All(job => job.Reservations.Select(row => row.SemEdxCondition.LiveTime).Distinct().Count() == 1), "Each job must keep one physical live-time condition.");
            TestAssert.Equal(6, reservations.Length, "Only the six unfulfilled physical plan rows should be reserved.");
            TestAssert.Equal(6, shortages.Sum(item => item.MissingCount), "Reservation shortages must match missing plan rows.");
            var identities = ReadIdentities(fixture);
            TestAssert.Equal(8, identities.Length, "One plan must contain exactly eight manifest rows after legacy adoption.");
            TestAssert.True(legacyIdentities.All(identity => identities.Any(row => row.PhysicalIdentity == identity && row.Status == SpectrumManifestStatus.Completed)), "Legacy SimulationIds must survive adoption.");
            TestAssert.Equal(8, identities.Select(row => row.PlanEntryId).Distinct().Count(), "Every planned row must be bound exactly once across time pools.");

            var saved = reservations[0];
            fixture.WriteValidSpectrum(Path.Combine(saved.PoolFolder, saved.FileName), saved.SemEdxCondition);
            foreach (var reservation in reservations)
                fixture.Workflow.ApplySimulationResults([new SimulationExecutionResult(
                    [reservation], -1, string.Empty, string.Empty, null, IsCanceled: true,
                    SavedSpectrumFiles: reservation == saved ? new HashSet<string> { reservation.FileName } : null)]);

            var afterCancellation = ReadIdentities(fixture);
            TestAssert.Equal(3, afterCancellation.Count(row => row.Status == SpectrumManifestStatus.Completed), "Canceled work must preserve the file whose save marker was received.");
            TestAssert.Equal(5, afterCancellation.Count(row => row.Status == SpectrumManifestStatus.Pending), "Canceled unsaved rows must remain retryable Pending.");
            var retried = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(request, out _));
            TestAssert.Equal(5, retried.Length, "Retry must exclude all three Completed rows.");
            TestAssert.SequenceEqual(
                reservations.Where(row => row != saved).Select(row => $"{row.ManifestPath}:{row.SimulationId}").Order(),
                retried.Select(row => $"{row.ManifestPath}:{row.SimulationId}").Order(),
                "Retry must retain the original reservation SimulationIds.");
            TestAssert.SequenceEqual(identities.Select(IdentityOnly), ReadIdentities(fixture).Select(IdentityOnly), "Retry must preserve plan IDs, row hashes, repeat ordinals, and physical filenames.");
            TestAssert.True(ReadIdentities(fixture).Where(row => row.Status == SpectrumManifestStatus.Pending).All(row => row.Attempts == 2), "Retries must increment attempts on existing rows.");
            fixture.Complete(retried);
            TestAssert.Equal(0, fixture.Workflow.GetShortages(request).Count, "A completed plan should report no shortages.");
        }

        // 260907Codex: A persisted Completed flag is insufficient when its file disappears or becomes invalid, including read-only preview.
        private static void InvalidCompletedFilesRemainShortages()
        {
            using var fixture = new TestFixture();
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            fixture.Complete(reservations);
            var original = ReadIdentities(fixture).Select(IdentityOnly).ToArray();
            string missingPath = Path.Combine(reservations[0].PoolFolder, reservations[0].FileName);
            string corruptPath = Path.Combine(reservations[1].PoolFolder, reservations[1].FileName);
            fixture.AssertOwnedPath(missingPath);
            fixture.AssertOwnedPath(corruptPath);
            File.Delete(missingPath);
            File.WriteAllText(corruptPath, "#SPECTRUM:\nNaN\n#ENDOFDATA:\n");

            var snapshot = fixture.Snapshot();
            TestAssert.Equal(6, fixture.Workflow.PreviewPoolStates(fixture.Request).Sum(item => item.CompletedCount), "Preview must exclude missing and corrupt plan-tagged Completed files.");
            TestAssert.SequenceEqual(snapshot, fixture.Snapshot(), "Preview must leave stale manifest statuses and all file bytes untouched.");
            TestAssert.Equal(0, fixture.Workflow.CreateTrainingPools(fixture.Request, out var shortages).Count, "Two invalid files must block the entire training pool.");
            TestAssert.Equal(2, shortages.Sum(item => item.MissingCount), "Training must expose both physical file failures.");
            TestAssert.Equal(2, ReadIdentities(fixture).Count(row => row.Status == SpectrumManifestStatus.Missing), "A full validation must demote invalid Completed rows to Missing.");

            var retry = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            TestAssert.Equal(2, retry.Length, "Only invalid rows should be retried.");
            foreach (var reservation in retry)
                fixture.Workflow.ApplySimulationResults([new SimulationExecutionResult([reservation], 0, string.Empty, string.Empty, null)]);
            TestAssert.Equal(6, ReadIdentities(fixture).Count(row => row.Status == SpectrumManifestStatus.Completed), "A clean exit must not mark an absent or corrupt file Completed.");
            TestAssert.SequenceEqual(original, ReadIdentities(fixture).Select(IdentityOnly), "File repair retries must not change plan identities.");
            fixture.Complete(retry);
            TestAssert.Equal(8, fixture.Workflow.CreateTrainingPools(fixture.Request, out _).Single().Samples.Count, "Valid replacements must make the original complete plan trainable.");
        }

        // 260907Codex: Recovery validates physical output after interrupted checkpoints while retaining the existing reservation rows.
        private static void PendingRecoveryKeepsReservations()
        {
            using var fixture = new TestFixture();
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            var original = ReadIdentities(fixture).Select(IdentityOnly).ToArray();
            fixture.WriteValidSpectrum(Path.Combine(reservations[0].PoolFolder, reservations[0].FileName), reservations[0].SemEdxCondition);
            string corruptPath = Path.Combine(reservations[1].PoolFolder, reservations[1].FileName);
            fixture.AssertOwnedPath(corruptPath);
            File.WriteAllText(corruptPath, "#SPECTRUM:\n1\n");
            var recovered = fixture.Workflow.RecoverPendingSpectra(fixture.Request);
            TestAssert.Equal(8, recovered.ExaminedCount, "Recovery must inspect all eight Pending rows.");
            TestAssert.Equal(1, recovered.RecoveredCount, "Only the full valid EMSA can be recovered.");
            TestAssert.Equal(1, recovered.InvalidCount, "Truncated output must remain invalid.");
            TestAssert.Equal(6, recovered.MissingFileCount, "Unsaved reservations must remain missing files.");
            TestAssert.SequenceEqual(original, ReadIdentities(fixture).Select(IdentityOnly), "Recovery must preserve every plan and simulation identity.");
            TestAssert.Equal(7, TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _)).Length, "Recovery must remove exactly one row from retry work.");
        }

        // 260907Codex: Reconstruct a deleted document only from every distinct planned row with matching physical identity and manifest references.
        private static void DeletedPlanRestoresOnlyFromCompleteConsistentRows()
        {
            using var fixture = new TestFixture();
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            fixture.Complete(reservations);
            var solution = fixture.Request.SelectedMineralSolutions.Single();
            var originalPlan = fixture.ReadPlan(solution);
            string path = SpectrumCompositionTimePlanStore.GetPlanPath(fixture.Request.Paths.SpectrumOutputFolder, solution.Name, originalPlan.PlanId);
            fixture.AssertOwnedPath(path);
            byte[] originalBytes = File.ReadAllBytes(path);
            File.Delete(path);
            TestAssert.Equal(8, fixture.Workflow.CreateTrainingPools(fixture.Request, out _).Single().Samples.Count, "Complete manifests must restore a deleted plan and remain trainable.");
            TestAssert.SequenceEqual(originalBytes, File.ReadAllBytes(path), "Plan restoration must reproduce the original document and IDs exactly.");
            File.Delete(path);

            string manifestPath = reservations[0].ManifestPath;
            var intact = fixture.Repository.Load(manifestPath)!;
            string intactJson = JsonSerializer.Serialize(intact);
            foreach (string corruption in new[] { "missing-row", "duplicate-row", "fractions", "repeat", "reference-only" })
            {
                var manifest = JsonSerializer.Deserialize<SpectrumPoolManifest>(intactJson)!;
                switch (corruption)
                {
                    case "missing-row":
                        manifest.Spectra.RemoveAt(0);
                        break;
                    case "duplicate-row":
                        TestAssert.True(manifest.Spectra.Count >= 2, "Duplicate fixture requires two rows in one time pool.");
                        manifest.Spectra[0] = manifest.Spectra[1];
                        break;
                    case "fractions":
                        manifest.Spectra[0].EndmemberFractions = new Dictionary<string, double> { ["Forsterite"] = 0.25, ["Fayalite"] = 0.75 };
                        break;
                    case "repeat":
                        manifest.Spectra[0].PlanRepeatOrdinal += 10;
                        break;
                    case "reference-only":
                        manifest.Spectra.Clear();
                        break;
                }

                fixture.Repository.Save(manifestPath, manifest);
                TestAssert.Throws<InvalidOperationException>(() => fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _), $"Deleted plan recovery must reject {corruption}.");
                TestAssert.True(!File.Exists(path), $"Rejected {corruption} must not replace the missing plan file.");
                fixture.Repository.Save(manifestPath, JsonSerializer.Deserialize<SpectrumPoolManifest>(intactJson)!);
            }

            fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _);
            TestAssert.SequenceEqual(originalBytes, File.ReadAllBytes(path), "Restoring intact manifests after rejection must retain the original plan.");
        }

        // 260907Codex: A changed total can reuse matching physical spectra while every earlier completed plan retains its own identities.
        private static void CompletedRowsCanBelongToMultiplePlans()
        {
            using var fixture = new TestFixture();
            var originalReservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            fixture.Complete(originalReservations);
            var solution = fixture.Request.SelectedMineralSolutions.Single();
            var firstPlan = fixture.ReadPlan(solution);
            var firstSamples = fixture.Workflow.CreateTrainingPools(fixture.Request, out _).Single().Samples.Select(sample => sample.FilePath).Order().ToArray();
            var secondRequest = fixture.Request with
            {
                Simulation = fixture.Request.Simulation with { TargetSpectrumCount = 10 },
                SpectrumTimeSchedule = fixture.Request.SpectrumTimeSchedule! with { TotalSpectrumCount = 10 }
            };
            var allocations = secondRequest.GetEffectiveSpectrumTimeAllocations();
            var secondPlan = new SpectrumCompositionTimePlanner().Create(solution, secondRequest.Simulation.ResolutionStep, allocations,
                allocations.ToDictionary(item => item.LiveTime, item => fixture.Handle(solution, item.LiveTime).ConditionKey));
            string CellKey(SpectrumCompositionTimePlanEntry row) =>
                solution.ComposeFractionKey(row.EndmemberFractions) + ":" + row.LiveTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var originalCounts = firstPlan.Entries.GroupBy(CellKey).ToDictionary(group => group.Key, group => group.Count());
            int reusable = secondPlan.Entries.GroupBy(CellKey).Sum(group => Math.Min(group.Count(), originalCounts.GetValueOrDefault(group.Key)));
            TestAssert.True(reusable > 0, "The changed-total fixture must have reusable physical composition/time cells.");
            var secondReservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(secondRequest, out _));
            TestAssert.Equal(10 - reusable, secondReservations.Length, "A changed plan should reserve only physical cells not already fulfilled by Completed spectra.");
            TestAssert.SequenceEqual(firstSamples,
                fixture.Workflow.CreateTrainingPools(fixture.Request, out _).Single().Samples.Select(sample => sample.FilePath).Order(),
                "Adopting Completed spectra into a second plan must keep the first plan trainable with identical files.");
            fixture.Complete(secondReservations);
            var secondSamples = fixture.Workflow.CreateTrainingPools(secondRequest, out var shortages).Single().Samples;
            TestAssert.Equal(0, shortages.Count, "The changed plan should become complete after only its missing cells are generated.");
            TestAssert.Equal(10, secondSamples.Count, "The changed plan must return its own requested total.");
            TestAssert.Equal(reusable, secondSamples.Count(sample => firstSamples.Contains(sample.FilePath)), "The second plan must actually consume the matching reused files.");
            TestAssert.True(secondSamples.All(sample => sample.Source!.CompositionTimePlanId == secondPlan.PlanId), "Reused spectra must report the selected plan's ID in training provenance.");
            string path = SpectrumCompositionTimePlanStore.GetPlanPath(secondRequest.Paths.SpectrumOutputFolder, solution.Name, secondPlan.PlanId);
            fixture.AssertOwnedPath(path);
            File.Delete(path);
            TestAssert.Equal(10, fixture.Workflow.CreateTrainingPools(secondRequest, out _).Single().Samples.Count, "Additional plan bindings must be sufficient to restore the second plan's deleted document.");
            TestAssert.SequenceEqual(firstSamples,
                fixture.Workflow.CreateTrainingPools(fixture.Request, out _).Single().Samples.Select(sample => sample.FilePath).Order(),
                "Restoring the second plan must not alter the original plan's selection.");
        }

        // 260907Codex: Compare mutable statuses separately from immutable identities across preview, interruption, and retries.
        private static RowIdentity[] ReadIdentities(TestFixture fixture) =>
            fixture.Request.SelectedMineralSolutions.SelectMany(solution => fixture.Request.GetEffectiveSpectrumTimeAllocations().SelectMany(allocation =>
            {
                var handle = fixture.Handle(solution, allocation.LiveTime);
                return (fixture.Repository.Load(handle.ManifestPath)?.Spectra ?? []).Select(entry => new RowIdentity(
                    $"{handle.ManifestPath}:{entry.SimulationId}", entry.FileName, entry.PlanId, entry.PlanEntryId,
                    entry.PlanRowHash, entry.PlanRepeatOrdinal, entry.Status, entry.GenerationAttemptCount));
            })).OrderBy(row => row.PhysicalIdentity, StringComparer.Ordinal).ToArray();

        // 260907Codex: A named identity projection keeps retries from hiding changes behind matching aggregate counts.
        private static string IdentityOnly(RowIdentity row) =>
            $"{row.PhysicalIdentity}|{row.FileName}|{row.PlanId}|{row.PlanEntryId}|{row.RowHash}|{row.Repeat}";

        // 260907Codex: Preserve enough information to detect both logical-plan and physical-file replacement.
        private sealed record RowIdentity(string PhysicalIdentity, string FileName, string? PlanId, string? PlanEntryId, string? RowHash, int? Repeat, string Status, int Attempts);
    }
}
