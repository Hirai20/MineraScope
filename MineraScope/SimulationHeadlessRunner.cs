using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MineraScope
{
    // 260623Claude: 【デバッグ・開発専用】GUI のボタン操作なしで spectrum 生成を回すヘッドレス実行。
    //   エンドユーザー向けの機能ではなく、UI からは一切呼ばれない。開発者が PC の前にいないときなどに、
    //   環境変数で起動して生成だけ走らせるための補助。製品挙動には影響しない (環境変数が無ければ通常の GUI 起動)。
    //   保存済み UI 設定 (FormMainSettings.json / GeneratorFormSettings.json) を GeneratorForm.CreateModelCreationRequest と
    //   同じ組み立てで読み、不足 spectrum だけを既存フロー (SpectrumPoolWorkflow + SimulationExecutionService) で生成する。
    //   manifest の Completed は再利用され、Failed/Pending は再試行されるため、途中まで終わった鉱物は不足分だけ補充される。
    //   環境変数:
    //     MINERASCOPE_HEADLESS_SIMULATE = "dryrun"            -> DTSA-II を起動せず、対象鉱物と不足件数・conditionKey 一致だけ報告
    //     MINERASCOPE_HEADLESS_SIMULATE = "1" / "run"         -> 実生成
    //     MINERASCOPE_SIM_OUTPUT        = <folder>            -> spectrum 出力先 (省略時は保存設定の EdxOutputPath)
    //     MINERASCOPE_SIM_MINERAL       = <substring>         -> 鉱物名の部分一致で対象を絞る (省略時は DB 全鉱物)
    internal static class SimulationHeadlessRunner
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MineraScope",
            "Logs",
            "headless-simulate.log");

        public static void Run(bool dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Log($"headless-simulate start mode={(dryRun ? "dryrun" : "run")}");

            var formMain = FormUserSettingsStore.Load<FormMainUserSettings>("FormMainSettings.json");
            var generator = FormUserSettingsStore.Load<GeneratorFormUserSettings>("GeneratorFormSettings.json");

            string outputFolder = Environment.GetEnvironmentVariable("MINERASCOPE_SIM_OUTPUT") is { Length: > 0 } overrideOutput
                ? overrideOutput
                : (string.IsNullOrWhiteSpace(formMain.EdxOutputPath) ? DefaultStoragePaths.TrainingDataFolder : formMain.EdxOutputPath);

            string scriptOutput = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MineraScope",
                "PythonScripts");
            Directory.CreateDirectory(scriptOutput);

            string assemblyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;
            var allSolutions = new MineralDatabaseRepository(assemblyPath).Load();

            string? mineralFilter = Environment.GetEnvironmentVariable("MINERASCOPE_SIM_MINERAL");
            var selectedSolutions = string.IsNullOrWhiteSpace(mineralFilter)
                ? allSolutions
                : allSolutions
                    .Where(solution => solution.Name.Contains(mineralFilter, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (selectedSolutions.Length == 0)
            {
                Log($"ERROR: no mineral matched filter '{mineralFilter}'. Abort.");
                return;
            }

            // 260623Claude: GeneratorForm.CreateModelCreationRequest と同じ順序・換算 (Resolution/100, ValidationSplit/100) で request を作る。
            var request = new ModelCreationRequest(
                new ModelCreationPaths(
                    outputFolder.Trim(),
                    scriptOutput,
                    DtsaMsiInstallation.UseDefaultIfBlank(formMain.DtsaPath),
                    (formMain.ModelPath ?? string.Empty).Trim()),
                generator.ModelName.Trim(),
                new SemEdxCondition(
                    generator.DetectorName.Trim(),
                    generator.CarbonThickness,
                    generator.BeamEnergy,
                    generator.LiveTime,
                    generator.ProbeCurrent),
                new SimulationExecutionSettings(
                    (int)generator.TargetSpectrumCount,
                    generator.Resolution / 100,
                    (int)generator.ParallelCount,
                    generator.CarbonThicknessJitterPercent),
                new ModelTrainingSettings(
                    (int)generator.Epochs,
                    (int)generator.BatchSize,
                    (int)generator.EarlyStopping,
                    (float)generator.ValidationSplit / 100f,
                    generator.UnknownDistanceScale),
                selectedSolutions);

            Log($"output={outputFolder}");
            Log($"dtsa={request.Paths.DtsaFolder}");
            Log($"minerals={selectedSolutions.Length} ({string.Join(", ", selectedSolutions.Select(s => s.Name))})");
            Log($"target={request.Simulation.TargetSpectrumCount} resolutionStep={request.Simulation.ResolutionStep.ToString(CultureInfo.InvariantCulture)} parallel={request.Simulation.ParallelCount} carbonJitter%={request.Simulation.CarbonThicknessJitterPercent.ToString(CultureInfo.InvariantCulture)}");

            // 260626Codex: Match the GUI-side dtsa2.msi validation before reserving/running spectra.
            if (!dryRun && !DtsaMsiInstallation.IsUsableInstallFolder(request.Paths.DtsaFolder))
            {
                Log($"ERROR: {DtsaMsiInstallation.NotFoundMessage} Abort.");
                return;
            }

            var planBuilder = new SimulationPlanBuilder();
            var repository = new SpectrumPoolRepository(new SpectrumConditionKeyBuilder());
            var workflow = new SpectrumPoolWorkflow(repository, planBuilder);

            // 260623Claude: dryrun では既存プール再利用 (conditionKey 一致) を確認するため、解決先フォルダと Completed 件数を鉱物ごとに出す。
            foreach (var solution in selectedSolutions)
            {
                var handle = repository.ResolvePool(outputFolder, solution, request.Simulation.ResolutionStep, request.SemEdxCondition);
                var manifest = repository.Load(handle.ManifestPath);
                int completed = manifest?.Spectra.Count(e => e.Status == SpectrumManifestStatus.Completed) ?? 0;
                Log($"  resolve {solution.Name}: poolExists={Directory.Exists(handle.PoolFolder)} completed={completed} key={handle.ConditionKey}");
                Log($"    folder={handle.PoolFolder}");
            }

            var plan = workflow.CreateMissingSimulationPlan(request, out var shortages);
            foreach (var shortage in shortages)
                Log($"  shortage {shortage.MineralName}: completed={shortage.CompletedCount}/{shortage.RequiredCount} missing={shortage.MissingCount}");

            int jobCount = plan.Batches.Sum(batch => batch.Jobs.Count);
            int reservedSpectrumCount = plan.Batches.SelectMany(batch => batch.Jobs).Sum(job => job.Reservations.Count);
            Log($"plan: batches={plan.Batches.Count} jobs={jobCount} reservedSpectra={reservedSpectrumCount}");

            if (plan.Batches.Count == 0)
            {
                Log("nothing to generate (no shortage). done.");
                return;
            }

            if (dryRun)
            {
                Log("dryrun: DTSA-II は起動しません。上記の reservedSpectra 件を本実行で生成します。");
                return;
            }

            var executionService = new SimulationExecutionService(new SimulationScriptGenerator());
            var progress = new Progress<SimulationExecutionProgress>(ReportProgress);
            var results = executionService.RunAsync(plan, progress, CancellationToken.None).GetAwaiter().GetResult();

            workflow.ApplySimulationResults(results);

            var counts = workflow.GetStatusCounts(request);
            Log($"done: Completed={counts.Completed} Failed={counts.Failed} Missing={counts.Missing} Pending={counts.Pending}");

            var remaining = workflow.GetShortages(request);
            int remainingMissing = remaining.Sum(s => s.MissingCount);
            Log(remainingMissing == 0
                ? "spectrum pool そろいました。"
                : $"まだ不足 {remainingMissing} 件あります (再実行で続行できます)。");
        }

        // 260623Claude: 進捗は完了ジョブ・保存スペクトル・失敗だけ拾い、ログを埋め尽くさない。
        private static void ReportProgress(SimulationExecutionProgress progress)
        {
            switch (progress.Kind)
            {
                case SimulationExecutionProgressKind.SpectrumSaved:
                    Log($"  spectrum {progress.CompletedSpectrumCount}/{progress.TotalSpectrumCount} ({progress.SolutionName})");
                    break;
                case SimulationExecutionProgressKind.JobCompleted:
                    Log($"  job {progress.CompletedJobCount}/{progress.TotalJobCount} done ({progress.SolutionName})");
                    break;
                case SimulationExecutionProgressKind.JobFailed:
                    Log($"  job FAILED ({progress.SolutionName}) exit={progress.ExitCode} {progress.Message}");
                    break;
                case SimulationExecutionProgressKind.JobCanceled:
                    Log($"  job canceled ({progress.SolutionName})");
                    break;
            }
        }

        private static void Log(string message)
        {
            string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:O}\t{message}");
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
