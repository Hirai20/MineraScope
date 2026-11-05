// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MineraScope.Tests
{
    // 261103Codex: 実Javaや実時間の速度に依存せず、ジョブ投入・保存・中止の因果関係を検証する。
    internal static class SimulationExecutionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        // 261104Codex: 通常テストとして実Javaを使わない検証だけを実行する。
        public static void Run()
        {
            ExistingPartitionsAndConditionsArePreserved();
            BenchmarkReplaysTheSameReservations();
            CrossBatchExecutionAndSerializedCheckpointsAsync().GetAwaiter().GetResult();
            CancellationPublishesPartialStartedBatchesAsync().GetAwaiter().GetResult();
            CheckpointFailureDrainsWorkersAsync().GetAwaiter().GetResult();
            FailedJobsDoNotBlockLaterBatchesAsync().GetAwaiter().GetResult();
            EmptyAndPreCanceledPlansAsync().GetAwaiter().GetResult();
            SerialExecutionAsync().GetAwaiter().GetResult();
        }

        // 261103Codex: 旧来の20×50本、少数の不足、複数条件の配分を固定し、条件と予約が変わらないことを確認する。
        private static void ExistingPartitionsAndConditionsArePreserved()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(1000, [new(1, 1)]));
            var request = WithParallel(fixture.Request, 20);
            var builder = new SimulationPlanBuilder();
            foreach (int count in new[] { 0, 1, 17, 1000 })
            {
                var reservations = Enumerable.Range(0, count).Select(index => new SpectrumSimulationReservation(
                    "One", Path.Combine(fixture.Root, "pool"), Path.Combine(fixture.Root, "manifest.json"), index, $"s{index}.emsa",
                    [("Si", 0.3), ("O", 0.7)], "condition", request.SemEdxCondition, 1000)).ToArray();
                var plan = builder.CreatePlan(request, reservations);
                TestAssert.Equal(20, plan.ParallelCount, "Even an empty plan must retain its execution limit.");
                var jobs = plan.Batches.SelectMany(batch => batch.Jobs).ToArray();
                int expectedJobs = count == 1000 ? 20 : count == 0 ? 0 : 1;
                TestAssert.Equal(expectedJobs, jobs.Length, "Existing partial-refill job sizes must not change.");
                TestAssert.Equal(count, jobs.Sum(job => job.Reservations.Count), "No reservation may be lost or duplicated.");
                foreach (var (job, index) in jobs.Select((job, index) => (job, index)))
                {
                    TestAssert.SequenceEqual(reservations.Where((_, row) => row % expectedJobs == index), job.Reservations,
                        "The existing round-robin partition must be unchanged.");
                    TestAssert.Equal(request.SemEdxCondition.CarbonCoatThickness, job.Property.CarbonCoatThickness, "Coating must be unchanged.");
                    TestAssert.Equal(request.SemEdxCondition.BeamEnergy, job.Property.BeamEnergy, "Beam energy must be unchanged.");
                    TestAssert.Equal(request.SemEdxCondition.ProbeCurrent, job.Property.ProbeCurrent, "Probe current must be unchanged.");
                    TestAssert.Equal(request.SemEdxCondition.LiveTime, job.Property.LiveTime, "Live time must be unchanged.");
                    TestAssert.SequenceEqual(job.Reservations.Select(row => row.FileName), job.Property.OutputFiles, "Output names must match their reservations.");
                }
                if (count == 1000)
                    TestAssert.True(jobs.All(job => job.Reservations.Count == 50), "1000 spectra at parallel=20 must still use twenty fifty-spectrum jobs.");
            }

            using var mixed = new TestFixture(new SpectrumTimeSchedule(1000, [new(1, 1), new(5, 1), new(10, 1), new(20, 1), new(50, 1)]));
            var mixedRequest = WithParallel(mixed.Request, 20);
            var before = mixed.Snapshot();
            var preview = mixed.Workflow.PreviewMissingSimulationPlan(mixedRequest, out _);
            TestAssert.SequenceEqual(before, mixed.Snapshot(), "The job preview must stay read-only.");
            var mixedPlan = mixed.Workflow.CreateMissingSimulationPlan(mixedRequest, out _);
            var mixedJobs = mixedPlan.Batches.SelectMany(batch => batch.Jobs).ToArray();
            TestAssert.Equal(20, mixedJobs.Length, "Five conditions must still share twenty existing jobs.");
            TestAssert.Equal(preview.JobCount, mixedJobs.Length, "Preview and execution job counts must agree.");
            TestAssert.True(mixedJobs.All(job => job.Reservations.Count == 50 && job.Reservations.All(row =>
                row.ConditionKey == job.Reservations[0].ConditionKey && row.PoolFolder == job.Property.OutputFolder
                && row.SemEdxCondition.LiveTime == job.Property.LiveTime)), "Each script must retain one physical condition and its original pool.");
            TestAssert.Equal(mixedJobs.Length, mixedJobs.Select(job => job.ScriptPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                "Concurrent jobs must never overwrite another script.");
        }

        // 261104Codex: 比較用poolの再予約が組成・ID・順序を変えず、基準のファイルも変更しないことを確認する。
        private static void BenchmarkReplaysTheSameReservations()
        {
            var schedule = new SpectrumTimeSchedule(6, [new(1, 1)]);
            using var source = new TestFixture(schedule, TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"));
            using var target = new TestFixture(schedule, TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"));
            var sourcePlan = source.Workflow.CreateMissingSimulationPlan(source.Request, out _);
            source.Complete(TestFixture.Reservations(sourcePlan).Where((_, index) => index % 2 == 0));
            var before = source.Snapshot();
            SimulationExecutionBenchmark.CopyReferenceReservations(source.Request.Paths.SpectrumOutputFolder,
                target.Request.Paths.SpectrumOutputFolder, target.Repository, 2);
            var targetPlan = target.Workflow.CreateMissingSimulationPlan(target.Request, out _);
            TestAssert.Equal(SimulationExecutionBenchmark.CreatePlanSnapshot(source.Request, sourcePlan),
                SimulationExecutionBenchmark.CreatePlanSnapshot(target.Request, targetPlan),
                "Benchmark replay must use exactly the same composition weights, IDs, output names, conditions, and job partitions.");
            TestAssert.SequenceEqual(before, source.Snapshot(), "Replaying a benchmark must not alter its reference pool.");
            TestAssert.True(!Directory.GetFiles(target.Request.Paths.SpectrumOutputFolder, "*.emsa", SearchOption.AllDirectories).Any(),
                "Benchmark replay must copy reservations only, never completed spectra.");
        }

        // 261103Codex: 遅いAを残してB・Cを投入し、Bの保存待ち中も実行しつつ保存は1本ずつ行う。
        private static async Task CrossBatchExecutionAndSerializedCheckpointsAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(6, [new(1, 1)]),
                TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"), TestFixture.CreateSolution("C"));
            var plan = fixture.Workflow.CreateMissingSimulationPlan(WithParallel(fixture.Request, 2), out _);
            var events = new ConcurrentQueue<SimulationExecutionProgress>();
            var progress = new SimulationProgressScope(plan, new InlineProgress<SimulationExecutionProgress>(events.Enqueue));
            var executor = new ControlledExecutor(plan, progress);
            using var cancellation = new CancellationTokenSource(Timeout);
            var saved = new ConcurrentDictionary<string, int>();
            var checkpoints = Channel.CreateUnbounded<string>();
            var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int activeSaves = 0;
            int maxSaves = 0;
            var run = SimulationExecutionScheduler.RunAsync(plan, progress, executor.ExecuteAsync,
                async (batch, results) =>
                {
                    int active = Interlocked.Increment(ref activeSaves);
                    maxSaves = Math.Max(maxSaves, active);
                    TestAssert.Equal(batch.Jobs.Count, results.Count, "Checkpoint must contain every job in its batch.");
                    TestAssert.True(results.All(result => !result.IsCanceled && result.ExitCode == 0), "A normal checkpoint must contain finished jobs only.");
                    checkpoints.Writer.TryWrite(batch.SolutionName);
                    if (batch.SolutionName == "B")
                        await releaseSave.Task.WaitAsync(Timeout);
                    TestAssert.True(saved.TryAdd(batch.SolutionName, results.Count), "Each batch must be checkpointed exactly once.");
                    Interlocked.Decrement(ref activeSaves);
                }, cancellation.Token);
            try
            {
                var a1 = await NextAsync(executor.Started);
                var a2 = await NextAsync(executor.Started);
                TestAssert.True(a1.Reservations[0].SolutionName == "A" && a2.Reservations[0].SolutionName == "A", "FIFO admission must start with batch A.");
                executor.Complete(a2);
                var b1 = await NextAsync(executor.Started);
                TestAssert.Equal("B", b1.Reservations[0].SolutionName, "A free slot must start B before the slow A job finishes.");
                TestAssert.True(checkpoints.Reader.Count == 0, "A must not be saved while one of its jobs is running.");
                executor.Complete(b1);
                var b2 = await NextAsync(executor.Started);
                executor.Complete(b2);
                var c1 = await NextAsync(executor.Started);
                TestAssert.Equal("C", c1.Reservations[0].SolutionName, "Admission must continue across another batch boundary.");
                TestAssert.Equal("B", await NextAsync(checkpoints), "B must be checkpointed without waiting for A.");
                TestAssert.True(!events.Any(item => item.Kind == SimulationExecutionProgressKind.BatchCompleted), "BatchCompleted must wait for the blocked save.");
                executor.Complete(a1);
                var c2 = await NextAsync(executor.Started);
                TestAssert.Equal("C", c2.Reservations[0].SolutionName, "Blocked checkpoint I/O must not hold an execution slot.");
                executor.Complete(c1);
                executor.Complete(c2);
                TestAssert.True(!run.IsCompleted, "Run completion must await pending checkpoints.");
                releaseSave.TrySetResult();
                var results = await run.WaitAsync(Timeout);
                TestAssert.SequenceEqual(TestFixture.Reservations(plan), results.SelectMany(result => result.Reservations), "Returned results must retain plan order.");
                TestAssert.Equal(2, executor.MaxActive, "Global execution must reach but never exceed its limit.");
                TestAssert.Equal(1, maxSaves, "Checkpoint callbacks must never run concurrently.");
                TestAssert.Equal(3, saved.Count, "All batches must be checkpointed once.");
                var notifications = events.ToArray();
                TestAssert.Equal(3, notifications.Count(item => item.Kind == SimulationExecutionProgressKind.BatchStarted), "Each batch must start once.");
                TestAssert.Equal(3, notifications.Count(item => item.Kind == SimulationExecutionProgressKind.BatchCompleted), "Each saved batch must complete once.");
                TestAssert.True(notifications.Zip(notifications.Skip(1)).All(pair =>
                    pair.First.CompletedJobCount <= pair.Second.CompletedJobCount && pair.First.CompletedSpectrumCount <= pair.Second.CompletedSpectrumCount),
                    "Progress counters must never go backwards across batches.");
            }
            finally
            {
                // 261104Codex: テスト失敗時も同じ順序で中止・保存待ち解除・worker回収を行う。
                await StopAndDrainAsync(run, cancellation, executor, releaseSave);
            }
        }

        // 261103Codex: A・Bにまたがる中止で最終回収を待ち、部分保存はCompleted、未投入分はPendingに残す。
        private static async Task CancellationPublishesPartialStartedBatchesAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(4, [new(1, 1)]),
                TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"), TestFixture.CreateSolution("C"));
            var request = WithParallel(fixture.Request, 2);
            var plan = fixture.Workflow.CreateMissingSimulationPlan(request, out _);
            var progress = new SimulationProgressScope(plan, null);
            var executor = new ControlledExecutor(plan, progress) { HoldCancellationCleanup = true };
            var saved = new ConcurrentQueue<string>();
            using var cancellation = new CancellationTokenSource(Timeout);
            var run = SimulationExecutionScheduler.RunAsync(plan, progress, executor.ExecuteAsync, (batch, results) =>
            {
                TestAssert.Equal(batch.Jobs.Count, results.Count, "Cancellation must still checkpoint complete batch result sets.");
                fixture.Workflow.ApplySimulationResults(results);
                saved.Enqueue(batch.SolutionName);
                return Task.CompletedTask;
            }, cancellation.Token);
            try
            {
                var a1 = await NextAsync(executor.Started);
                var a2 = await NextAsync(executor.Started);
                foreach (var row in a1.Reservations)
                    fixture.WriteValidSpectrum(Path.Combine(row.PoolFolder, row.FileName), row.SemEdxCondition);
                executor.Complete(a1);
                var b1 = await NextAsync(executor.Started);
                var partial = a2.Reservations[0];
                fixture.WriteValidSpectrum(Path.Combine(partial.PoolFolder, partial.FileName), partial.SemEdxCondition);
                executor.SavedOnCancellation[a2.ScriptPath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { partial.FileName };
                cancellation.Cancel();
                await NextAsync(executor.Canceled);
                await NextAsync(executor.Canceled);
                TestAssert.True(!run.IsCompleted && saved.IsEmpty, "Cancellation must await process cleanup and final output collection before saving.");
                TestAssert.Equal(3, executor.StartCount, "No queued job may be launched after cancellation.");
                executor.CleanupRelease.TrySetResult();
                var results = await run.WaitAsync(Timeout);
                TestAssert.Equal(4, results.Count, "Only the two started batches must receive results, including their unstarted jobs.");
                TestAssert.True(results.Count(result => result.IsCanceled) == 3, "Running and queued work must receive cancellation results.");
                TestAssert.SequenceEqual(new[] { "A", "B" }, saved.Order(), "Only started batches must be checkpointed once.");
                var counts = fixture.Workflow.GetStatusCounts(request, validateCompletedSpectra: false);
                TestAssert.Equal(3, counts.Completed, "Two successful outputs plus the final partial output must be Completed.");
                TestAssert.Equal(9, counts.Pending, "All unsaved and never-started reservations must remain Pending.");
                TestAssert.Equal(0, counts.Failed, "User cancellation must not mark unsaved work Failed.");
                var remaining = TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(request, out _));
                TestAssert.Equal(9, remaining.Length, "Retry must reserve only the unsaved outputs.");
                var original = TestFixture.Reservations(plan).ToDictionary(row => (row.ManifestPath, row.SimulationId));
                TestAssert.True(remaining.All(row => original.TryGetValue((row.ManifestPath, row.SimulationId), out var prior)
                    && prior.FileName == row.FileName && prior.ConditionKey == row.ConditionKey
                    && prior.CompositionWeights.SequenceEqual(row.CompositionWeights)), "Retry must preserve physical reservation identity and composition.");
            }
            finally
            {
                // 261104Codex: 他の非同期テストと共通の終了境界を使う。
                await StopAndDrainAsync(run, cancellation, executor);
            }
        }

        // 261103Codex: 保存失敗は新規投入を止め、他プロセスの停止が済むまで例外を返さない。
        private static async Task CheckpointFailureDrainsWorkersAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(4, [new(1, 1)]),
                TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"), TestFixture.CreateSolution("C"));
            var plan = fixture.Workflow.CreateMissingSimulationPlan(WithParallel(fixture.Request, 2), out _);
            var progress = new SimulationProgressScope(plan, null);
            var executor = new ControlledExecutor(plan, progress) { HoldCancellationCleanup = true };
            using var cancellation = new CancellationTokenSource(Timeout);
            int saves = 0;
            var run = SimulationExecutionScheduler.RunAsync(plan, progress, executor.ExecuteAsync, (_, _) =>
            {
                Interlocked.Increment(ref saves);
                throw new IOException("checkpoint denied");
            }, cancellation.Token);
            try
            {
                var a1 = await NextAsync(executor.Started);
                var a2 = await NextAsync(executor.Started);
                executor.Complete(a1);
                await NextAsync(executor.Started);
                executor.Complete(a2);
                await NextAsync(executor.Canceled);
                TestAssert.True(!run.IsCompleted, "A checkpoint error must wait for running job cleanup.");
                executor.CleanupRelease.TrySetResult();
                try
                {
                    await run.WaitAsync(Timeout);
                    throw new InvalidOperationException("Expected checkpoint failure.");
                }
                catch (IOException exception)
                {
                    TestAssert.Equal("checkpoint denied", exception.Message, "The original checkpoint failure must be preserved.");
                }
                TestAssert.Equal(0, executor.Active, "Every worker must be drained before a checkpoint error is returned.");
                TestAssert.Equal(1, saves, "No further callbacks may publish results after a checkpoint failure.");
                TestAssert.True(executor.StartedJobs.All(job => job.Reservations[0].SolutionName != "C"), "Unreached batches must not be started after failure.");
            }
            finally
            {
                // 261104Codex: 保存失敗のテストでも同じ手順でworkerを回収する。
                await StopAndDrainAsync(run, cancellation, executor);
            }
        }

        // 261104Codex: 中止と待機解除の順序を共通化し、終了待ちの例外で元の検証失敗を上書きしない。
        private static async Task StopAndDrainAsync(Task run, CancellationTokenSource cancellation, ControlledExecutor executor,
            TaskCompletionSource? checkpointRelease = null)
        {
            cancellation.Cancel();
            checkpointRelease?.TrySetResult();
            executor.CleanupRelease.TrySetResult();
            try { await run.WaitAsync(Timeout); } catch { }
        }

        // 261104Codex: job単位の失敗は保存失敗と区別し、ほかの鉱物の実行・保存を続ける。
        private static async Task FailedJobsDoNotBlockLaterBatchesAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(4, [new(1, 1)]), TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"));
            var plan = fixture.Workflow.CreateMissingSimulationPlan(WithParallel(fixture.Request, 2), out _);
            var progress = new SimulationProgressScope(plan, null);
            var saved = new ConcurrentQueue<string>();
            int jobs = 0;
            var results = await SimulationExecutionScheduler.RunAsync(plan, progress, (job, solution, batch, _) =>
            {
                Interlocked.Increment(ref jobs);
                var result = new SimulationExecutionResult(job.Reservations, job == plan.Batches[0].Jobs[0] ? 1 : 0,
                    string.Empty, string.Empty, null);
                new SimulationJobProgress(progress, job, solution, batch).ReportJobFinished(result, TimeSpan.Zero);
                return Task.FromResult(result);
            }, (batch, _) => { saved.Enqueue(batch.SolutionName); return Task.CompletedTask; }, CancellationToken.None).WaitAsync(Timeout);
            TestAssert.Equal(4, jobs, "A process failure must not stop admission of later jobs.");
            TestAssert.Equal(1, results.Count(result => result.ExitCode != 0), "The failed result must be preserved.");
            TestAssert.SequenceEqual(new[] { "A", "B" }, saved.Order(), "Both batches must be checkpointed despite one failed job.");
        }

        // 261103Codex: 空計画と開始前の中止には、生成・保存・進捗の副作用を作らない。
        private static async Task EmptyAndPreCanceledPlansAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(4, [new(1, 1)]));
            var plan = fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            foreach (var (candidate, token) in new[] { (new SimulationExecutionPlan([], 20), CancellationToken.None), (plan, cancellation.Token) })
            {
                var events = new ConcurrentQueue<SimulationExecutionProgress>();
                var results = await SimulationExecutionScheduler.RunAsync(candidate, new SimulationProgressScope(candidate, new InlineProgress<SimulationExecutionProgress>(events.Enqueue)),
                    (_, _, _, _) => throw new InvalidOperationException("No job should execute."),
                    (_, _) => throw new InvalidOperationException("No checkpoint should execute."), token).WaitAsync(Timeout);
                TestAssert.Equal(0, results.Count, "An empty or pre-canceled plan must return no results.");
                TestAssert.True(events.IsEmpty, "An unstarted plan must not report batch progress.");
            }
        }

        // 261103Codex: 並列数1でも全バッチが完了し、同時実行上限を守る。
        private static async Task SerialExecutionAsync()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(3, [new(1, 1)]), TestFixture.CreateSolution("A"), TestFixture.CreateSolution("B"));
            var plan = fixture.Workflow.CreateMissingSimulationPlan(WithParallel(fixture.Request, 1), out _);
            var progress = new SimulationProgressScope(plan, null);
            int completed = 0;
            var results = await SimulationExecutionScheduler.RunAsync(plan, progress, (job, _, _, _) =>
            {
                completed++;
                return Task.FromResult(new SimulationExecutionResult(job.Reservations, 0, string.Empty, string.Empty, null));
            }, (_, _) => Task.CompletedTask, CancellationToken.None).WaitAsync(Timeout);
            TestAssert.Equal(1, plan.ParallelCount, "Serial requests must retain the exact global limit.");
            TestAssert.Equal(2, completed, "Both serial batches must execute exactly once.");
            TestAssert.Equal(6, results.Sum(result => result.Reservations.Count), "Serial execution must preserve every spectrum.");
        }

        // 261105Claude: 合図を待つ式と並列数の上書きの繰り返しを短くする。
        private static Task<T> NextAsync<T>(Channel<T> channel) => channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        private static ModelCreationRequest WithParallel(ModelCreationRequest request, int parallelCount) =>
            request with { Simulation = request.Simulation with { ParallelCount = parallelCount } };

        // 261103Codex: job終了とキャンセル後の回収を独立した合図で制御する実行器。
        private sealed class ControlledExecutor
        {
            private readonly Dictionary<string, TaskCompletionSource<SimulationExecutionResult>> _results;
            private readonly SimulationProgressScope _progress;
            private int _active;
            private int _maxActive;
            private readonly object _gate = new();
            public Channel<SimulationExecutionJob> Started { get; } = Channel.CreateUnbounded<SimulationExecutionJob>();
            public Channel<SimulationExecutionJob> Canceled { get; } = Channel.CreateUnbounded<SimulationExecutionJob>();
            public ConcurrentQueue<SimulationExecutionJob> StartedJobs { get; } = new();
            public ConcurrentDictionary<string, IReadOnlySet<string>> SavedOnCancellation { get; } = new();
            public TaskCompletionSource CleanupRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool HoldCancellationCleanup { get; init; }
            public int StartCount => StartedJobs.Count;
            public int Active => Volatile.Read(ref _active);
            public int MaxActive => _maxActive;

            // 261104Codex: 各jobに独立した終了合図を用意する。
            public ControlledExecutor(SimulationExecutionPlan plan, SimulationProgressScope progress)
            {
                _progress = progress;
                _results = plan.Batches.SelectMany(batch => batch.Jobs).ToDictionary(job => job.ScriptPath,
                    _ => new TaskCompletionSource<SimulationExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            // 261104Codex: 指定したjobだけを正常終了させる。
            public void Complete(SimulationExecutionJob job) => _results[job.ScriptPath].SetResult(
                new SimulationExecutionResult(job.Reservations, 0, string.Empty, string.Empty, null));

            // 261104Codex: 開始・終了・中止後の回収を独立して制御し、実行中の上限も記録する。
            public async Task<SimulationExecutionResult> ExecuteAsync(SimulationExecutionJob job, string solution, int batch, CancellationToken token)
            {
                var progress = new SimulationJobProgress(_progress, job, solution, batch);
                progress.ReportJobProgress(SimulationExecutionProgressKind.JobStarted, "test start");
                int active = Interlocked.Increment(ref _active);
                lock (_gate)
                    _maxActive = Math.Max(_maxActive, active);
                StartedJobs.Enqueue(job);
                Started.Writer.TryWrite(job);
                SimulationExecutionResult result;
                try
                {
                    result = await _results[job.ScriptPath].Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Canceled.Writer.TryWrite(job);
                    if (HoldCancellationCleanup)
                        await CleanupRelease.Task.WaitAsync(Timeout);
                    SavedOnCancellation.TryGetValue(job.ScriptPath, out var saved);
                    result = SimulationExecutionResult.Canceled(job.Reservations) with { SavedSpectrumFiles = saved };
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
                if (result.SavedSpectrumFiles is not null)
                    foreach (string file in result.SavedSpectrumFiles)
                        progress.ReportSpectrumSaved(file);
                progress.CompleteUnreportedSuccessfulSpectra(result);
                progress.ReportJobFinished(result, TimeSpan.Zero);
                return result;
            }
        }
    }
}
