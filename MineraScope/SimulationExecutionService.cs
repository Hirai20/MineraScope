using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260507Codex: DTSA-II スクリプト生成と外部実行を担当し、結果は manifest 更新側へ返します。
    internal sealed class SimulationExecutionService
    {
        private readonly SimulationScriptGenerator _scriptGenerator;

        public SimulationExecutionService(SimulationScriptGenerator scriptGenerator)
        {
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
        }

        // 260513Codex: batch 内は並列実行し、キャンセル時は後続 batch を起動せず実行済み結果だけを返します。
        public async Task<IReadOnlyList<SimulationExecutionResult>> RunAsync(
            SimulationExecutionPlan plan,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var results = new List<SimulationExecutionResult>();
            foreach (SimulationExecutionBatch batch in plan.Batches)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var batchResults = await Task.WhenAll(batch.Jobs.Select(job => ExecuteJobAsync(job, cancellationToken)));
                results.AddRange(batchResults);
            }

            return results;
        }

        // 260513Codex: スクリプト生成、古い出力削除、DTSA-II 起動、結果収集を 1 ジョブ単位で非同期に完結させます。
        private async Task<SimulationExecutionResult> ExecuteJobAsync(
            SimulationExecutionJob job,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(job.Property.OutputFolder);
                DeleteReservedOutputFiles(job.Reservations);

                string? scriptFolder = Path.GetDirectoryName(job.ScriptPath);
                if (!string.IsNullOrWhiteSpace(scriptFolder))
                {
                    Directory.CreateDirectory(scriptFolder);
                }

                File.WriteAllText(job.ScriptPath, _scriptGenerator.Generate(job.Property, job.ParallelIndex));
                return await RunCommandAsync(job, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateCanceledResult(job, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                return new SimulationExecutionResult(
                    job.Reservations,
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    ExceptionMessage: ex.Message);
            }
        }

        // 260513Codex: 再生成時に同じ fileName を使うため、実行直前に対象 spectrum ファイルだけを消します。
        private static void DeleteReservedOutputFiles(IReadOnlyList<SpectrumSimulationReservation> reservations)
        {
            foreach (var reservation in reservations)
            {
                string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        // 260513Codex: 標準出力と標準エラーを並行して読み、キャンセル時は DTSA-II のプロセスツリーを停止します。
        private static async Task<SimulationExecutionResult> RunCommandAsync(
            SimulationExecutionJob job,
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
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
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

        // 260513Codex: キャンセル要求後に残った子プロセスも含めて DTSA-II を止めます。
        private static void KillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
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
    }
}
