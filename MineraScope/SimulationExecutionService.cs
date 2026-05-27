using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260507Codex: DTSA-II スクリプト生成と外部実行を担当し、結果は manifest 更新側へ返します。
    internal sealed class SimulationExecutionService
    {
        // 260528Codex: DTSA-II script からの保存完了通知を識別し、生成ファイル数ベースの進捗へ変換します。
        private const string SavedSpectrumMarker = "MINERASCOPE_SPECTRUM_SAVED|";

        private readonly SimulationScriptGenerator _scriptGenerator;

        public SimulationExecutionService(SimulationScriptGenerator scriptGenerator)
        {
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
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

            for (int batchIndex = 0; batchIndex < plan.Batches.Count; batchIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var batch = plan.Batches[batchIndex];
                int batchNumber = batchIndex + 1;
                ReportBatchProgress(progressScope, batch, batchNumber, SimulationExecutionProgressKind.BatchStarted, "開始");

                var batchResults = await Task.WhenAll(batch.Jobs.Select(job =>
                    ExecuteJobAsync(job, batch.SolutionName, batchNumber, progressScope, cancellationToken)));
                results.AddRange(batchResults);

                ReportBatchProgress(progressScope, batch, batchNumber, SimulationExecutionProgressKind.BatchCompleted, "完了");
            }

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
        private async Task<SimulationExecutionResult> ExecuteJobAsync(
            SimulationExecutionJob job,
            string solutionName,
            int batchIndex,
            SimulationProgressScope progressScope,
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

                string? scriptFolder = Path.GetDirectoryName(job.ScriptPath);
                if (!string.IsNullOrWhiteSpace(scriptFolder))
                    Directory.CreateDirectory(scriptFolder);

                File.WriteAllText(job.ScriptPath, _scriptGenerator.Generate(job.Property, job.ParallelIndex));
                progress.ReportJobProgress(
                    SimulationExecutionProgressKind.ScriptWritten,
                    $"script 作成: {Path.GetFileName(job.ScriptPath)}");

                var result = await RunCommandAsync(job, progress, cancellationToken);
                progress.CompleteUnreportedSuccessfulSpectra(result);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var result = CreateCanceledResult(job, string.Empty, string.Empty);
                progress.ReportJobFinished(result, stopwatch.Elapsed);
                return result;
            }
            catch (Exception ex)
            {
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

        // 260528Codex: 標準出力を逐次読み、保存完了マーカーを spectrum 進捗へ即時反映します。
        private static async Task<SimulationExecutionResult> RunCommandAsync(
            SimulationExecutionJob job,
            SimulationJobProgress progress,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c java-runtime\\bin\\java -jar dtsa2-15.1.44.jar -- --script \"{job.ScriptPath}\"",
                WorkingDirectory = job.DtsaFolder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = new()
            {
                StartInfo = startInfo
            };

            process.Start();
            progress.ReportJobProgress(SimulationExecutionProgressKind.ProcessStarted, "DTSA-II process 起動");
            var standardOutputTask = ReadStandardOutputAsync(process, progress);
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                await WaitForProcessExitAfterKillAsync(process);
                string canceledOutput = await ReadProcessOutputAsync(standardOutputTask);
                string canceledError = await ReadProcessOutputAsync(standardErrorTask);
                return CreateCanceledResult(job, canceledOutput, canceledError);
            }

            string standardOutput = await ReadProcessOutputAsync(standardOutputTask);
            string standardError = await ReadProcessOutputAsync(standardErrorTask);

            return new SimulationExecutionResult(
                job.Reservations,
                process.ExitCode,
                standardOutput,
                standardError,
                ExceptionMessage: null);
        }

        // 260513Codex: キャンセル結果は Failed と区別し、manifest 側で Pending に戻せる印を付けます。
        private static SimulationExecutionResult CreateCanceledResult(
            SimulationExecutionJob job,
            string standardOutput,
            string standardError) =>
            new(
                job.Reservations,
                ExitCode: -1,
                StandardOutput: standardOutput,
                StandardError: standardError,
                ExceptionMessage: null,
                IsCanceled: true);

        // 260528Codex: DTSA-II スクリプトの保存完了マーカーを逐次読み、生成ファイル数ベースで進捗を進めます。
        private static async Task<string> ReadStandardOutputAsync(
            Process process,
            SimulationJobProgress progress)
        {
            var builder = new StringBuilder();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
            {
                builder.AppendLine(line);
                if (line.StartsWith(SavedSpectrumMarker, StringComparison.Ordinal))
                    progress.ReportSpectrumSaved();
            }

            return builder.ToString();
        }

        // 260513Codex: キャンセル要求後に残った子プロセスも含めて DTSA-II を止めます。
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

        // 260528Codex: 進捗全体の件数とスレッド間カウンタをまとめ、長い引数リレーを避けます。
        private sealed class SimulationProgressScope
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

        // 260528Codex: job 固有の進捗表示と spectrum 保存数をまとめ、外部実行処理を短く保ちます。
        private sealed class SimulationJobProgress
        {
            private readonly SimulationProgressScope _scope;
            private readonly SimulationExecutionJob _job;
            private int _reportedSpectrumCount;

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

            public void ReportSpectrumSaved()
            {
                if (!TryMarkSpectrumSaved())
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

                while (Volatile.Read(ref _reportedSpectrumCount) < _job.Reservations.Count)
                    ReportSpectrumSaved();
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

            private bool TryMarkSpectrumSaved()
            {
                int reportedCount = Interlocked.Increment(ref _reportedSpectrumCount);
                if (reportedCount <= _job.Reservations.Count)
                {
                    _scope.MarkSpectrumCompleted();
                    return true;
                }

                Interlocked.Decrement(ref _reportedSpectrumCount);
                return false;
            }
        }
    }
}
