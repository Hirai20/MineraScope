using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260416Codex: Move DTSA-II execution out of the form so UI code only orchestrates workflows.
    internal sealed class SimulationExecutionService
    {
        // 260416Codex: Keep script generation injectable so execution can stay focused on file and process work.
        private readonly SimulationScriptGenerator _scriptGenerator;

        // 260416Codex: Validate constructor dependencies once so later execution paths can stay simple.
        public SimulationExecutionService(SimulationScriptGenerator scriptGenerator)
        {
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
        }

        // 260416Codex: Execute one batch at a time while still running jobs inside the batch in parallel.
        public async Task RunAsync(SimulationExecutionPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            foreach (SimulationExecutionBatch batch in plan.Batches)
            {
                // 260416Codex: Wait on the projected tasks directly to avoid a throwaway local array variable.
                await Task.WhenAll(batch.Jobs.Select(job => Task.Run(() => ExecuteJob(job))));
            }
        }

        // 260416Codex: Each job always follows the same three steps, so keep the execution path flat.
        private void ExecuteJob(SimulationExecutionJob job)
        {
            Directory.CreateDirectory(job.Property.OutPutFolder);
            File.WriteAllText(job.ScriptPath, _scriptGenerator.Generate(job.Property, job.ParallelIndex));
            RunCommand(job.DtsaFolder, job.ScriptPath);
        }

        // 260416Codex: Build the DTSA-II command in place so the process setup stays easy to scan.
        private static void RunCommand(string dtsaPath, string scriptPath)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c cd /d \"{dtsaPath}\" && java-runtime\\bin\\java -jar dtsa2-15.1.44.jar -- --script \"{scriptPath}\"",
                WindowStyle = ProcessWindowStyle.Minimized
            };

            using Process? process = Process.Start(startInfo);
            process?.WaitForExit();
        }
    }
}
