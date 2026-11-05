// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
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
    // 260901Codex: 実行処理が次の鉱物へ進む前に、各鉱物バッチを保存する。
    // 261103Codex: バッチ間の実行順は独立とし、各バッチの全結果がそろった時に直列で保存する。
    internal delegate Task SimulationBatchCompletedCallback(
        SimulationExecutionBatch batch,
        IReadOnlyList<SimulationExecutionResult> results);

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
        // 261103Codex: 全鉱物のjobを共通上限で投入し、中止後は未投入jobを起動しない。
        public async Task<IReadOnlyList<SimulationExecutionResult>> RunAsync(
            SimulationExecutionPlan plan,
            IProgress<SimulationExecutionProgress>? progress,
            CancellationToken cancellationToken,
            // 260901Codex: 任意の第4引数で、既存呼び出しを保ったままバッチのチェックポイントを有効にする。
            // 261105Claude: 全呼び出しが渡すので必須にした。
            SimulationBatchCompletedCallback batchCompleted)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var progressScope = new SimulationProgressScope(plan, progress);
            // 260717Claude: run 単位の永続ログ。GUI/headless どちらでも RunAsync 起点で必ず 1 ファイル残し、
            //   実行後に失敗原因 (watchdog kill・stderr) を追跡できるようにする。
            var runLog = SimulationRunLog.CreateForRun();
            // 261103Codex: 変更前後で同じCPU時間計測を使い、runの最後まで集計する。
            await using var metrics = new SimulationRunMetrics(runLog);
            // 260717Claude: 起動ゲートは run 単位で共有し、batch をまたいでも Process.Start の最小間隔を保つ。
            // 261105Claude: run 単位の共有物は 1 つにまとめ、job と attempt の層を通す引数を減らす。
            var run = new SimulationRunContext(runLog, new SimulationLaunchGate(), metrics);
            var runStopwatch = Stopwatch.StartNew();
            // 261103Codex: 実際のrun上限を記録し、ジョブ件数と同時稼働数を混同しない。
            runLog.WriteLine($"run start: batches={progressScope.BatchCount}, jobs={progressScope.TotalJobCount}, spectra={progressScope.TotalSpectrumCount}, parallel={plan.ParallelCount}");
            // 261030Claude: 生成に使った DTSA-II の版を残し、pool の出所を後から追えるようにする。
            foreach (string dtsaFolder in plan.Batches.SelectMany(static batch => batch.Jobs).Select(static job => job.DtsaFolder).Distinct(StringComparer.OrdinalIgnoreCase))
                runLog.WriteLine($"dtsa: {DtsaInstallation.Describe(dtsaFolder)}");
            // 261105Claude: GUI のログ欄はファイルに残らないので、スクリプトへ渡す検出器の値も run ログへ残す。
            foreach (string detector in plan.Batches.SelectMany(static batch => batch.Jobs).Select(static job => job.Property.DetectorProfile.Describe()).Distinct(StringComparer.Ordinal))
                runLog.WriteLine($"detector: {detector}");

            // 261103Codex: バッチ末尾の待機をなくす。起動・再試行・監視は従来のExecuteJobAsync内に残す。
            IReadOnlyList<SimulationExecutionResult> results;
            try
            {
                // 260717Claude: GUI は UI スレッドから RunAsync を await するため、job 本体 (500ms のファイルポーリングや
                //   失敗時の stdout 全文ログ書き込み) の継続が UI コンテキストへ戻らないよう Task.Run で切り離す。
                //   UI 更新は Progress<T> が生成元 (UI) スレッドへ post するので従来どおり安全。
                // 261103Codex: schedulerの全workerと保存consumerをUIから切り離す。
                // 261104Codex: run末尾のログと資源計測の破棄もUIスレッドへ戻さない。
                results = await Task.Run(() => SimulationExecutionScheduler.RunAsync(
                    plan, progressScope,
                    (job, solution, batchNumber, token) => ExecuteJobAsync(job, solution, batchNumber, progressScope, run, token),
                    // 261104Codex: 保存と例外ログの境界に名前を付け、投入処理の入れ子を減らす。
                    SaveBatchAsync, cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 261103Codex: 保存失敗でも全worker回収後の状態とrunの経過を残す。
                runLog.WriteLine($"run aborted: completedJobs={progressScope.CompletedJobCount}/{progressScope.TotalJobCount}, " +
                    $"savedSpectra={progressScope.CompletedSpectrumCount}/{progressScope.TotalSpectrumCount}, elapsed={SimulationRunLog.FormatSeconds(runStopwatch.Elapsed)} detail={SimulationRunLog.Flatten(ex.Message)}");
                throw;
            }

            // 260717Claude: run 全体の要約。中断時も途中までの結果件数が残る。
            int succeeded = results.Count(r => !r.IsCanceled && r.ExitCode == 0 && r.ExceptionMessage is null);
            int canceled = results.Count(r => r.IsCanceled);
            runLog.WriteLine(
                $"run end: jobs={results.Count}/{progressScope.TotalJobCount}, succeeded={succeeded}, failed={results.Count - succeeded - canceled}, canceled={canceled}, " +
                $"savedSpectra={progressScope.CompletedSpectrumCount}/{progressScope.TotalSpectrumCount}, elapsed={SimulationRunLog.FormatSeconds(runStopwatch.Elapsed)}");

            return results;

            // 261104Codex: 保存の成功・失敗ログと例外の伝播を同じ非同期境界に保つ。
            async Task SaveBatchAsync(SimulationExecutionBatch batch, IReadOnlyList<SimulationExecutionResult> batchResults)
            {
                try
                {
                    await batchCompleted(batch, batchResults).ConfigureAwait(false);
                    runLog.WriteLine($"batch checkpoint: solution={batch.SolutionName} jobs={batchResults.Count}");
                }
                catch (Exception ex)
                {
                    runLog.WriteLine($"batch checkpoint failed: solution={batch.SolutionName} detail={SimulationRunLog.Flatten(ex.Message)}");
                    throw;
                }
            }
        }

        // 260528Codex: スクリプト生成、古い出力削除、DTSA-II 起動、結果収集を job context に閉じ込めます。
        // 260717Claude: attempt の内側は runner に委ね、ここではジョブ単位の前処理と結果変換・進捗確定だけを行う。
        private async Task<SimulationExecutionResult> ExecuteJobAsync(
            SimulationExecutionJob job,
            string solutionName,
            int batchIndex,
            SimulationProgressScope progressScope,
            SimulationRunContext run,
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

                // 261103Codex: 初回・再試行とも同じrunへCPU時間を集計する。
                var attempt = await _processRunner.RunAttemptAsync(job, attemptNumber: 1, progress, run, cancellationToken);
                LogAttemptResult(run.Log, solutionName, job, attempt);
                attempt = await RetryStartupTimeoutOnceAsync(job, solutionName, attempt, progress, run, cancellationToken);
                var result = ConvertToExecutionResult(job, attempt, run.Log);
                progress.CompleteUnreportedSuccessfulSpectra(result);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                run.Log.WriteLine($"job canceled before start: solution={solutionName} script={Path.GetFileName(job.ScriptPath)}");
                // 260717Codex: 試行前の中止結果はここだけなので、判定箇所で構築する。
                // 261105Claude: 未投入 job の中止と同じ形にするため、共通の Canceled で作る。
                var result = SimulationExecutionResult.Canceled(job.Reservations);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (Exception ex)
            {
                // 260717Claude: attempt 前のジョブ準備 (出力フォルダ作成・予約ファイル削除) の失敗もログへ残す。
                //   FailureReason から run ログへ辿れるよう、この経路にも種別 prefix を付ける (レビュー指摘)。
                run.Log.WriteLine($"job setup failed: solution={solutionName} script={Path.GetFileName(job.ScriptPath)} detail={SimulationRunLog.Flatten(ex.Message)}");
                var result = new SimulationExecutionResult(
                    job.Reservations,
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    ExceptionMessage: $"[JobSetupFailed] log={run.Log.FileName}; {ex.Message}");
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

            // 260717Claude: attempt 2 は成功時も全文を残す (リトライで復旧したケースの検証に使う。要件定義で確定)。
            bool dumpOutput = attempt.AttemptNumber > 1
                || attempt.Outcome is not (SimulationAttemptOutcome.Succeeded or SimulationAttemptOutcome.Canceled);
            if (!dumpOutput)
                return;

            string context = $"{script} attempt={attempt.AttemptNumber} pid={pidText}";
            runLog.WriteBlock($"stdout: {context}", attempt.StandardOutput);
            runLog.WriteBlock($"stderr: {context}", attempt.StandardError);
        }

        // 260717Claude: 起動フェーズ watchdog timeout の 1 回だけの自動リトライ境界。対象は
        //   StartupTimeout・保存 0 本 (kill 後の最終ディスクスキャン済み snapshot で判定)・attempt 1・未キャンセルの全成立時のみ。
        //   attempt 2 は予約ファイルの初期削除を行わないため、起動直前に再スキャンして遅延出現ファイルが 1 件でもあれば
        //   partial save とみなしてリトライを中止し、保存済み snapshot へ反映する (attempt 1 由来の出力を消さない保証)。
        private async Task<SimulationProcessAttemptResult> RetryStartupTimeoutOnceAsync(
            SimulationExecutionJob job,
            string solutionName,
            SimulationProcessAttemptResult attempt,
            SimulationJobProgress progress,
            SimulationRunContext run,
            CancellationToken cancellationToken)
        {
            // 260717Codex: 1回再試行の全条件が満たされなければ、すぐに返す。
            if (attempt.Outcome != SimulationAttemptOutcome.StartupTimeout ||
                attempt.AttemptNumber != 1 ||
                attempt.SavedSpectrumFiles.Count > 0 ||
                cancellationToken.IsCancellationRequested)
                return attempt;

            // 260717Claude: この時点で attempt 1 のプロセスツリーは kill →終了待ち→最終スキャンまで完了しており、
            //   以後に予約ファイルを書ける主体は存在しない。よってこの決定時点の再スキャンが実質の「起動直前」チェック
            //   (スタガー待ち中の遅延出現は書き手不在のため起こらない)。
            string script = Path.GetFileName(job.ScriptPath);
            string[] lateSavedFiles = job.Reservations
                .Where(reservation => File.Exists(Path.Combine(reservation.PoolFolder, reservation.FileName)))
                .Select(reservation => reservation.FileName)
                .ToArray();
            if (lateSavedFiles.Length > 0)
            {
                run.Log.WriteLine($"retry aborted: script={script} reason=late output files detected ({lateSavedFiles.Length})");
                var merged = new HashSet<string>(attempt.SavedSpectrumFiles, StringComparer.OrdinalIgnoreCase);
                foreach (string fileName in lateSavedFiles)
                {
                    merged.Add(fileName);
                    progress.ReportSpectrumSaved(fileName);
                }

                return attempt with { SavedSpectrumFiles = merged };
            }

            run.Log.WriteLine($"retry: solution={solutionName} script={script} reason=StartupTimeout attempt=2");
            // 261103Codex: watchdogと再試行条件はそのままに計測だけを共有する。
            var secondAttempt = await _processRunner.RunAttemptAsync(job, attemptNumber: 2, progress, run, cancellationToken);
            LogAttemptResult(run.Log, solutionName, job, secondAttempt);
            return secondAttempt;
        }

        // 260717Claude: attempt 結果を manifest 更新側が読む既存形式へ変換する。判定規則は従来と同一
        //   (キャンセルは IsCanceled、正常系は ExitCode のみで判定)。最終失敗の ExceptionMessage には
        //   構造化種別と run ログファイル名の prefix を必ず付け、manifest の FailureReason から証跡へ辿れるようにする。
        private static SimulationExecutionResult ConvertToExecutionResult(
            SimulationExecutionJob job,
            SimulationProcessAttemptResult attempt,
            SimulationRunLog runLog)
        {
            // 260717Codex: 共通の結果形式を一度だけ構築し、結果ごとに中止と失敗のメタデータだけを変える。
            bool isCanceled = attempt.Outcome == SimulationAttemptOutcome.Canceled;
            string? exceptionMessage = attempt.Outcome is SimulationAttemptOutcome.Canceled or SimulationAttemptOutcome.Succeeded
                ? null
                : BuildFailureExceptionMessage(attempt, runLog);
            return new SimulationExecutionResult(
                job.Reservations,
                isCanceled ? -1 : attempt.ExitCode,
                attempt.StandardOutput,
                attempt.StandardError,
                exceptionMessage,
                IsCanceled: isCanceled,
                SavedSpectrumFiles: attempt.SavedSpectrumFiles);
        }

        // 260717Claude: manifest FailureReason 向けの詳細上限。全文は run ログ側にあるため、ここは要点だけ残す
        //   (BuildFailureReason は ExceptionMessage を無切り詰めで採用するので、ここで cap しないと manifest が肥大する)。
        private const int FailureDetailMaxLength = 1000;

        // 260717Claude: FailureReason の先頭 prefix。詳細は watchdog/例外メッセージを優先し、無ければ stderr/stdout、
        //   それも無ければ exit code を残す。prefix は先頭なので cap 後も必ず残る。
        private static string BuildFailureExceptionMessage(SimulationProcessAttemptResult attempt, SimulationRunLog runLog)
        {
            string detail = attempt.FailureDetail
                ?? (!string.IsNullOrWhiteSpace(attempt.StandardError) ? attempt.StandardError
                    : !string.IsNullOrWhiteSpace(attempt.StandardOutput) ? attempt.StandardOutput
                    : $"DTSA-II exit code {attempt.ExitCode}");
            if (detail.Length > FailureDetailMaxLength)
                detail = detail[..FailureDetailMaxLength];

            return $"[{attempt.Outcome}] log={runLog.FileName}; {detail}";
        }

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

    }

    // 260528Codex: 進捗全体の件数とスレッド間カウンタをまとめ、長い引数リレーを避けます。
    // 260717Claude: attempt 実行を DtsaSimulationProcessRunner へ分離したのに伴い、runner からも進捗を報告できるよう
    //   service の private nested からトップレベル internal へ昇格 (実装は変更なし)。
    internal sealed class SimulationProgressScope
    {
        private readonly IProgress<SimulationExecutionProgress>? _progress;
        // 261103Codex: 複数バッチの通知がUIへpostされる順序とカウンタの増加順を一致させる。
        private readonly object _reportGate = new();
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
        // 260717Codex: これらの増分は通知であり、数値の戻り値を使う呼び出し側はない。
        public void MarkJobCompleted() => Interlocked.Increment(ref _completedJobCount);
        public void MarkSpectrumCompleted() => Interlocked.Increment(ref _completedSpectrumCount);

        // 261103Codex: 件数のsnapshot取得と通知を一緒に直列化し、GUIの表示値が逆戻りしないようにする。
        public void Report(
            SimulationExecutionProgressKind kind,
            string solutionName,
            int batchIndex,
            int jobIndex,
            int spectrumCount,
            string message,
            int? exitCode = null,
            TimeSpan? elapsed = null)
        {
            lock (_reportGate)
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
