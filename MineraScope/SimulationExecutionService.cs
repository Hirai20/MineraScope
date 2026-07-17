using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260507Codex: DTSA-II スクリプト生成と外部実行を担当し、結果は manifest 更新側へ返します。
    // 260717Claude: attempt 単位のプロセス実行は DtsaSimulationProcessRunner へ分離。この service は batch/job の
    //   スケジューリング、ジョブ単位の前処理 (出力フォルダ作成・予約ファイル削除)、attempt 結果の
    //   SimulationExecutionResult への変換に責務を絞る。
    internal sealed class SimulationExecutionService
    {
        private readonly DtsaSimulationProcessRunner _processRunner;

        public SimulationExecutionService(SimulationScriptGenerator scriptGenerator)
        {
            // 260717Claude: 呼び出し側の互換のため ctor は generator のまま受け、attempt 実行側へ渡す。
            _processRunner = new DtsaSimulationProcessRunner(scriptGenerator);
        }

        // 260513Codex: batch 内は並列実行し、キャンセル時は後続 batch を起動せず実行済み結果だけを返します。
        public async Task<IReadOnlyList<SimulationExecutionResult>> RunAsync(
            SimulationExecutionPlan plan,
            IProgress<SimulationExecutionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var progressScope = new SimulationProgressScope(plan, progress);
            var results = new List<SimulationExecutionResult>();
            // 260717Claude: run 単位の永続ログ。GUI/headless どちらでも RunAsync 起点で必ず 1 ファイル残し、
            //   実行後に失敗原因 (watchdog kill・stderr) を追跡できるようにする。
            var runLog = SimulationRunLog.CreateForRun();
            var runStopwatch = Stopwatch.StartNew();
            runLog.WriteLine($"run start: batches={progressScope.BatchCount}, jobs={progressScope.TotalJobCount}, spectra={progressScope.TotalSpectrumCount}");

            for (int batchIndex = 0; batchIndex < plan.Batches.Count; batchIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var batch = plan.Batches[batchIndex];
                int batchNumber = batchIndex + 1;
                ReportBatchProgress(progressScope, batch, batchNumber, SimulationExecutionProgressKind.BatchStarted, "開始");

                var batchResults = await Task.WhenAll(batch.Jobs.Select(job =>
                    ExecuteJobAsync(job, batch.SolutionName, batchNumber, progressScope, runLog, cancellationToken)));
                results.AddRange(batchResults);

                ReportBatchProgress(progressScope, batch, batchNumber, SimulationExecutionProgressKind.BatchCompleted, "完了");
            }

            // 260717Claude: run 全体の要約。中断時も途中までの結果件数が残る。
            int succeeded = results.Count(r => !r.IsCanceled && r.ExitCode == 0 && r.ExceptionMessage is null);
            int canceled = results.Count(r => r.IsCanceled);
            runLog.WriteLine(
                $"run end: jobs={results.Count}/{progressScope.TotalJobCount}, succeeded={succeeded}, failed={results.Count - succeeded - canceled}, canceled={canceled}, " +
                $"savedSpectra={progressScope.CompletedSpectrumCount}/{progressScope.TotalSpectrumCount}, elapsed={SimulationRunLog.FormatSeconds(runStopwatch.Elapsed)}");

            return results;
        }

        // 260528Codex: batch 単位の表示をまとめ、RunAsync の本筋を batch 進行だけにします。
        private static void ReportBatchProgress(
            SimulationProgressScope progressScope,
            SimulationExecutionBatch batch,
            int batchNumber,
            SimulationExecutionProgressKind kind,
            string statusText)
        {
            int spectrumCount = batch.Jobs.Sum(job => job.Reservations.Count);
            progressScope.Report(
                kind,
                batch.SolutionName,
                batchNumber,
                progressScope.NextJobIndexPreview,
                spectrumCount,
                $"{batch.SolutionName}: batch {batchNumber}/{progressScope.BatchCount} {statusText}, spectra {spectrumCount}");
        }

        // 260528Codex: スクリプト生成、古い出力削除、DTSA-II 起動、結果収集を job context に閉じ込めます。
        // 260717Claude: attempt の内側は runner に委ね、ここではジョブ単位の前処理と結果変換・進捗確定だけを行う。
        private async Task<SimulationExecutionResult> ExecuteJobAsync(
            SimulationExecutionJob job,
            string solutionName,
            int batchIndex,
            SimulationProgressScope progressScope,
            SimulationRunLog runLog,
            CancellationToken cancellationToken)
        {
            var progress = new SimulationJobProgress(progressScope, job, solutionName, batchIndex);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                progress.ReportJobProgress(SimulationExecutionProgressKind.JobStarted, "DTSA-II job 準備開始");
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(job.Property.OutputFolder);
                DeleteReservedOutputFiles(job.Reservations);

                var attempt = await _processRunner.RunAttemptAsync(job, attemptNumber: 1, progress, runLog, cancellationToken);
                LogAttemptResult(runLog, solutionName, job, attempt);
                var result = ConvertToExecutionResult(job, attempt);
                progress.CompleteUnreportedSuccessfulSpectra(result);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                runLog.WriteLine($"job canceled before start: solution={solutionName} script={Path.GetFileName(job.ScriptPath)}");
                var result = CreateCanceledResult(job, string.Empty, string.Empty);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (Exception ex)
            {
                // 260717Claude: attempt 前のジョブ準備 (出力フォルダ作成・予約ファイル削除) の失敗もログへ残す。
                runLog.WriteLine($"job setup failed: solution={solutionName} script={Path.GetFileName(job.ScriptPath)} detail={SimulationRunLog.Flatten(ex.Message)}");
                var result = new SimulationExecutionResult(
                    job.Reservations,
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    ExceptionMessage: ex.Message);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
        }

        // 260717Claude: attempt 要約は常に 1 行。失敗 attempt (キャンセル除く) だけ stdout/stderr 全文を証跡として残す。
        //   成功 attempt の全文を書かないことでログ肥大を防ぐ (要件定義で確定した方針)。
        private static void LogAttemptResult(
            SimulationRunLog runLog,
            string solutionName,
            SimulationExecutionJob job,
            SimulationProcessAttemptResult attempt)
        {
            string script = Path.GetFileName(job.ScriptPath);
            string pidText = attempt.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string detailText = attempt.FailureDetail is null
                ? string.Empty
                : $" detail={SimulationRunLog.Flatten(attempt.FailureDetail)}";
            runLog.WriteLine(
                $"attempt: solution={solutionName} script={script} attempt={attempt.AttemptNumber} pid={pidText} " +
                $"outcome={attempt.Outcome} exit={attempt.ExitCode} reserved={job.Reservations.Count} saved={attempt.SavedSpectrumFiles.Count} " +
                $"elapsed={SimulationRunLog.FormatSeconds(attempt.Elapsed)}{detailText}");

            if (attempt.Outcome is SimulationAttemptOutcome.Succeeded or SimulationAttemptOutcome.Canceled)
                return;

            string context = $"{script} attempt={attempt.AttemptNumber} pid={pidText}";
            runLog.WriteBlock($"stdout: {context}", attempt.StandardOutput);
            runLog.WriteBlock($"stderr: {context}", attempt.StandardError);
        }

        // 260717Claude: attempt 結果を manifest 更新側が読む既存形式へ変換する。判定規則は従来と同一
        //   (watchdog/起動失敗は ExceptionMessage 有り、キャンセルは IsCanceled、正常系は ExitCode のみで判定)。
        private static SimulationExecutionResult ConvertToExecutionResult(
            SimulationExecutionJob job,
            SimulationProcessAttemptResult attempt) =>
            attempt.Outcome switch
            {
                SimulationAttemptOutcome.Canceled => new SimulationExecutionResult(
                    job.Reservations,
                    ExitCode: -1,
                    attempt.StandardOutput,
                    attempt.StandardError,
                    ExceptionMessage: null,
                    IsCanceled: true,
                    SavedSpectrumFiles: attempt.SavedSpectrumFiles),
                SimulationAttemptOutcome.StartupTimeout
                    or SimulationAttemptOutcome.RunningIdleTimeout
                    or SimulationAttemptOutcome.LaunchFailed => new SimulationExecutionResult(
                    job.Reservations,
                    attempt.ExitCode,
                    attempt.StandardOutput,
                    attempt.StandardError,
                    ExceptionMessage: attempt.FailureDetail,
                    SavedSpectrumFiles: attempt.SavedSpectrumFiles),
                _ => new SimulationExecutionResult(
                    job.Reservations,
                    attempt.ExitCode,
                    attempt.StandardOutput,
                    attempt.StandardError,
                    ExceptionMessage: null,
                    SavedSpectrumFiles: attempt.SavedSpectrumFiles)
            };

        // 260513Codex: 再生成時に同じ fileName を使うため、実行直前に対象 spectrum ファイルだけを消します。
        private static void DeleteReservedOutputFiles(IReadOnlyList<SpectrumSimulationReservation> reservations)
        {
            foreach (var reservation in reservations)
            {
                string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }

        // 260513Codex: キャンセル結果は Failed と区別し、manifest 側で Pending に戻せる印を付けます。
        private static SimulationExecutionResult CreateCanceledResult(
            SimulationExecutionJob job,
            string standardOutput,
            string standardError,
            IReadOnlySet<string>? savedSpectrumFiles = null) =>
            new(
                job.Reservations,
                ExitCode: -1,
                StandardOutput: standardOutput,
                StandardError: standardError,
                ExceptionMessage: null,
                IsCanceled: true,
                SavedSpectrumFiles: savedSpectrumFiles);
    }

    // 260528Codex: 進捗全体の件数とスレッド間カウンタをまとめ、長い引数リレーを避けます。
    // 260717Claude: attempt 実行を DtsaSimulationProcessRunner へ分離したのに伴い、runner からも進捗を報告できるよう
    //   service の private nested からトップレベル internal へ昇格 (実装は変更なし)。
    internal sealed class SimulationProgressScope
    {
        private readonly IProgress<SimulationExecutionProgress>? _progress;
        private int _completedJobCount;
        private int _completedSpectrumCount;
        private int _nextJobIndex;

        public SimulationProgressScope(
            SimulationExecutionPlan plan,
            IProgress<SimulationExecutionProgress>? progress)
        {
            _progress = progress;
            BatchCount = plan.Batches.Count;
            TotalJobCount = plan.Batches.Sum(batch => batch.Jobs.Count);
            TotalSpectrumCount = plan.Batches.Sum(batch => batch.Jobs.Sum(job => job.Reservations.Count));
        }

        public int BatchCount { get; }
        public int TotalJobCount { get; }
        public int TotalSpectrumCount { get; }
        public int CompletedJobCount => Volatile.Read(ref _completedJobCount);
        public int CompletedSpectrumCount => Volatile.Read(ref _completedSpectrumCount);
        public int NextJobIndexPreview => Volatile.Read(ref _nextJobIndex);

        public int ReserveJobIndex() => Interlocked.Increment(ref _nextJobIndex);
        public int MarkJobCompleted() => Interlocked.Increment(ref _completedJobCount);
        public int MarkSpectrumCompleted() => Interlocked.Increment(ref _completedSpectrumCount);

        public void Report(
            SimulationExecutionProgressKind kind,
            string solutionName,
            int batchIndex,
            int jobIndex,
            int spectrumCount,
            string message,
            int? exitCode = null,
            TimeSpan? elapsed = null) =>
            _progress?.Report(new SimulationExecutionProgress(
                kind,
                solutionName,
                batchIndex,
                BatchCount,
                jobIndex,
                TotalJobCount,
                CompletedJobCount,
                TotalSpectrumCount,
                CompletedSpectrumCount,
                spectrumCount,
                message,
                exitCode,
                elapsed));
    }

    // 260717Claude: SimulationProgressScope と同じ理由でトップレベル internal へ昇格 (実装は変更なし)。
    internal sealed class SimulationJobProgress
    {
        private readonly SimulationProgressScope _scope;
        private readonly SimulationExecutionJob _job;
        private readonly object _reportedSpectrumLock = new();
        private readonly HashSet<string> _reportedSpectrumFiles = new(StringComparer.OrdinalIgnoreCase);

        public SimulationJobProgress(
            SimulationProgressScope scope,
            SimulationExecutionJob job,
            string solutionName,
            int batchIndex)
        {
            _scope = scope;
            _job = job;
            SolutionName = solutionName;
            BatchIndex = batchIndex;
            JobIndex = scope.ReserveJobIndex();
        }

        private string SolutionName { get; }
        private int BatchIndex { get; }
        private int JobIndex { get; }

        public void ReportJobProgress(SimulationExecutionProgressKind kind, string message) =>
            _scope.Report(
                kind,
                SolutionName,
                BatchIndex,
                JobIndex,
                _job.Reservations.Count,
                $"{message}: {SolutionName}, job {JobIndex}/{_scope.TotalJobCount}, spectra {_job.Reservations.Count}");

        public void ReportSpectrumSaved(string fileName)
        {
            if (!TryMarkSpectrumSaved(fileName))
                return;

            _scope.Report(
                SimulationExecutionProgressKind.SpectrumSaved,
                SolutionName,
                BatchIndex,
                JobIndex,
                _job.Reservations.Count,
                $"spectrum 保存: {SolutionName}, {_scope.CompletedSpectrumCount}/{_scope.TotalSpectrumCount}, job {JobIndex}/{_scope.TotalJobCount}");
        }

        public void CompleteUnreportedSuccessfulSpectra(SimulationExecutionResult result)
        {
            if (result.IsCanceled || result.ExitCode != 0 || result.ExceptionMessage is not null)
                return;

            foreach (var reservation in _job.Reservations)
                ReportSpectrumSaved(reservation.FileName);
        }

        public void ReportJobFinished(SimulationExecutionResult result, TimeSpan elapsed)
        {
            _scope.MarkJobCompleted();
            var kind = result.IsCanceled
                ? SimulationExecutionProgressKind.JobCanceled
                : result.ExitCode == 0 && result.ExceptionMessage is null
                    ? SimulationExecutionProgressKind.JobCompleted
                    : SimulationExecutionProgressKind.JobFailed;
            string statusText = kind switch
            {
                SimulationExecutionProgressKind.JobCompleted => "DTSA-II job 完了",
                SimulationExecutionProgressKind.JobCanceled => "DTSA-II job キャンセル",
                _ => "DTSA-II job 失敗"
            };

            _scope.Report(
                kind,
                SolutionName,
                BatchIndex,
                JobIndex,
                _job.Reservations.Count,
                $"{statusText}: {SolutionName}, job {JobIndex}/{_scope.TotalJobCount}, spectra {_job.Reservations.Count}",
                result.ExitCode,
                elapsed);
        }

        private bool TryMarkSpectrumSaved(string fileName)
        {
            lock (_reportedSpectrumLock)
            {
                if (!_reportedSpectrumFiles.Add(fileName))
                    return false;

                _scope.MarkSpectrumCompleted();
                return true;
            }
        }
    }
}
