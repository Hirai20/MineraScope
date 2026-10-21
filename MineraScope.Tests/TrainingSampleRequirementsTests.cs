using System.Text.Json;

namespace MineraScope.Tests
{
    // 261016Codex: 公開版の分類開始条件を検証し、定量省略の方針は追加しない。
    // 261021Claude: ユーザー指定により、非公開版と同じ定量省略と完了通知を公開版にも適用し、その保存を検証する。
    internal static class TrainingSampleRequirementsTests
    {
        public static void Run()
        {
            var (training, validation) = TrainingSampleRequirements.GetSplitCounts(100, .2f);
            TestAssert.Equal(80, training, "学習件数は従来の分割と同じ。");
            TestAssert.Equal(20, validation, "検証件数は従来の分割と同じ。");
            using var fixture = new TestFixture();
            var samples = Enumerable.Range(0, 100).Select(index => new SpectrumTrainingSample(
                Path.Combine(fixture.Root, $"{index}.emsa"), new Dictionary<string, double> { ["First"] = 1d })).ToArray();
            var pools = new[] { new SpectrumTrainingPool("First", ["First"], samples),
                new SpectrumTrainingPool("Second", ["Second"], samples) };
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
            var plan = new ModelTrainingPlan(Path.Combine(fixture.Root, "model"), pools,
                fixture.Request.Training with { BatchSize = 160 }, fixture.Request.SemEdxCondition.GetDetectorProfile(), false);
            TestAssert.True(workflow.Validate(plan) is null, "分類でちょうど1バッチ確保できれば受理する。");
            var invalidPlans = new[]
            {
                plan with { TrainingPools = [] }, plan with { TrainingPools = [pools[0]] },
                plan with { TrainingPools = [pools[0], pools[0]] },
                plan with { Settings = plan.Settings with { BatchSize = 161 } },
                plan with { Settings = plan.Settings with { BatchSize = 0 } },
                plan with { Settings = plan.Settings with { ValidationSplit = .001f } },
                plan with { Settings = plan.Settings with { ValidationSplit = float.NaN } },
                plan with { Settings = plan.Settings with { ValidationSplit = 1f } }
            };
            foreach (var invalid in invalidPlans)
            {
                TestAssert.True(workflow.Validate(invalid) is not null, "学習不能な計画を事前に拒否する。");
                TestAssert.Throws<InvalidOperationException>(() => workflow.RunAsync(invalid).GetAwaiter().GetResult(),
                    "学習不能ならTensorFlowの実行前に停止する。");
                TestAssert.True(!Directory.Exists(invalid.ModelOutputFolder), "拒否した計画で出力を作らない。");
            }

            // 261021Claude: 定量は1バッチに満たなければ省略し、分類器だけでも昇格できることを通知する。
            var classifier = new TrainingModelMetrics("classification", "classifier", "trained", null, null,
                200, 160, 40, 2, null, 1, 1, 1, "cross_entropy", .5, .5, null, "legacy");
            var skipped = classifier with { Target = "regression", Status = "skipped", MineralName = "First",
                SkipReason = "学習用80本がバッチサイズ128未満です。", SampleCount = 100, TrainingSampleCount = 80,
                ValidationSampleCount = 20, CompletedEpochs = 0, BestEpoch = null, ValidationLoss = null };
            var onlyClassifier = ModelTrainingResult.CreatePromoted([classifier, skipped, skipped with { MineralName = "Second" }]);
            TestAssert.True(onlyClassifier.CompletionMessage!.StartsWith("分類モデルのみ作成しました。", StringComparison.Ordinal),
                "定量をすべて省略したときは分類器のみの通知になる。");
            TestAssert.True(!onlyClassifier.CompletionMessage.Contains("First", StringComparison.Ordinal)
                && !onlyClassifier.CompletionMessage.Contains("Second", StringComparison.Ordinal), "通知は鉱物名を列挙しない。");
            TestAssert.Equal(2, onlyClassifier.CompletionMessage.Split(skipped.SkipReason!, StringSplitOptions.None).Length,
                "共通の省略理由は1回だけ示す。");
            var trainedRegression = skipped with { Status = "trained", MineralName = "Second", SkipReason = null };
            TestAssert.True(ModelTrainingResult.CreatePromoted([classifier, skipped, trainedRegression]).CompletionMessage!
                .StartsWith("分類モデルと学習可能な定量モデル", StringComparison.Ordinal), "一部の省略では学習した定量を残す。");
            TestAssert.True(ModelTrainingResult.CreatePromoted([classifier, trainedRegression]).CompletionMessage is null,
                "通常の完了では通知を増やさない。");
            TestAssert.True(ModelTrainingResult.CreatePromoted().CompletionMessage is null, "指標なしの昇格は従来どおり。");
            TrainingResultsWriter.Write(fixture.Root, new TrainingResultsSpecification(1, 128, 1, .2f, 42, "val_loss", .0001f),
                [classifier, skipped]);
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root, ModelArtifactPaths.TrainingResultsFileName)));
            var omission = saved.RootElement.GetProperty("models")[1];
            TestAssert.Equal("skipped", omission.GetProperty("status").GetString(), "省略した状態を保存する。");
            TestAssert.Equal(80, omission.GetProperty("trainingSampleCount").GetInt32(), "実際の分割件数を保存する。");
            TestAssert.Equal(skipped.SkipReason, omission.GetProperty("skipReason").GetString(), "省略理由を保存する。");
        }

        // 261016Codex: 実学習の入口を使い、既存モデルの代替として未更新の重みを保存しないことを確認する。
        public static void RunNativeSmoke()
        {
            using var fixture = new TestFixture();
            var pools = new List<SpectrumTrainingPool>();
            foreach (string name in new[] { "First", "Second" })
            {
                var samples = new List<SpectrumTrainingSample>();
                for (int index = 0; index < 3; index++)
                {
                    string path = Path.Combine(fixture.Root, $"{name}{index}.emsa");
                    File.WriteAllLines(path, Enumerable.Range(0, SpectrumDataLoader.SpectrumLength)
                        .Select(channel => channel == 100 ? "10" : "0"));
                    samples.Add(new(path, new Dictionary<string, double> { [name] = 1d }));
                }
                pools.Add(new(name, [name], samples));
            }
            string output = Path.Combine(fixture.Root, "existing");
            Directory.CreateDirectory(output);
            string sentinel = Path.Combine(output, "sentinel.txt");
            File.WriteAllText(sentinel, "preserve");
            var result = new DeepLearning(_ => { }).RunTraining(pools, 1, 128, 1, .2f, 1.5, output);
            TestAssert.Equal(DeepLearningTrainingStatus.NotCompleted, result.Status, "分類の件数不足では完了しない。");
            TestAssert.Equal(DeepLearningTrainingFailureKind.InsufficientTrainingSamples, result.FailureKind!.Value,
                "件数不足と読込失敗を区別する。");
            TestAssert.SequenceEqual(new[] { sentinel }, Directory.GetFiles(output), "既存出力に学習成果物を追加しない。");
            TestAssert.Equal("preserve", File.ReadAllText(sentinel), "既存モデルの内容を保持する。");
        }
    }
}
