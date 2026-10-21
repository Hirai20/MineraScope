using System.Text.Json;

namespace MineraScope.Tests
{
    // 260907Codex: 学習の開始や実スペクトルpoolへの接触なしに、準備処理の最適化を観測可能にする。
    internal static class PreparationTests
    {
        public static void Run()
        {
            PreparationPreservesExactTrainingSelection();
            PendingRecoveryAndSelectedIntegrityKeepTrainingAllOrNone();
            ReadOnlyPreparationDoesNotPersistRecovery();
            RetainedUnselectedSpectraAreNotValidatedForBTraining();
            CachedManifestsAreScopedAndObserveFileChanges();
            DuplicateCompatiblePoolsRetainPhysicalFileRanking();
            PreparationReportsProgressAndHonorsCancellation();
            RejectedCompositionCandidatesRemainCancelable();
            // 260908Codex: 並列ファイル読み取りでも、科学的入力・進捗・修復・中止の動作を保持する。
            ParallelValidationPreservesPreparationAndRepairs();
        }

        // 260908Codex: 計画あり・旧形式の両選択経路で、128ファイルのバッチ2個と端数バッチをまたぐ入力を使う。
        private static void ParallelValidationPreservesPreparationAndRepairs()
        {
            foreach (bool planned in new[] { true, false })
            {
                using var fixture = new TestFixture(new SpectrumTimeSchedule(260, [new(1, 1)]));
                var request = planned ? fixture.Request : fixture.Request with { SpectrumTimeSchedule = null };
                var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(request, out _));
                fixture.Complete(reservations);
                var before = fixture.Snapshot();
                var serialProgress = new List<SpectrumPoolPreparationProgress>();
                var parallelProgress = new List<SpectrumPoolPreparationProgress>();
                // 260908Codex: 共有PCで不確かな速度判定を課さず、規模を限定した合成データの所要時間を表示する。
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var serial = fixture.Workflow.PrepareTrainingPools(request, new InlineProgress(serialProgress.Add));
                double serialSeconds = timer.Elapsed.TotalSeconds;
                timer.Restart();
                var parallel = fixture.Workflow.PrepareTrainingPools(request, new InlineProgress(parallelProgress.Add), validationParallelism: 4);
                Console.WriteLine($"Preparation {(planned ? "planned" : "legacy")} 260 synthetic spectra: serial {serialSeconds:F3}s, four readers {timer.Elapsed.TotalSeconds:F3}s.");
                TestAssert.Equal(JsonSerializer.Serialize(serial), JsonSerializer.Serialize(parallel), "Parallel validation must preserve sample order, labels, provenance, recovery, and shortages.");
                TestAssert.SequenceEqual(serialProgress, parallelProgress, "Parallel readers must not reorder progress callbacks or alter counts.");
                TestAssert.SequenceEqual(before, fixture.Snapshot(), "Valid parallel preparation must remain read-only.");
                TestAssert.Throws<ArgumentOutOfRangeException>(() => fixture.Workflow.PrepareTrainingPools(request, validationParallelism: 0), "Invalid read concurrency must fail before preparation starts.");

                using var canceled = new CancellationTokenSource();
                TestAssert.Throws<OperationCanceledException>(() => fixture.Workflow.PrepareTrainingPools(request,
                    new InlineProgress(value =>
                    {
                        if (value.FilesChecked == 128 && value.Phase is "採用EMSA確認" or "学習pool確認")
                            canceled.Cancel();
                    }), canceled.Token, validationParallelism: 4), "Cancellation at a read-batch progress boundary must stop preparation.");
                TestAssert.SequenceEqual(before, fixture.Snapshot(), "Canceled parallel preparation must not change source files or plans.");

                // 260908Codex: 欠損ファイル、不正な数値、実体のヘッダー不一致でも、逐次処理と同じ失敗規則を保つ。
                var damaged = new[] { reservations[0], reservations[129], reservations[259] };
                string[] paths = damaged.Select(row => Path.Combine(row.PoolFolder, row.FileName)).ToArray();
                foreach (string path in paths)
                    fixture.AssertOwnedPath(path);
                File.Delete(paths[0]);
                File.WriteAllText(paths[1], "#SPECTRUM:\nNaN\n#ENDOFDATA:\n");
                File.WriteAllText(paths[2], File.ReadAllText(paths[2]).Replace("#BEAMKV: 15", "#BEAMKV: 99", StringComparison.Ordinal));
                var readOnly = new SpectrumPoolWorkflow(fixture.Repository, new SimulationPlanBuilder(), persistManifestRepairs: false);
                before = fixture.Snapshot();
                serial = readOnly.PrepareTrainingPools(request);
                parallel = readOnly.PrepareTrainingPools(request, validationParallelism: 4);
                TestAssert.Equal(JsonSerializer.Serialize(serial), JsonSerializer.Serialize(parallel), "Invalid selected files must produce the same complete refusal and exact shortages in both modes.");
                TestAssert.Equal(0, parallel.Pools.Count, "An invalid selected file must prevent partial training.");
                TestAssert.Equal(3, parallel.Shortages.Sum(item => item.MissingCount), "Every invalid file must remain a separate missing input.");
                TestAssert.SequenceEqual(before, fixture.Snapshot(), "Read-only parallel validation must not persist Missing statuses.");

                fixture.Workflow.PrepareTrainingPools(request, validationParallelism: 4);
                string[] reasons = ["出力ファイルが見つかりませんでした。", "無効なスペクトル値があります: NaN", "#BEAMKV がmanifestの加速電圧と一致しません。"];
                for (int index = 0; index < damaged.Length; index++)
                {
                    var entry = fixture.Repository.Load(damaged[index].ManifestPath)!.Spectra.Single(row => row.SimulationId == damaged[index].SimulationId);
                    TestAssert.Equal(SpectrumManifestStatus.Missing, entry.Status, "Parallel validation must persist the same Missing repairs when enabled.");
                    TestAssert.Equal(reasons[index], entry.FailureReason, "Parallel validation must retain the exact original validation reason.");
                }
            }
        }

        // 260907Codex: 旧形式の2番目の鉱物では選択器をリセットせず、seed 42の続きの乱数列を使う。
        private static void PreparationPreservesExactTrainingSelection()
        {
            foreach (bool planned in new[] { true, false })
            {
                using var fixture = new TestFixture(null, TestFixture.CreateSolution("SyntheticFirst"), TestFixture.CreateSolution("SyntheticSecond"));
                var generationRequest = planned ? fixture.Request : fixture.Request with { SpectrumTimeSchedule = null };
                fixture.Complete(TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(generationRequest, out _)));
                var request = planned ? generationRequest : generationRequest with
                {
                    Simulation = generationRequest.Simulation with { TargetSpectrumCount = 3 }
                };
                var expected = fixture.Workflow.CreateTrainingPools(request, out var expectedShortages);
                var before = fixture.Snapshot();
                var log = new List<string>();
                var actual = fixture.Workflow.PrepareTrainingPools(request, log: log.Add);

                TestAssert.Equal(0, expectedShortages.Count, "The comparison fixture must already be complete.");
                TestAssert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual.Pools), "Preparation must preserve selected paths, order, fractions, and provenance across every mineral.");
                TestAssert.Equal(0, actual.Shortages.Count, "A complete request must remain complete after preparation.");
                TestAssert.Equal(0, actual.Recovery.ExaminedCount, "Complete fixtures require no Pending recovery.");
                TestAssert.True(log.Count > 0, "Preparation must report its measured work through the supplied logger.");
                TestAssert.SequenceEqual(before, fixture.Snapshot(), "A complete preparation must not rewrite spectra, manifests, or plans.");
            }
        }

        // 260907Codex: 復旧で保存済みPendingファイルを戻すことはできるが、選択ファイルの破損・欠損後に不完全なpoolを返さない。
        private static void PendingRecoveryAndSelectedIntegrityKeepTrainingAllOrNone()
        {
            using var fixture = new TestFixture(null, TestFixture.CreateSolution("SyntheticFirst"), TestFixture.CreateSolution("SyntheticSecond"));
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            foreach (var reservation in reservations.Where(row => row.SolutionName == "SyntheticFirst"))
                fixture.WriteValidSpectrum(Path.Combine(reservation.PoolFolder, reservation.FileName), reservation.SemEdxCondition);

            var incomplete = fixture.Workflow.PrepareTrainingPools(fixture.Request);
            TestAssert.Equal(16, incomplete.Recovery.ExaminedCount, "Recovery must inspect Pending rows across both minerals.");
            TestAssert.Equal(8, incomplete.Recovery.RecoveredCount, "Only fully written spectra may be recovered.");
            TestAssert.Equal(8, incomplete.Recovery.MissingFileCount, "Unsaved second-mineral reservations must remain missing.");
            TestAssert.Equal(0, incomplete.Pools.Count, "A complete first mineral cannot permit partial training.");
            TestAssert.Equal(8, incomplete.Shortages.Sum(item => item.MissingCount), "Only the unsaved mineral should require supplementation.");
            fixture.Complete(reservations.Where(row => row.SolutionName == "SyntheticSecond"));
            TestAssert.Equal(2, fixture.Workflow.PrepareTrainingPools(fixture.Request).Pools.Count, "Recovered and newly completed rows must train together.");

            string corruptPath = Path.Combine(reservations[0].PoolFolder, reservations[0].FileName);
            string missingPath = Path.Combine(reservations[1].PoolFolder, reservations[1].FileName);
            fixture.AssertOwnedPath(corruptPath);
            fixture.AssertOwnedPath(missingPath);
            File.WriteAllText(corruptPath, "#SPECTRUM:\nNaN\n#ENDOFDATA:\n");
            File.Delete(missingPath);
            var invalid = fixture.Workflow.PrepareTrainingPools(fixture.Request);
            TestAssert.Equal(0, invalid.Pools.Count, "Selected invalid or missing EMSA files must block every training pool.");
            TestAssert.Equal(2, invalid.Shortages.Sum(item => item.MissingCount), "Selected file integrity failures must remain exact row shortages.");
            foreach (var reservation in reservations.Take(2))
                TestAssert.Equal(SpectrumManifestStatus.Missing,
                    fixture.Repository.Load(reservation.ManifestPath)!.Spectra.Single(row => row.SimulationId == reservation.SimulationId).Status,
                    "Selected Completed files that fail validation must be persisted as Missing.");

            fixture.Complete(reservations.Take(2));
            var manifest = fixture.Repository.Load(reservations[0].ManifestPath)!;
            manifest.Spectra.Single(row => row.SimulationId == reservations[0].SimulationId).PlanRowHash = "damaged-plan-binding";
            fixture.Repository.Save(reservations[0].ManifestPath, manifest);
            TestAssert.Throws<InvalidOperationException>(() => fixture.Workflow.PrepareTrainingPools(fixture.Request), "Faster preparation must still reject damaged plan bindings.");
        }

        // 260907Codex: 読み取り専用の呼び出しでは元のmanifestを変更しない。
        // 260917Codex: 内容が完成して見えても、Pending入力は除外する。
        private static void ReadOnlyPreparationDoesNotPersistRecovery()
        {
            using var fixture = new TestFixture();
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            foreach (var reservation in reservations)
                fixture.WriteValidSpectrum(Path.Combine(reservation.PoolFolder, reservation.FileName), reservation.SemEdxCondition);
            var before = fixture.Snapshot();
            var workflow = new SpectrumPoolWorkflow(fixture.Repository, new SimulationPlanBuilder(), persistManifestRepairs: false);

            var result = workflow.PrepareTrainingPools(fixture.Request);
            // 260917Codex: 書き込み済みのPendingファイルでも、稼働中の生成処理が所有している可能性があるため学習に使わない。
            TestAssert.Equal(0, result.Recovery.RecoveredCount, "Read-only preparation must not recover pending spectra.");
            TestAssert.Equal(0, result.Pools.Count, "Pending-only input cannot satisfy training, even if its files parse correctly.");
            TestAssert.True(result.Shortages.Count > 0, "Unpublished input must report a shortage.");
            TestAssert.SequenceEqual(before, fixture.Snapshot(), "Read-only preparation must not persist recovered statuses or plan changes.");
            // 260917Codex: 生成側が完了を公開した後は、同じ読み取り専用ワークフローでそのデータを選択できる。
            fixture.Complete(reservations);
            before = fixture.Snapshot();
            result = workflow.PrepareTrainingPools(fixture.Request);
            TestAssert.Equal(1, result.Pools.Count, "Published Completed spectra must remain available to learners.");
            TestAssert.SequenceEqual(before, fixture.Snapshot(), "Reading published spectra must not modify pool metadata.");
        }

        // 260907Codex: 古い未選択スペクトルは保存データとして残し、このB計画の追加入力や必須処理にしない。
        private static void RetainedUnselectedSpectraAreNotValidatedForBTraining()
        {
            using var fixture = new TestFixture();
            var reservations = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _));
            fixture.Complete(reservations);
            var expected = fixture.Workflow.CreateTrainingPools(fixture.Request, out _);
            var reservation = reservations[0];
            var manifest = fixture.Repository.Load(reservation.ManifestPath)!;
            var retained = new SpectrumManifestEntry
            {
                SimulationId = manifest.NextSimulationId++,
                FileName = "retained-unselected-corrupt.emsa",
                Status = SpectrumManifestStatus.Completed,
                EndmemberFractions = new(manifest.Spectra[0].EndmemberFractions)
            };
            manifest.Spectra.Add(retained);
            fixture.Repository.Save(reservation.ManifestPath, manifest);
            string path = Path.Combine(reservation.PoolFolder, retained.FileName);
            fixture.AssertOwnedPath(path);
            File.WriteAllText(path, "#SPECTRUM:\n1\n");
            var before = fixture.Snapshot();

            var result = fixture.Workflow.PrepareTrainingPools(fixture.Request);
            TestAssert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(result.Pools), "Unselected spectra must not change an exact plan's selection.");
            TestAssert.Equal(0, result.Shortages.Count, "Unselected corruption must not make valid planned rows incomplete.");
            TestAssert.Equal(SpectrumManifestStatus.Completed, fixture.Repository.Load(reservation.ManifestPath)!.Spectra.Single(row => row.SimulationId == retained.SimulationId).Status,
                "Training preparation must not validate or demote unselected retained spectra in a single compatible B pool.");
            TestAssert.SequenceEqual(before, fixture.Snapshot(), "Unused old data must be retained without an unrelated repair write.");
        }

        // 260907Codex: 共有PCで不確かな実時間の速度を判定せず、実際のJSON読み取り回数を数える。
        private static void CachedManifestsAreScopedAndObserveFileChanges()
        {
            using var fixture = new TestFixture();
            fixture.Complete(TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _)));
            var solution = fixture.Request.SelectedMineralSolutions.Single();
            var baseline = new SpectrumPoolRepository(new SpectrumConditionKeyBuilder());
            var cached = fixture.Repository.CreatePreparationRepository();
            foreach (var repository in new[] { baseline, cached })
                for (int pass = 0; pass < 2; pass++)
                    foreach (var allocation in fixture.Request.GetEffectiveSpectrumTimeAllocations())
                    {
                        var handle = repository.ResolvePool(fixture.Request.Paths.SpectrumOutputFolder, solution,
                            fixture.Request.Simulation.ResolutionStep, fixture.Request.SemEdxCondition with { LiveTime = allocation.LiveTime });
                        TestAssert.True(repository.Load(handle.ManifestPath) is not null, "Every synthetic condition must resolve to its manifest.");
                    }

            TestAssert.Equal(5, cached.ManifestReadCount, "Repeated recovery/training catalog scans should read each unchanged manifest once.");
            TestAssert.True(cached.ManifestCacheHitCount > 0, "Repeated compatible-pool resolution must reuse cached JSON.");
            TestAssert.True(baseline.ManifestReadCount > cached.ManifestReadCount, "The synthetic benchmark must prove that duplicate JSON reads were removed.");
            Console.WriteLine($"Preparation manifest reads, two scans of five pools: {baseline.ManifestReadCount} -> {cached.ManifestReadCount}.");

            string manifestPath = fixture.Handle(solution, 1).ManifestPath;
            var external = fixture.Repository.Load(manifestPath)!;
            external.MineralName += "-external-change";
            fixture.Repository.Save(manifestPath, external);
            TestAssert.Equal(external.MineralName, cached.Load(manifestPath)!.MineralName, "A normal external file replacement must invalidate cached metadata.");
            TestAssert.Equal(6, cached.ManifestReadCount, "Changed file metadata must cause one new JSON read.");

            var changed = cached.Load(manifestPath)!;
            changed.NextSimulationId += 100;
            cached.Save(manifestPath, changed);
            TestAssert.Equal(changed.NextSimulationId, cached.Load(manifestPath)!.NextSimulationId, "Saving in the same scope must expose the saved content.");
            TestAssert.Equal(7, cached.ManifestReadCount, "Saving must invalidate even the same cached object.");

            var nextOperation = fixture.Repository.CreatePreparationRepository();
            var nextManifest = nextOperation.Load(manifestPath)!;
            TestAssert.Equal(1, nextOperation.ManifestReadCount, "A new preparation operation must read current storage independently.");
            TestAssert.True(!ReferenceEquals(nextManifest, cached.Load(manifestPath)), "Mutable manifest objects must not survive into a different preparation scope.");
            fixture.AssertOwnedPath(manifestPath);
            File.Delete(manifestPath);
            TestAssert.True(cached.Load(manifestPath) is null, "Deleted manifests must not survive through the preparation cache.");
            fixture.Repository.Save(manifestPath, nextManifest);
            TestAssert.Equal(nextManifest.NextSimulationId, cached.Load(manifestPath)!.NextSimulationId, "A manifest recreated after deletion must be read again.");
            TestAssert.Equal(8, cached.ManifestReadCount, "Recreated files must require a fresh JSON read.");
        }

        // 260907Codex: 互換フォルダーが重複して再利用候補になる場合、JSONキャッシュで実EMSA検証を代替しない。
        private static void DuplicateCompatiblePoolsRetainPhysicalFileRanking()
        {
            using var fixture = new TestFixture();
            var solution = fixture.Request.SelectedMineralSolutions.Single();
            var original = fixture.Handle(solution, 1);
            var handles = new[] { "aa-compatible", "bb-compatible" }.Select(name =>
            {
                string folder = Path.Combine(Path.GetDirectoryName(original.PoolFolder)!, name);
                return new SpectrumPoolHandle(folder, Path.Combine(folder, "manifest.json"), name, original.Condition);
            }).ToArray();
            for (int poolIndex = 0; poolIndex < handles.Length; poolIndex++)
            {
                var handle = handles[poolIndex];
                var manifest = fixture.Repository.LoadOrCreate(handle);
                for (int index = 0; index < 3; index++)
                {
                    string fileName = $"spectrum-{index}.emsa";
                    manifest.Spectra.Add(new SpectrumManifestEntry { SimulationId = index, FileName = fileName, Status = SpectrumManifestStatus.Completed });
                    if (index <= poolIndex)
                        fixture.WriteValidSpectrum(Path.Combine(handle.PoolFolder, fileName), handle.Condition.SemEdxCondition);
                }
                fixture.Repository.Save(handle, manifest);
            }

            var cached = fixture.Repository.CreatePreparationRepository();
            SpectrumPoolHandle Resolve(SpectrumPoolRepository repository) => repository.ResolvePool(fixture.Request.Paths.SpectrumOutputFolder,
                solution, fixture.Request.Simulation.ResolutionStep, fixture.Request.SemEdxCondition);
            TestAssert.Equal(handles[1].PoolFolder, Resolve(fixture.Repository).PoolFolder, "The fixture's second folder must win by valid-file count, not Completed flags.");
            TestAssert.Equal(handles[1].PoolFolder, Resolve(cached).PoolFolder, "Cached resolution must preserve the original physical-file winner.");
            string path = Path.Combine(handles[1].PoolFolder, "spectrum-1.emsa");
            fixture.AssertOwnedPath(path);
            File.WriteAllText(path, "#SPECTRUM:\n1\n");
            TestAssert.Equal(handles[0].PoolFolder, Resolve(cached).PoolFolder, "Changed physical spectra must be rechecked and ties must still use folder ordering.");
            TestAssert.Equal(2, cached.ManifestReadCount, "Rechecking duplicate spectra must not require rereading unchanged manifests.");
        }

        // 260907Codex: 同期的な進捗通知で、UIスレッドやタイマーの競合なしに中止テストの境界を確定する。
        private static void PreparationReportsProgressAndHonorsCancellation()
        {
            // 260907Codex: 2鉱物を使い、pool内の進捗と準備処理全体のオフセットを両方検証する。
            using var fixture = new TestFixture(null, TestFixture.CreateSolution("SyntheticFirst"), TestFixture.CreateSolution("SyntheticSecond"));
            fixture.Complete(TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _)));
            var reports = new List<SpectrumPoolPreparationProgress>();
            fixture.Workflow.PrepareTrainingPools(fixture.Request, new InlineProgress(reports.Add));
            TestAssert.True(reports.Any(item => item.FilesToCheck > 0 && item.FilesChecked > 0 && !item.PoolCompleted), "Preparation must report spectrum work before the final completed-pool event.");
            TestAssert.True(reports.All(item => item.FilesChecked >= 0 && item.FilesToCheck >= 0), "Preparation progress counters must remain nonnegative.");

            // 260907Codex: 共通フィールドの重複を除去しても、開始・最終ファイル・完了の通知を正確に保持する。
            var allocations = fixture.Request.GetEffectiveSpectrumTimeAllocations();
            int totalPools = fixture.Request.SelectedMineralSolutions.Count * allocations.Count;
            int poolNumber = 0;
            foreach (var solution in fixture.Request.SelectedMineralSolutions)
                foreach (var allocation in allocations)
                {
                    poolNumber++;
                    int count = allocation.TargetSpectrumCount;
                    var start = new SpectrumPoolPreparationProgress("採用EMSA確認", poolNumber, totalPools, poolNumber - 1,
                        solution.Name, allocation.LiveTime, 0, count, false);
                    TestAssert.SequenceEqual(
                        [start, start with { FilesChecked = count },
                            start with { FilesChecked = count, CompletedPoolCount = poolNumber, PoolCompleted = true }],
                        reports.Where(item => item.Phase == "採用EMSA確認" && item.PoolNumber == poolNumber),
                        "Progress identity, counts, order, and completion flags must not change during simplification.");
                }

            var before = fixture.Snapshot();
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            TestAssert.Throws<OperationCanceledException>(() => fixture.Workflow.PrepareTrainingPools(fixture.Request, cancellationToken: canceled.Token), "Pre-canceled preparation must not start pool work.");
            TestAssert.Throws<OperationCanceledException>(() => fixture.Repository.CreatePreparationRepository(canceled.Token).Load("unused-path"), "A preparation repository must observe cancellation before reading a path.");

            using var during = new CancellationTokenSource();
            TestAssert.Throws<OperationCanceledException>(() => fixture.Workflow.PrepareTrainingPools(fixture.Request,
                new InlineProgress(item =>
                {
                    if (item.FilesToCheck > 0 && item.FilesChecked > 0)
                        during.Cancel();
                }), during.Token), "Cancellation requested while checking selected spectra must interrupt preparation.");
            TestAssert.SequenceEqual(before, fixture.Snapshot(), "Canceled preparation of an intact fixture must not change files or reservations.");
            TestAssert.Equal(2, fixture.Workflow.PrepareTrainingPools(fixture.Request).Pools.Count, "A canceled cache scope must not poison the next preparation attempt.");
        }

        // 260907Codex: 全候補を拒否する鉱物でも、採用組成が返るまで中止を待たせない。
        private static void RejectedCompositionCandidatesRemainCancelable()
        {
            var solution = TestFixture.CreateSolution();
            solution.Constraints = ["Forsterite < 0"];
            TestAssert.True(!solution.CapableComposition([0.5, 0.5]), "The cancellation fixture must reject its composition candidates.");
            using var canceled = new CancellationTokenSource();
            int examined = 0;
            TestAssert.Throws<OperationCanceledException>(() => solution.EnumerateCandidateFractionsLazy(0.001, canceled.Token, count =>
            {
                examined = count;
                if (count >= 128)
                    canceled.Cancel();
            }).ToArray(), "Raw rejected candidates must expose progress and observe cancellation.");
            TestAssert.Equal(128, examined, "Cancellation should stop at the requested raw-candidate progress boundary.");

            using var plannerCanceled = new CancellationTokenSource();
            TestAssert.Throws<OperationCanceledException>(() => new SpectrumCompositionTimePlanner().Create(solution, 0.001,
                [new SpectrumTimeAllocation(1, 8)], new Dictionary<double, string> { [1] = "synthetic-condition" },
                cancellationToken: plannerCanceled.Token, onCandidateExamined: count =>
                {
                    if (count >= 128)
                        plannerCanceled.Cancel();
                }), "Planner candidate probing must pass cancellation through constraint rejections.");
        }

        // 260907Codex: テストのコールバックは同期実行し、中止とファイルスナップショットの順序を確定する。
        private sealed class InlineProgress(Action<SpectrumPoolPreparationProgress> report) : IProgress<SpectrumPoolPreparationProgress>
        {
            public void Report(SpectrumPoolPreparationProgress value) => report(value);
        }
    }
}
