// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 261103Codex: 明示起動だけで実DTSA-IIを比較する。保存設定を変更せず、新規の専用poolだけへ生成する。
    internal static class SimulationExecutionBenchmark
    {
        // 261103Codex: 基準poolの予約だけを複製できるようにし、新規poolごとの乱択を速度比較から除く。
        public static void Run(string outputFolder, string? referenceFolder = null)
        {
            string root = Path.GetFullPath(outputFolder);
            if (Directory.Exists(root) || File.Exists(root))
                throw new InvalidOperationException("Benchmark output must be a new directory: " + root);
            if (!SpectrumPoolMutationLease.TryAcquire(out var lease, out string reason))
                throw new InvalidOperationException(reason);
            using var poolLease = lease;

            var main = FormUserSettingsStore.Load<FormMainUserSettings>("FormMainSettings.json").Settings;
            var generatorLoad = FormUserSettingsStore.Load<GeneratorFormUserSettings>("GeneratorFormSettings.json");
            var generator = GeneratorFormUserSettings.FromLoad(generatorLoad);
            var registry = DetectorProfileRegistry.Load(generatorLoad);
            if (!registry.TryResolve(generator.GetSelectedDetectorName(), out var detector, out string detectorError))
                throw new InvalidOperationException(detectorError);
            var database = new MineralDatabaseRepository(AppContext.BaseDirectory).Load();
            var solutions = new[] { "Olivine", "Pyroxene" }
                .Select(name => database.Single(solution => solution.Name == name)).ToArray();
            var request = new ModelCreationRequest(
                new ModelCreationPaths(Path.Combine(root, "pool"), Path.Combine(root, "scripts"),
                    DtsaInstallation.ResolveFolder(main.DtsaPath), Path.Combine(root, "models")),
                "SimulationBenchmark",
                new SemEdxCondition(detector, generator.CarbonThickness, generator.BeamEnergy, generator.LiveTime, generator.ProbeCurrent),
                // 261103Codex: 前後とも2鉱物×100本、保存済みの並列数・組成刻み・物理条件を使う。
                new SimulationExecutionSettings(100, generator.Resolution / 100, (int)generator.ParallelCount),
                new ModelTrainingSettings(1, 4, 1, 0.2f, 1.5), solutions);
            string? installationError = DtsaInstallation.GetValidationError(request.Paths.DtsaFolder);
            if (installationError is not null)
                throw new InvalidOperationException(installationError);

            var repository = new SpectrumPoolRepository(new SpectrumConditionKeyBuilder());
            // 261103Codex: スペクトルをコピーせず、基準の予約ID・組成・条件を新規poolのPendingへ複製する。
            if (referenceFolder is not null)
                CopyReferenceReservations(Path.Combine(Path.GetFullPath(referenceFolder), "pool"),
                    request.Paths.SpectrumOutputFolder, repository, solutions.Length);
            var workflow = new SpectrumPoolWorkflow(repository, new SimulationPlanBuilder());
            var plan = workflow.CreateMissingSimulationPlan(request, out _);
            var reservations = TestFixture.Reservations(plan);
            // 261103Codex: 出力root以外の予約情報を保存し、実行前後で同じ計算を要求したことを照合できるようにする。
            string planJson = CreatePlanSnapshot(request, plan);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "benchmark-plan.json"), planJson, new UTF8Encoding(false));
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planJson)));
            // 261103Codex: 予約の完全一致を実プロセス起動より前に検査する。GUI設定が変わっていた場合もここで止める。
            if (referenceFolder is not null)
                TestAssert.Equal(File.ReadAllText(Path.Combine(referenceFolder, "benchmark-plan.json"), Encoding.UTF8), planJson,
                    "Benchmark conditions, compositions, reservation order, and job partitions must exactly match the reference.");
            Console.WriteLine($"Benchmark: output={root} spectra={reservations.Length} planSha256={digest}");
            var previousLogs = Directory.Exists(DefaultStoragePaths.LogsFolder)
                ? Directory.GetFiles(DefaultStoragePaths.LogsFolder, "simulate-*.log").ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                var results = new SimulationExecutionService(new SimulationScriptGenerator()).RunAsync(
                    plan, null, cancellation.Token,
                    (batch, batchResults) =>
                    {
                        workflow.ApplySimulationResults(batchResults);
                        Console.WriteLine($"Checkpoint: {batch.SolutionName} jobs={batchResults.Count}");
                        return Task.CompletedTask;
                    }).GetAwaiter().GetResult();
                var counts = workflow.GetStatusCounts(request, validateCompletedSpectra: false);
                string[] logs = Directory.GetFiles(DefaultStoragePaths.LogsFolder, "simulate-*.log")
                    .Where(path => !previousLogs.Contains(path)).ToArray();
                string resultJson = JsonSerializer.Serialize(new
                {
                    PlanSha256 = digest,
                    Counts = counts,
                    FailedJobs = results.Count(result => result.ExitCode != 0 || result.ExceptionMessage is not null),
                    Logs = logs
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(root, "benchmark-result.json"), resultJson, new UTF8Encoding(false));
                Console.WriteLine(resultJson);
                cancellation.Token.ThrowIfCancellationRequested();
                TestAssert.Equal(reservations.Length, counts.Completed, "All benchmark spectra must pass the existing manifest validation.");
                TestAssert.True(results.All(result => result.ExitCode == 0 && result.ExceptionMessage is null),
                    "All benchmark jobs must succeed; inspect the simulate log on failure.");
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }

        // 261104Codex: 比較元は読み取りだけとし、別poolへ予約だけを再現する操作を通常テストでも検証する。
        internal static void CopyReferenceReservations(string sourcePool, string targetPool, SpectrumPoolRepository repository, int mineralCount)
        {
            string[] manifests = Directory.GetFiles(sourcePool, "manifest.json", SearchOption.AllDirectories);
            TestAssert.Equal(mineralCount, manifests.Length, "The reference benchmark must contain exactly the selected mineral pools.");
            foreach (string source in manifests)
            {
                var manifest = repository.Load(source) ?? throw new InvalidDataException("Invalid reference manifest: " + source);
                foreach (var entry in manifest.Spectra)
                {
                    entry.Status = SpectrumManifestStatus.Pending;
                    entry.FailureReason = null;
                    entry.LastFailureKind = null;
                }
                repository.Save(Path.Combine(targetPool, Path.GetRelativePath(sourcePool, source)), manifest);
            }
        }

        // 261104Codex: 出力rootと実行順以外の要求を完全比較できる、同一のsnapshot形式を共用する。
        internal static string CreatePlanSnapshot(ModelCreationRequest request, SimulationExecutionPlan plan) =>
            JsonSerializer.Serialize(new
            {
                request.SemEdxCondition,
                request.Simulation,
                Jobs = plan.Batches.SelectMany(batch => batch.Jobs.Select(job => new
                {
                    batch.SolutionName,
                    Script = Path.GetFileName(job.ScriptPath),
                    job.ParallelIndex,
                    Reservations = job.Reservations.Select(reservation => new
                    {
                        reservation.SimulationId,
                        reservation.FileName,
                        reservation.ConditionKey,
                        Composition = reservation.CompositionWeights.Select(weight => new { weight.ElementName, weight.Weight })
                    })
                }))
            }, new JsonSerializerOptions { WriteIndented = true });
    }
}
