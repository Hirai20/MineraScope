using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        // 260507Codex: batch 内は並列実行し、各ジョブの終了コードと出力を呼び出し元へ返します。
        public async Task<IReadOnlyList<SimulationExecutionResult>> RunAsync(SimulationExecutionPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var results = new List<SimulationExecutionResult>();
            foreach (SimulationExecutionBatch batch in plan.Batches)
            {
                var batchResults = await Task.WhenAll(batch.Jobs.Select(job => Task.Run(() => ExecuteJob(job))));
                results.AddRange(batchResults);
            }

            return results;
        }

        // 260507Codex: スクリプト生成、DTSA-II 起動、結果収集を 1 ジョブ単位で完結させます。
        private SimulationExecutionResult ExecuteJob(SimulationExecutionJob job)
        {
            try
            {
                Directory.CreateDirectory(job.Property.OutputFolder);
                string? scriptFolder = Path.GetDirectoryName(job.ScriptPath);
                if (!string.IsNullOrWhiteSpace(scriptFolder))
                {
                    Directory.CreateDirectory(scriptFolder);
                }

                File.WriteAllText(job.ScriptPath, _scriptGenerator.Generate(job.Property, job.ParallelIndex));
                return RunCommand(job);
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

        // 260507Codex: 標準出力・標準エラー・終了コードを明示的に取得して失敗理由へ使えるようにします。
        private static SimulationExecutionResult RunCommand(SimulationExecutionJob job)
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
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new SimulationExecutionResult(
                job.Reservations,
                process.ExitCode,
                standardOutput,
                standardError,
                ExceptionMessage: null);
        }
    }
}
