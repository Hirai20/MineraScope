using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 260917Codex: テスト専用の合成TEMPデータだけを使い、マネージド子プロセス間で実際のOS共有動作を検証する。
    internal static class ConcurrencyTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            string poolLock = Path.Combine(fixture.Root, "pool.lock");
            // 読み取りを先に行う場合は、存在しないロックを読み取りアクセスで作成する動作も確認する。
            using (SpectrumPoolMutationLease.OpenReader(poolLock))
            {
                Probe("reader", poolLock, true);
                Probe("writer", poolLock, true);
            }
            using (SpectrumPoolMutationLease.OpenWriter(poolLock))
            {
                Probe("reader", poolLock, true);
                Probe("writer", poolLock, false);
                // 260917Codex: スナップショットを、意図的にロックするOS調停用ファイルから分離する。
                using var poolFixture = new TestFixture();
                var reservations = TestFixture.Reservations(poolFixture.Workflow.CreateMissingSimulationPlan(poolFixture.Request, out _));
                poolFixture.Complete(reservations);
                var before = poolFixture.Snapshot();
                using var reader = SpectrumPoolMutationLease.OpenReader(poolLock);
                var workflow = new SpectrumPoolWorkflow(poolFixture.Repository, new SimulationPlanBuilder(), persistManifestRepairs: false);
                TestAssert.Equal(1, workflow.PrepareTrainingPools(poolFixture.Request).Pools.Count,
                    "Published input must be usable while the producer holds its lease.");
                TestAssert.SequenceEqual(before, poolFixture.Snapshot(), "Concurrent preparation must not write pool state.");
            }
            using (new FileStream(poolLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Probe("reader", poolLock, false);
                Probe("writer", poolLock, false);
            }
            Probe("writer", poolLock, true, abandon: true);
            using (SpectrumPoolMutationLease.OpenWriter(poolLock)) { }

            string model = Path.Combine(fixture.Root, "model");
            using (PathOperationLease.Acquire(model, "ModelOutput"))
            {
                Probe("model", model.ToUpperInvariant() + Path.DirectorySeparatorChar, false);
                Probe("model", Path.Combine(model, "..", "model"), false);
                Probe("model", Path.Combine(fixture.Root, "other-model"), true);
            }
            Probe("model", model, true);
            Probe("model", model, true, abandon: true);
            using (PathOperationLease.Acquire(model, "ModelOutput")) { }
            VerifyPublication(fixture.Root);
            VerifyCatalog(fixture.Root);
        }

        // 260917Codex: 読み取り側がスナップショットを開き続ける間に完全なJSONを多数公開し、不完全なJSONが読まれないことを確認する。
        private static void VerifyPublication(string root)
        {
            string path = Path.Combine(root, "publication.json");
            DurableJsonFile.Replace(path, JsonSerializer.Serialize(new[] { 0, 0 }));
            var writer = Task.Run(() =>
            {
                for (int version = 2; version < 102; version++)
                    DurableJsonFile.Replace(path, JsonSerializer.Serialize(new[] { version, version }));
            });
            try
            {
                for (int iteration = 0; iteration < 300; iteration++)
                {
                    int[] values = JsonSerializer.Deserialize<int[]>(DurableJsonFile.ReadSnapshot(path))!;
                    TestAssert.True(values.Length == 2 && values[0] == values[1], "Snapshots must contain one complete version.");
                }
            }
            finally
            {
                writer.GetAwaiter().GetResult();
            }
        }

        // 260917Codex: 別アプリによる一覧更新でも、未完成のモデルや昇格時のバックアップを表示しない。
        private static void VerifyCatalog(string root)
        {
            string modelRoot = Path.Combine(root, "catalog");
            // 260930Codex: 共通カタログAPIで、接頭辞のない作業用フォルダーと通常のバックアップに似た名前も検証する。
            string[] visible = ["finished", "user.previous", "user.tmp"];
            string[] hidden = [$".{Guid.NewGuid():N}.tmp", $"active.{Guid.NewGuid():N}.tmp", $"old.{Guid.NewGuid():N}.previous"];
            foreach (string name in visible.Concat(hidden))
                Directory.CreateDirectory(Path.Combine(modelRoot, name));
            var catalog = new ModelCatalog();
            catalog.Update(modelRoot);
            TestAssert.SequenceEqual(visible, catalog.ModelNames,
                "Hide only private staging and backup names, preserving ordinary model names.");
        }

        // 260917Codex: 子プロセスの寿命をテスト側で制限・管理し、アプリ、DTSA、TensorFlowのプロセスは起動しない。
        private static void Probe(string kind, string path, bool acquired, bool abandon = false)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string argument in new[] { "--concurrency-child", kind, path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start concurrency test child.");
            try
            {
                string? state = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                TestAssert.Equal(acquired ? "ACQUIRED" : "BLOCKED", state, $"Cross-process {kind} lease result.");
                if (abandon)
                    process.Kill();
                else if (acquired)
                    process.StandardInput.WriteLine("release");
                TestAssert.True(process.WaitForExit(15000), "Child must exit after release.");
                if (!abandon)
                    TestAssert.Equal(0, process.ExitCode, process.StandardError.ReadToEnd());
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(15000);
                }
            }
        }

        // 260917Codex: テスト専用の入口でも、本番ワークフローと同じハンドル・ミューテックス実装を使う。
        public static int RunChild(string kind, string path)
        {
            IDisposable lease;
            try
            {
                lease = kind switch
                {
                    "reader" => SpectrumPoolMutationLease.OpenReader(path),
                    "writer" => SpectrumPoolMutationLease.OpenWriter(path),
                    "model" => PathOperationLease.Acquire(path, "ModelOutput"),
                    _ => throw new ArgumentException("Unknown lease kind.")
                };
            }
            catch (IOException)
            {
                Console.WriteLine("BLOCKED");
                return 0;
            }
            using (lease)
            {
                Console.WriteLine("ACQUIRED");
                Console.ReadLine();
            }
            return 0;
        }
    }
}
