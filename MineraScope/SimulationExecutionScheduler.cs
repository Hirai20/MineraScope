// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace MineraScope
{
    // 261103Codex: プロセス実行の所有権はserviceに残し、投入順・全体上限・バッチ保存だけを調停する。
    internal static class SimulationExecutionScheduler
    {
        // 261104Codex: 全workerの終了と開始済みバッチの保存を待ってから、結果または保存失敗を返す。
        public static async Task<IReadOnlyList<SimulationExecutionResult>> RunAsync(
            SimulationExecutionPlan plan,
            SimulationProgressScope progress,
            Func<SimulationExecutionJob, string, int, CancellationToken, Task<SimulationExecutionResult>> executeJob,
            SimulationBatchCompletedCallback batchCompleted,
            CancellationToken cancellationToken)
        {
            var states = plan.Batches.Select((batch, index) => new BatchState(batch, index + 1)).ToArray();
            var jobs = states.SelectMany(state => state.Batch.Jobs.Select((job, index) => (State: state, Job: job, Index: index))).ToArray();
            var ready = Channel.CreateUnbounded<BatchState>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var gate = new object();
            int nextJob = 0;
            ExceptionDispatchInfo? checkpointFailure = null;

            // 261103Codex: 完了したバッチだけを1本のconsumerへ渡す。保存I/Oで空いた実行枠を塞がない。
            async Task SaveBatchesAsync()
            {
                try
                {
                    await foreach (var state in ready.Reader.ReadAllAsync().ConfigureAwait(false))
                    {
                        await batchCompleted(state.Batch, state.Results!).ConfigureAwait(false);
                        ReportBatch(state, SimulationExecutionProgressKind.BatchCompleted, "完了");
                    }
                }
                catch (Exception exception)
                {
                    checkpointFailure = ExceptionDispatchInfo.Capture(exception);
                    stop.Cancel();
                }
            }

            // 261103Codex: queueからの取り出しだけを直列化し、最大ParallelCount個のworkerを使い回す。
            async Task WorkAsync()
            {
                while (true)
                {
                    (BatchState State, SimulationExecutionJob Job, int Index) work;
                    lock (gate)
                    {
                        if (stop.IsCancellationRequested || nextJob >= jobs.Length)
                            return;
                        work = jobs[nextJob++];
                        if (!work.State.Started)
                        {
                            work.State.Started = true;
                            ReportBatch(work.State, SimulationExecutionProgressKind.BatchStarted, "開始");
                        }
                    }

                    SimulationExecutionResult result;
                    try
                    {
                        result = await executeJob(work.Job, work.State.Batch.SolutionName, work.State.Number, stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested)
                    {
                        // 261104Codex: 実行器から返る中止と未投入分の中止を同じ結果・進捗処理で記録する。
                        result = RecordCanceledJob(work.State, work.Index);
                    }
                    catch
                    {
                        // 261103Codex: 実行器の予期しない例外でも他workerを止め、全員の回収後に呼び出し元へ返す。
                        stop.Cancel();
                        throw;
                    }

                    lock (gate)
                    {
                        work.State.Results[work.Index] = result;
                        if (--work.State.Remaining == 0)
                            ready.Writer.TryWrite(work.State);
                    }
                }
            }

            // 261103Codex: GUIのSynchronizationContextを使わず、実行と保存の両方を必ず最後まで回収する。
            Task saving = Task.Run(SaveBatchesAsync);
            Task[] workers = Enumerable.Range(0, Math.Min(plan.ParallelCount, jobs.Length))
                .Select(_ => Task.Run(WorkAsync)).ToArray();
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            finally
            {
                // 261103Codex: 中止した開始済みバッチは未投入分も中止結果としてそろえる。未開始バッチは触らない。
                foreach (var state in states.Where(state => state.Started && state.Remaining > 0))
                {
                    for (int index = 0; index < state.Results.Length; index++)
                        if (state.Results[index] is null)
                            // 261104Codex: 未投入分も共通の中止処理で記録する。
                            state.Results[index] = RecordCanceledJob(state, index);
                    ready.Writer.TryWrite(state);
                }
                ready.Writer.TryComplete();
                await saving.ConfigureAwait(false);
            }

            checkpointFailure?.Throw();
            // 261103Codex: 実行・保存の完了順に依存せず、従来どおり計画順で結果を返す。
            return states.Where(state => state.Started).SelectMany(state => state.Results).ToArray()!;

            // 261103Codex: ファイルやscriptには触れず、中止の件数とPending復帰用の予約だけを確定する。
            // 261104Codex: 呼び出し元は未投入jobに限らないため、記録する動作を名前にする。
            SimulationExecutionResult RecordCanceledJob(BatchState state, int index)
            {
                var job = state.Batch.Jobs[index];
                var result = SimulationExecutionResult.Canceled(job.Reservations);
                new SimulationJobProgress(progress, job, state.Batch.SolutionName, state.Number).ReportJobFinished(result, TimeSpan.Zero);
                return result;
            }

            // 261103Codex: バッチ番号は計画中の識別子であり、現在の同時稼働数や完了数としては扱わない。
            void ReportBatch(BatchState state, SimulationExecutionProgressKind kind, string status)
            {
                int spectra = state.Batch.Jobs.Sum(job => job.Reservations.Count);
                progress.Report(kind, state.Batch.SolutionName, state.Number, progress.NextJobIndexPreview, spectra,
                    $"{state.Batch.SolutionName}: batch {state.Number}/{progress.BatchCount} {status}, spectra {spectra}");
            }
        }

        // 261103Codex: ジョブごとの結果位置を固定し、保存時に抜け・重複や順序変化を起こさない。
        private sealed class BatchState(SimulationExecutionBatch batch, int number)
        {
            public SimulationExecutionBatch Batch { get; } = batch;
            public int Number { get; } = number;
            public SimulationExecutionResult?[] Results { get; } = new SimulationExecutionResult?[batch.Jobs.Count];
            public int Remaining { get; set; } = batch.Jobs.Count;
            public bool Started { get; set; }
        }
    }
}
