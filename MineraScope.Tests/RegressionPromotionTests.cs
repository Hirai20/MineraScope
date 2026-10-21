using System.Text.Json;

namespace MineraScope.Tests
{
    // 261021Codex: 合成データで定量の部分省略と分類器だけの上書き保存を検証する。
    internal static class RegressionPromotionTests
    {
        public static void RunNativeSmoke()
        {
            using var fixture = new TestFixture(new SpectrumTimeSchedule(200, [new(50, 1)]),
                TestFixture.CreateSolution("First"), TestFixture.CreateSolution("Second"));
            foreach (string variable in new[] { "TF_NUM_INTRAOP_THREADS", "TF_NUM_INTEROP_THREADS", "OMP_NUM_THREADS" })
                Environment.SetEnvironmentVariable(variable, "2");
            fixture.Complete(TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _)));
            var pools = fixture.Workflow.CreateTrainingPools(fixture.Request, out var shortages);
            TestAssert.Equal(0, shortages.Count, "Synthetic simulation inputs are complete.");
            var workflow = new ModelTrainingWorkflow(new DeepLearning(Console.WriteLine), Console.WriteLine);
            var partialPools = pools.Select(pool => pool.MineralName == "Second"
                ? pool with { Samples = pool.Samples.Take(100).ToArray() } : pool).ToArray();
            // 260930Codex: ネイティブSavedModelの一時パスをWindowsのパス長上限内に保つ。
            string modelRoot = Path.Combine(Path.GetTempPath(), "MSR_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var plan = new ModelTrainingPlan(Path.Combine(modelRoot, "M"), partialPools,
                    fixture.Request.Training with { BatchSize = 128 }, fixture.Request.SemEdxCondition.GetDetectorProfile(), false);
                TestAssert.True(workflow.Validate(plan) is null, "Classification passes while Second regression cannot form a batch.");
                var partial = workflow.RunAsync(plan).GetAwaiter().GetResult();
                TestAssert.Equal(ModelTrainingStatus.Promoted, partial.Status, "Classifier and First regression promote.");
                TestAssert.True(Directory.Exists(Path.Combine(plan.ModelOutputFolder, "First" + ModelArtifactPaths.RegressionFolderSuffix)),
                    "A trainable regression is saved.");
                TestAssert.True(!Directory.Exists(Path.Combine(plan.ModelOutputFolder, "Second" + ModelArtifactPaths.RegressionFolderSuffix)),
                    "The insufficient regression is never saved.");

                var shortPools = pools.Select(pool => pool with { Samples = pool.Samples.Take(100).ToArray() }).ToArray();
                plan = plan with { TrainingPools = shortPools, AllowOverwriteExistingModel = true };
                var onlyClassifier = workflow.RunAsync(plan).GetAwaiter().GetResult();
                TestAssert.Equal(ModelTrainingStatus.Promoted, onlyClassifier.Status, "All regressions may be omitted.");
                TestAssert.True(onlyClassifier.CompletionMessage!.StartsWith("分類モデルのみ作成しました。", StringComparison.Ordinal),
                    "Native completion carries the classifier-only notice.");
                foreach (var pool in pools)
                    TestAssert.True(!Directory.Exists(Path.Combine(plan.ModelOutputFolder, pool.MineralName + ModelArtifactPaths.RegressionFolderSuffix)),
                        "Overwrite must not retain a stale regression from the previous model.");
                var catalog = new ModelCatalog();
                catalog.Update(modelRoot);
                TestAssert.True(catalog.ModelNames.Contains("M"),
                    "The classifier-only model remains selectable after promotion.");
                string resultPath = Path.Combine(plan.ModelOutputFolder, ModelArtifactPaths.TrainingResultsFileName);
                string before = File.ReadAllText(resultPath);
                using var saved = JsonDocument.Parse(before);
                TestAssert.Equal(2, saved.RootElement.GetProperty("models").EnumerateArray()
                    .Count(model => model.GetProperty("status").GetString() == "skipped"), "Both omissions are persisted.");
                // 260930Codex: ワークフローの事前検査は不正計画を拒否し、学習処理の直接呼び出しは型付きの件数不足結果を返す。
                TestAssert.Throws<InvalidOperationException>(() => workflow.RunAsync(
                    plan with { Settings = plan.Settings with { BatchSize = 161 } }).GetAwaiter().GetResult(),
                    "Workflow refuses insufficient classification before training.");
                string directOutput = Path.Combine(modelRoot, "direct");
                Directory.CreateDirectory(directOutput);
                var refused = new DeepLearning(Console.WriteLine).RunTraining(shortPools, 1, 161, 1, .2f, 1.5, directOutput);
                TestAssert.Equal(DeepLearningTrainingStatus.NotCompleted, refused.Status, "Untrainable classification is never completed.");
                TestAssert.Equal(DeepLearningTrainingFailureKind.InsufficientTrainingSamples, refused.FailureKind!.Value,
                    "Count failure remains distinct from load failures.");
                TestAssert.Equal(before, File.ReadAllText(resultPath), "Classification refusal preserves the existing model.");
                // 260930Codex: スペクトル欠損は任意定量の省略として扱わず、処理を中断する。
                File.Delete(shortPools[0].Samples[0].FilePath);
                var unreadable = workflow.RunAsync(plan).GetAwaiter().GetResult();
                TestAssert.Equal(ModelTrainingFailureKind.TrainingDataLoadFailed, unreadable.FailureKind!.Value,
                    "Unreadable input remains a load failure.");
                TestAssert.Equal(before, File.ReadAllText(resultPath), "Load failure preserves the previously saved model.");
            }
            finally
            {
                // 260930Codex: このテストが所有するTEMP直下の正確な対象だけを削除する。
                string resolved = Path.GetFullPath(modelRoot);
                TestAssert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(resolved), "Native output must remain an immediate TEMP child.");
                TestAssert.True(Path.GetFileName(resolved).StartsWith("MSR_", StringComparison.Ordinal), "Native output must have the test prefix.");
                if (Directory.Exists(resolved))
                    Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
