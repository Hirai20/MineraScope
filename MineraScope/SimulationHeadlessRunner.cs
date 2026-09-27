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
        // 260727Claude: ログ・スクリプトの既定保存先は DefaultStoragePaths に集約する (パスは従来と同一)。
        private static readonly string LogPath = Path.Combine(DefaultStoragePaths.LogsFolder, "headless-simulate.log");

        public static void Run(bool dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Log($"headless-simulate start mode={(dryRun ? "dryrun" : "run")}");

            var formMain = FormUserSettingsStore.Load<FormMainUserSettings>("FormMainSettings.json").Settings;
            // 260901Codex: 初回起動は GUI の Designer 既定 ON、保存済み旧設定の null は互換 OFF として解釈します。
            var generatorLoad = FormUserSettingsStore.Load<GeneratorFormUserSettings>("GeneratorFormSettings.json");
            // 260901Codex: 設定ファイルがない headless 初回実行も、GUI と同じ有効な数値条件で B を preview します。
            var generator = generatorLoad.HasStoredValues
                ? generatorLoad.Settings
                : GeneratorFormUserSettings.CreateInitialDefaults();
            bool useMeasurementTimePresetB = generatorLoad.HasStoredValues
                ? generator.MeasurementTimePresetBEnabled ?? false
                : true;
            // 260727Claude: headless では MessageBox を出せないので、壊れた設定の警告はログへ落とす。
            // 260728Claude: 警告はストアが溜めるので、両方の読み込み後にまとめて引き取る。
            foreach (string warning in FormUserSettingsStore.DrainWarnings())
                Log($"WARN: {SimulationRunLog.Flatten(warning)}");

            string outputFolder = Environment.GetEnvironmentVariable("MINERASCOPE_SIM_OUTPUT") is { Length: > 0 } overrideOutput
                ? overrideOutput
                : (string.IsNullOrWhiteSpace(formMain.EdxOutputPath) ? DefaultStoragePaths.TrainingDataFolder : formMain.EdxOutputPath);

            string scriptOutput = DefaultStoragePaths.PythonScriptsFolder;
            DetectorProfile detectorProfile = generator.GetDetectorProfile();

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
            // 260901Codex: B 選択時は GUI と同じ共通配分を渡し、OFF なら従来の単一測定時間を維持します。
            var request = new ModelCreationRequest(
                new ModelCreationPaths(
                    outputFolder.Trim(),
                    scriptOutput,
                    DtsaMsiInstallation.UseDefaultIfBlank(formMain.DtsaPath),
                    (formMain.ModelPath ?? string.Empty).Trim()),
                generator.ModelName.Trim(),
                new SemEdxCondition(
                    detectorProfile,
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
                selectedSolutions)
            {
                SpectrumTimeAllocations = useMeasurementTimePresetB
                    ? SpectrumTimeSchedule.MeasurementTimeB.CreateAllocations()
                    : [],
                // 260907Codex: Headless B must use the same cross-time composition plan as the GUI, including its read-only preview.
                SpectrumTimeSchedule = useMeasurementTimePresetB
                    ? SpectrumTimeSchedule.MeasurementTimeB
                    : null
            };

            Log($"output={outputFolder}");
            Log($"dtsa={request.Paths.DtsaFolder}");
            Log($"minerals={selectedSolutions.Length} ({string.Join(", ", selectedSolutions.Select(s => s.Name))})");
            // 260901Codex: 実行前ログで B と単一時間を区別し、同じ保存設定から条件を再現できるようにします。
            Log($"measurementTimes={string.Join(",", request.GetEffectiveSpectrumTimeAllocations().Select(allocation => $"{allocation.LiveTime.ToString(CultureInfo.InvariantCulture)}s×{allocation.TargetSpectrumCount}"))}");
            Log($"target={request.Simulation.TargetSpectrumCount} resolutionStep={request.Simulation.ResolutionStep.ToString(CultureInfo.InvariantCulture)} parallel={request.Simulation.ParallelCount} carbonJitter%={request.Simulation.CarbonThicknessJitterPercent.ToString(CultureInfo.InvariantCulture)}");

            // 260626Codex: Match the GUI-side dtsa2.msi validation before reserving/running spectra.
            string? dtsaValidationError = dryRun
                ? null
                : DtsaMsiInstallation.GetValidationError(request.Paths.DtsaFolder);
            if (dtsaValidationError is not null)
            {
                Log($"ERROR: {dtsaValidationError} Abort.");
                return;
            }

            var planBuilder = new SimulationPlanBuilder();
            var repository = new SpectrumPoolRepository(new SpectrumConditionKeyBuilder());
            var workflow = new SpectrumPoolWorkflow(repository, planBuilder);

            // 260901Codex: dry-run は全 condition の有効 Completed と実行規模を読むだけで、予約・復旧・manifest 保存を行いません。
            if (dryRun)
            {
                var poolStates = workflow.PreviewPoolStates(request);
                foreach (var state in poolStates
                    .OrderBy(item => item.LiveTime)
                    .ThenBy(item => item.MineralName, StringComparer.OrdinalIgnoreCase))
                    Log(
                        $"  preview {state.MineralName} {state.LiveTime.ToString(CultureInfo.InvariantCulture)}s: " +
                        $"completed={state.CompletedCount}/{state.RequiredCount} shortage={state.MissingCount} key={state.ConditionKey}");

                var shortagesPreview = poolStates.Where(item => item.MissingCount > 0).ToArray();
                var planPreview = planBuilder.CreatePreview(request, shortagesPreview);
                Log($"plan preview: batches={planPreview.BatchCount} jobs={planPreview.JobCount} spectra={planPreview.SpectrumCount}");
                Log(planPreview.SpectrumCount == 0
                    ? "nothing to generate (no shortage). done."
                    : "dryrun: DTSA-II は起動せず、予約・manifest 更新も行いません。");
                return;
            }

            // 260901Codex: Actual headless recovery and generation share the same cross-process exclusion as the GUI; dry-run stays lock-free and read-only.
            if (!SpectrumPoolMutationLease.TryAcquire(out var poolMutationLease, out string leaseFailureReason))
            {
                Log($"ERROR: {SimulationRunLog.Flatten(leaseFailureReason)} Abort.");
                return;
            }
            using var poolMutationLeaseScope = poolMutationLease;

            // 260901Codex: 実行モードだけ script フォルダを準備し、停止済み run の完成ファイルを予約前に回収します。
            Directory.CreateDirectory(scriptOutput);
            var recovery = workflow.RecoverPendingSpectra(request);
            Log(
                $"pending recovery: examined={recovery.ExaminedCount} recovered={recovery.RecoveredCount} " +
                $"invalid={recovery.InvalidCount} missingFile={recovery.MissingFileCount}");

            // 260902Codex: Headless generation also uses the restart fast path; full EMSA validation remains in training.
            var plan = workflow.CreateMissingSimulationPlan(
                request,
                out var shortages,
                validateCompletedSpectra: false);
            foreach (var shortage in shortages)
                Log(
                    $"  shortage {shortage.MineralName} {shortage.LiveTime.ToString(CultureInfo.InvariantCulture)}s: " +
                    $"completed={shortage.CompletedCount}/{shortage.RequiredCount} missing={shortage.MissingCount} key={shortage.ConditionKey}");

            int jobCount = plan.Batches.Sum(batch => batch.Jobs.Count);
            int reservedSpectrumCount = plan.Batches.SelectMany(batch => batch.Jobs).Sum(job => job.Reservations.Count);
            Log($"plan: batches={plan.Batches.Count} jobs={jobCount} reservedSpectra={reservedSpectrumCount}");

            if (plan.Batches.Count == 0)
            {
                Log("nothing to generate (no shortage). done.");
                return;
            }

            var executionService = new SimulationExecutionService(new SimulationScriptGenerator());
            var progress = new Progress<SimulationExecutionProgress>(ReportProgress);
            // 260901Codex: 鉱物 batch ごとに結果を原子的保存し、後続 batch 中の停止でも完了済み分を保持します。
            executionService.RunAsync(
                plan,
                progress,
                CancellationToken.None,
                (_, batchResults) =>
                {
                    workflow.ApplySimulationResults(batchResults);
                    return System.Threading.Tasks.Task.CompletedTask;
                }).GetAwaiter().GetResult();

            var counts = workflow.GetStatusCounts(request, validateCompletedSpectra: false);
            Log($"done: Completed={counts.Completed} Failed={counts.Failed} Missing={counts.Missing} Pending={counts.Pending}");

            // 260902Codex: Generation already validates newly saved EMSA files; avoid rereading the full reusable pool here.
            var remaining = workflow.GetShortages(request, validateCompletedSpectra: false);
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
