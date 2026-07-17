using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260717Claude: DTSA-II 1 attempt (1プロセスの起動〜終了) の結果分類。リトライ判断や失敗ログはこの構造化種別で行い、
    //   watchdog メッセージ文字列の解析に依存させない。StartupTimeout は watchdog 2段化 (起動フェーズ導入) 後に使う。
    internal enum SimulationAttemptOutcome
    {
        Succeeded,
        ProcessFailed,
        StartupTimeout,
        RunningIdleTimeout,
        Canceled,
        LaunchFailed
    }

    // 260717Claude: 単一 attempt の実行結果。SavedSpectrumFiles は kill 後の最終ディスクスキャンまで反映した同期済み snapshot で、
    //   「保存0本か」のリトライ判定はこの集合を正とする。
    internal sealed record SimulationProcessAttemptResult(
        SimulationAttemptOutcome Outcome,
        int AttemptNumber,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string? FailureDetail,
        int? ProcessId,
        TimeSpan Elapsed,
        IReadOnlySet<string> SavedSpectrumFiles);

    // 260717Claude: run 内の Process.Start を一定間隔に間引く起動ゲート。20 JVM のほぼ同時ブートによる
    //   DTSA-II 内部共有リソース (Derby 等) の起動競合スタックを避けるのが目的で、起動後の実行は並列のまま。
    //   待機者はセマフォで直列に並び、先頭が間隔分の Delay を消化してから次へ許可を回す (キャンセル可能)。
    internal sealed class SimulationLaunchGate
    {
        private static readonly TimeSpan LaunchInterval = TimeSpan.FromSeconds(2);

        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _nextAllowedUtc = DateTime.MinValue;

        public async Task WaitForLaunchSlotAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (now < _nextAllowedUtc)
                    await Task.Delay(_nextAllowedUtc - now, cancellationToken);

                _nextAllowedUtc = DateTime.UtcNow + LaunchInterval;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    // 260717Claude: DTSA-II 外部プロセスの単一 attempt 実行 (script 作成→起動→監視→watchdog→結果生成) を担当する。
    //   ジョブ単位の判断 (予約ファイルの初期削除、attempt 間リトライ、SimulationExecutionResult への変換) は
    //   SimulationExecutionService 側に置き、attempt の内側だけをこのクラスに閉じ込める。
    internal sealed class DtsaSimulationProcessRunner
    {
        // 260528Codex: DTSA-II script からの保存完了通知を識別し、生成ファイル数ベースの進捗へ変換します。
        private const string SavedSpectrumMarker = "MINERASCOPE_SPECTRUM_SAVED|";
        // 260611Codex: Poll generated spectrum files because DTSA-II stdout can be delayed until process exit.
        private static readonly TimeSpan OutputFilePollInterval = TimeSpan.FromMilliseconds(500);
        // 260629Codex: Treat DTSA-II as stuck only when stdout, files, and CPU are all idle for a while.
        private static readonly TimeSpan DtsaIdleTimeout = TimeSpan.FromMinutes(10);
        // 260717Claude: 保存 0 本の起動フェーズは短く見切る。正常起動中の JVM は JIT/クラスロードで CPU を使い
        //   idle 扱いにならないため、3 分間の完全無活動は起動競合スタックとみなして kill →リトライで復旧させる。
        private static readonly TimeSpan DtsaStartupIdleTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan DtsaWatchdogPollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DtsaCpuActivityThreshold = TimeSpan.FromSeconds(1);

        private readonly SimulationScriptGenerator _scriptGenerator;

        public DtsaSimulationProcessRunner(SimulationScriptGenerator scriptGenerator)
        {
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
        }

        // 260717Claude: script 作成から結果生成までを 1 attempt として実行する。例外は投げずに Outcome へ畳む
        //   (Canceled / LaunchFailed) ので、呼び出し側はこの結果だけでリトライ・manifest 反映を判断できる。
        public async Task<SimulationProcessAttemptResult> RunAttemptAsync(
            SimulationExecutionJob job,
            int attemptNumber,
            SimulationJobProgress progress,
            SimulationRunLog runLog,
            SimulationLaunchGate launchGate,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? scriptFolder = Path.GetDirectoryName(job.ScriptPath);
                if (!string.IsNullOrWhiteSpace(scriptFolder))
                    Directory.CreateDirectory(scriptFolder);

                File.WriteAllText(job.ScriptPath, _scriptGenerator.Generate(job.Property, job.ParallelIndex));
                progress.ReportJobProgress(
                    SimulationExecutionProgressKind.ScriptWritten,
                    $"script 作成: {Path.GetFileName(job.ScriptPath)}");

                return await RunProcessAsync(job, attemptNumber, progress, runLog, launchGate, stopwatch, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateFailureResult(SimulationAttemptOutcome.Canceled, attemptNumber, null, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                return CreateFailureResult(SimulationAttemptOutcome.LaunchFailed, attemptNumber, ex.Message, stopwatch.Elapsed);
            }
        }

        // 260717Claude: プロセス起動前の失敗 (キャンセル・script/起動例外) は出力も保存ファイルも無いので空で返す。
        private static SimulationProcessAttemptResult CreateFailureResult(
            SimulationAttemptOutcome outcome,
            int attemptNumber,
            string? failureDetail,
            TimeSpan elapsed) =>
            new(
                outcome,
                attemptNumber,
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                failureDetail,
                ProcessId: null,
                elapsed,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // 260528Codex: 標準出力を逐次読み、保存完了マーカーを spectrum 進捗へ即時反映します。
        private async Task<SimulationProcessAttemptResult> RunProcessAsync(
            SimulationExecutionJob job,
            int attemptNumber,
            SimulationJobProgress progress,
            SimulationRunLog runLog,
            SimulationLaunchGate launchGate,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            // 260717Claude: 起動ゲートで Process.Start だけを間引く (実行は並列のまま)。待ち時間はログで検証できるよう残す。
            var staggerStopwatch = Stopwatch.StartNew();
            await launchGate.WaitForLaunchSlotAsync(cancellationToken);
            long staggerWaitMs = staggerStopwatch.ElapsedMilliseconds;

            // 260626Codex: Launch the dtsa2.msi app image directly in no-GUI mode while keeping stdout markers redirected.
            ProcessStartInfo startInfo = DtsaMsiInstallation.CreateStartInfo(job.DtsaFolder, job.ScriptPath);

            using Process process = new()
            {
                StartInfo = startInfo
            };

            process.Start();
            int? processId = TryGetProcessId(process);
            runLog.WriteLine(
                $"process start: script={Path.GetFileName(job.ScriptPath)} attempt={attemptNumber} " +
                $"pid={processId?.ToString(CultureInfo.InvariantCulture) ?? "-"} staggerWaitMs={staggerWaitMs}");
            progress.ReportJobProgress(SimulationExecutionProgressKind.ProcessStarted, "DTSA-II process 起動");
            // 260606Claude: 保存完了マーカーのファイル名をここに集め、ジョブ全体の成否とは別に「保存できた spectrum」を結果へ渡します。
            // 260717Claude: stdout 読み取りタスクとファイルポーリングタスクが同時に触るため、lock 付き tracker で同期する
            //   (従来は裸の HashSet を並行更新しており race だった)。
            var savedFiles = new SavedSpectrumFileTracker();
            var activity = new SimulationJobActivityTracker();
            // 260611Codex: File polling drives live progress even when the process buffers stdout.
            using var outputMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var outputMonitorTask = MonitorOutputFilesAsync(job, progress, savedFiles, activity, outputMonitorCts.Token);
            var standardOutputTask = ReadStandardOutputAsync(process, progress, savedFiles, activity);
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            var watchdogTask = WatchDtsaProcessAsync(process, job, savedFiles, activity, runLog, watchdogCts.Token);
            // 260717Claude: kill 前後のログで script/pid を対応づける (並行ジョブが混ざっても追える)。
            string killContext = $"script={Path.GetFileName(job.ScriptPath)} pid={processId?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

            try
            {
                var processExitTask = process.WaitForExitAsync(cancellationToken);
                var completedTask = await Task.WhenAny(processExitTask, watchdogTask);
                if (completedTask == watchdogTask)
                {
                    WatchdogTimeoutResult? watchdogTimeout = await watchdogTask;
                    if (watchdogTimeout is not null)
                    {
                        runLog.WriteLine($"kill start: {killContext} reason=watchdog");
                        KillProcessTree(process);
                        await WaitForProcessExitAfterKillAsync(process);
                        runLog.WriteLine($"kill done: {killContext}");
                        await StopOutputMonitorAsync(outputMonitorCts, outputMonitorTask);
                        await StopWatchdogAsync(watchdogCts, watchdogTask);
                        ReportExistingOutputFiles(job, progress, savedFiles, activity);
                        string timeoutOutput = await ReadProcessOutputAsync(standardOutputTask);
                        string timeoutError = await ReadProcessOutputAsync(standardErrorTask);
                        return new SimulationProcessAttemptResult(
                            watchdogTimeout.Outcome,
                            attemptNumber,
                            ExitCode: -1,
                            timeoutOutput,
                            timeoutError,
                            watchdogTimeout.Message,
                            processId,
                            stopwatch.Elapsed,
                            savedFiles.CreateSnapshot());
                    }
                }

                await processExitTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                runLog.WriteLine($"kill start: {killContext} reason=cancel");
                KillProcessTree(process);
                await WaitForProcessExitAfterKillAsync(process);
                runLog.WriteLine($"kill done: {killContext}");
                await StopOutputMonitorAsync(outputMonitorCts, outputMonitorTask);
                await StopWatchdogAsync(watchdogCts, watchdogTask);
                ReportExistingOutputFiles(job, progress, savedFiles, activity);
                string canceledOutput = await ReadProcessOutputAsync(standardOutputTask);
                string canceledError = await ReadProcessOutputAsync(standardErrorTask);
                return new SimulationProcessAttemptResult(
                    SimulationAttemptOutcome.Canceled,
                    attemptNumber,
                    ExitCode: -1,
                    canceledOutput,
                    canceledError,
                    FailureDetail: null,
                    processId,
                    stopwatch.Elapsed,
                    savedFiles.CreateSnapshot());
            }

            await StopOutputMonitorAsync(outputMonitorCts, outputMonitorTask);
            await StopWatchdogAsync(watchdogCts, watchdogTask);
            ReportExistingOutputFiles(job, progress, savedFiles, activity);
            string standardOutput = await ReadProcessOutputAsync(standardOutputTask);
            string standardError = await ReadProcessOutputAsync(standardErrorTask);

            int exitCode = process.ExitCode;
            return new SimulationProcessAttemptResult(
                exitCode == 0 ? SimulationAttemptOutcome.Succeeded : SimulationAttemptOutcome.ProcessFailed,
                attemptNumber,
                exitCode,
                standardOutput,
                standardError,
                FailureDetail: null,
                processId,
                stopwatch.Elapsed,
                savedFiles.CreateSnapshot());
        }

        // 260717Claude: Start 直後でも稀に取得に失敗するため、PID はログ用の補助情報として nullable で持つ。
        private static int? TryGetProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // 260528Codex: DTSA-II スクリプトの保存完了マーカーを逐次読み、生成ファイル数ベースで進捗を進めます。
        // 260606Claude: 併せてマーカーのファイル名を savedFiles へ記録し、ジョブ途中失敗でも保存済み spectrum を Completed と判定できるようにします。
        private static async Task<string> ReadStandardOutputAsync(
            Process process,
            SimulationJobProgress progress,
            SavedSpectrumFileTracker savedFiles,
            SimulationJobActivityTracker activity)
        {
            var builder = new StringBuilder();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
            {
                builder.AppendLine(line);
                if (!line.StartsWith(SavedSpectrumMarker, StringComparison.Ordinal))
                    continue;

                // 260606Claude: マーカー末尾の保存パス (パスに '|' は現れない) からファイル名だけ取り出して記録します。
                string savedFileName = Path.GetFileName(line.AsSpan(line.LastIndexOf('|') + 1)).ToString();
                if (savedFileName.Length == 0)
                    continue;

                activity.MarkActivity();
                if (savedFiles.TryAdd(savedFileName))
                    progress.ReportSpectrumSaved(savedFileName);
            }

            return builder.ToString();
        }

        // 260611Codex: Count files that appear on disk so the progress bar advances while DTSA-II is still running.
        private static async Task MonitorOutputFilesAsync(
            SimulationExecutionJob job,
            SimulationJobProgress progress,
            SavedSpectrumFileTracker savedFiles,
            SimulationJobActivityTracker activity,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ReportExistingOutputFiles(job, progress, savedFiles, activity);
                    await Task.Delay(OutputFilePollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                ReportExistingOutputFiles(job, progress, savedFiles, activity);
            }
        }

        // 260611Codex: Keep the polling task shutdown quiet; final file detection happens in its finally block.
        private static async Task StopOutputMonitorAsync(CancellationTokenSource monitorCancellation, Task monitorTask)
        {
            monitorCancellation.Cancel();

            try
            {
                await monitorTask;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }

        // 260611Codex: Report each reserved output file at most once through SimulationJobProgress.
        private static void ReportExistingOutputFiles(
            SimulationExecutionJob job,
            SimulationJobProgress progress,
            SavedSpectrumFileTracker savedFiles,
            SimulationJobActivityTracker activity)
        {
            foreach (var reservation in job.Reservations)
            {
                string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
                if (File.Exists(outputPath) && savedFiles.TryAdd(reservation.FileName))
                {
                    activity.MarkActivity();
                    progress.ReportSpectrumSaved(reservation.FileName);
                }
            }
        }

        // 260717Claude: watchdog の kill 判定結果。フェーズ (起動/実行) ごとの終了種別を呼び出し側へ構造化して返す。
        private sealed record WatchdogTimeoutResult(SimulationAttemptOutcome Outcome, string Message);

        // 260513Codex: キャンセル要求後に残った子プロセスも含めて DTSA-II を止めます。
        // 260629Codex: Kill DTSA-II jobs that stop producing stdout, files, and CPU so the batch can return.
        // 260717Claude: 2 段 watchdog。保存 0 本の起動フェーズは 3 分、1 本以上保存後の実行フェーズは従来の 10 分で見切る。
        private static async Task<WatchdogTimeoutResult?> WatchDtsaProcessAsync(
            Process process,
            SimulationExecutionJob job,
            SavedSpectrumFileTracker savedFiles,
            SimulationJobActivityTracker activity,
            SimulationRunLog runLog,
            CancellationToken cancellationToken)
        {
            TimeSpan lastCpuTime = ReadProcessCpuTime(process);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(DtsaWatchdogPollInterval, cancellationToken);
                    if (process.HasExited)
                        return null;

                    TimeSpan currentCpuTime = ReadProcessCpuTime(process);
                    TimeSpan cpuDelta = currentCpuTime - lastCpuTime;
                    lastCpuTime = currentCpuTime;
                    // 260629Codex: Ignore tiny Derby wait/housekeeping CPU ticks; they are not simulation progress.
                    if (cpuDelta >= DtsaCpuActivityThreshold)
                    {
                        activity.MarkActivity();
                        continue;
                    }

                    bool isStartupPhase = savedFiles.Count == 0;
                    TimeSpan idleTimeout = isStartupPhase ? DtsaStartupIdleTimeout : DtsaIdleTimeout;
                    TimeSpan idleFor = activity.IdleFor;
                    if (idleFor >= idleTimeout)
                    {
                        var outcome = isStartupPhase
                            ? SimulationAttemptOutcome.StartupTimeout
                            : SimulationAttemptOutcome.RunningIdleTimeout;
                        // 260717Claude: 判定時点の状態 (種別・idle 時間・保存本数・直近 CPU delta) を残し、後から
                        //   「起動スタックか実行中の停滞か」を切り分けられるようにする。
                        runLog.WriteLine(
                            $"watchdog timeout: type={outcome} script={Path.GetFileName(job.ScriptPath)} " +
                            $"idleFor={SimulationRunLog.FormatSeconds(idleFor)} saved={savedFiles.Count} " +
                            $"lastCpuDeltaMs={(long)cpuDelta.TotalMilliseconds}");
                        string phaseText = isStartupPhase ? " before the first spectrum was saved" : string.Empty;
                        return new WatchdogTimeoutResult(
                            outcome,
                            $"DTSA-II watchdog timeout: no stdout marker, output file, or CPU progress for {FormatTimeSpan(idleTimeout)}{phaseText}. script={Path.GetFileName(job.ScriptPath)}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return null;
        }

        private static async Task StopWatchdogAsync(CancellationTokenSource watchdogCancellation, Task<WatchdogTimeoutResult?> watchdogTask)
        {
            watchdogCancellation.Cancel();

            try
            {
                await watchdogTask;
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }

        private static TimeSpan ReadProcessCpuTime(Process process)
        {
            try
            {
                process.Refresh();
                return process.HasExited ? TimeSpan.Zero : process.TotalProcessorTime;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return TimeSpan.Zero;
            }
        }

        private static string FormatTimeSpan(TimeSpan value) =>
            value.TotalMinutes >= 1
                ? $"{value.TotalMinutes:0.#} minutes"
                : $"{value.TotalSeconds:0.#} seconds";

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        // 260513Codex: Kill 後の終了待ちはキャンセルなしで行い、stdout/stderr の後片付けを安定させます。
        private static async Task WaitForProcessExitAfterKillAsync(Process process)
        {
            try
            {
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }

        // 260513Codex: Kill 後に閉じられたリダイレクト stream の例外は結果反映を邪魔させません。
        private static async Task<string> ReadProcessOutputAsync(Task<string> outputTask)
        {
            try
            {
                return await outputTask;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                return string.Empty;
            }
        }

        // 260717Claude: 保存済み spectrum ファイル名の共有集合。stdout 読み取りとファイルポーリングが並行更新するため
        //   lock で同期し、結果へは snapshot を渡す (裸の HashSet 並行 Add による破損・二重報告を防ぐ)。
        private sealed class SavedSpectrumFileTracker
        {
            private readonly object _sync = new();
            private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

            public bool TryAdd(string fileName)
            {
                lock (_sync)
                    return _files.Add(fileName);
            }

            // 260717Claude: watchdog ログ用の保存済み本数。判定と snapshot は別ロック取得だが、ログ表示用途なので十分。
            public int Count
            {
                get
                {
                    lock (_sync)
                        return _files.Count;
                }
            }

            public IReadOnlySet<string> CreateSnapshot()
            {
                lock (_sync)
                    return new HashSet<string>(_files, StringComparer.OrdinalIgnoreCase);
            }
        }

        // 260528Codex: job 固有の進捗表示と spectrum 保存数をまとめ、外部実行処理を短く保ちます。
        private sealed class SimulationJobActivityTracker
        {
            private long _lastActivityUtcTicks = DateTime.UtcNow.Ticks;

            public TimeSpan IdleFor =>
                DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastActivityUtcTicks), DateTimeKind.Utc);

            public void MarkActivity() =>
                Volatile.Write(ref _lastActivityUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
