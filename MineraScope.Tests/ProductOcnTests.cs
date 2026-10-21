using System.Text.Json;

namespace MineraScope.Tests
{
    // 261001Codex: Seed実験を使わずに、通常機能のクラス数方針と保存した構造を検証する。
    internal static class ProductOcnTests
    {
        internal static void Run()
        {
            // 261001Codex: フォームの起動やユーザー設定の変更なしに、Designer側の削除を確認する。
            foreach (string field in new[] { "numericBoxUnknownDistanceScale", "numericBoxOcnTrainingSeed", "numericBoxOcnMaxEpochs", "numericBoxOcnPatience" })
                TestAssert.True(typeof(GeneratorForm).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is null,
                    "Production no longer exposes fixed or shared settings as dedicated controls.");
            using var fixture = new TestFixture();
            foreach (int count in Enumerable.Range(2, 7))
            {
                int hidden = SoftmaxOcnHeadTraining.GetHiddenClassCount(count);
                TestAssert.Equal(count >= 7 ? 3 : count / 2, hidden, "Large sets keep three hidden classes.");
                int width = Math.Min(3, count - hidden);
                var head = new SoftmaxOcnHead(new Random(42), width);
                var inputs = new List<float[]> { Enumerable.Repeat(.1f, width).ToArray(), Enumerable.Repeat(3f, width).ToArray() };
                // 261001Claude: 近い入力が学習済み側、遠い入力が未学習側です。出力番号は名前で参照します。
                TestAssert.True(double.IsFinite(head.TrainStep(inputs, [SoftmaxOcnHead.KnownOutput, SoftmaxOcnHead.UnknownOutput])),
                    "Every supported input width trains.");
                string model = Path.Combine(fixture.Root, "model" + count);
                string classification = ModelArtifactPaths.GetClassificationFolder(model);
                Directory.CreateDirectory(classification);
                string[] names = Enumerable.Range(0, count).Select(index => "Mineral" + index).ToArray();
                File.WriteAllText(Path.Combine(classification, ModelArtifactPaths.LabelEncoderFileName),
                    JsonSerializer.Serialize(names.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index)));
                File.WriteAllText(Path.Combine(classification, MineralUnknownDetector.FileName), "synthetic detector");
                File.WriteAllText(Path.Combine(classification, "saved_model.pb"), "synthetic classifier");
                SoftmaxOcnHeadArtifact.Save(model, names, head.Snapshot(), 42, 1, .5, "product test", width);
                var artifact = SoftmaxOcnHeadArtifact.Load(model);
                var restored = new SoftmaxOcnHead(new Random(0), artifact.InputCount);
                restored.Restore(artifact.Weights);
                TestAssert.Equal(head.Predict(inputs[0]), restored.Predict(inputs[0]), "Save/reload retains exact managed output.");
                TestAssert.Throws<ArgumentException>(() => restored.Predict(new float[width + 1]), "Wrong input dimensions are rejected.");
                var catalog = new ModelCatalog();
                // 261015Codex: 保存済み独立OCNバンドルはディスクに残すが、通常モデルの一覧へ再表示しない。
                Directory.CreateDirectory(Path.Combine(fixture.Root, "OCN"));
                catalog.Update(fixture.Root);
                TestAssert.True(!catalog.ModelNames.Contains("OCN"), "Retired independent OCN containers are hidden from model selection.");
                // 261014Codex: 維持するカタログは独立OCNの記述子を使わず、通常のモデル名を扱う。
                TestAssert.True(catalog.ModelNames.Contains("model" + count), "Only ordinary classifier names remain selectable.");
                TestAssert.True(catalog.ModelNames.Contains("model" + count), "Combined models are selectable by PTS views.");
                string artifactPath = Path.Combine(classification, SoftmaxOcnHeadArtifact.FileName);
                File.WriteAllText(artifactPath, JsonSerializer.Serialize(artifact with { InputCount = 0 }, ClassificationTrainingDataset.JsonOptions));
                TestAssert.Throws<InvalidDataException>(() => SoftmaxOcnHeadArtifact.Load(model), "Malformed widths are rejected.");
                File.WriteAllText(artifactPath, JsonSerializer.Serialize(artifact, ClassificationTrainingDataset.JsonOptions));
                if (width == 3)
                {
                    string file = Path.Combine(classification, SoftmaxOcnHeadArtifact.FileName);
                    var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
                    json.AsObject().Remove("inputCount");
                    json.AsObject().Remove("InputCount");
                    File.WriteAllText(file, json.ToJsonString());
                    TestAssert.Equal(3, SoftmaxOcnHeadArtifact.Load(model).InputCount, "Legacy artifacts retain three inputs.");
                }
            }
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
            foreach (var pools in new IReadOnlyList<SpectrumTrainingPool>[] { [], [new("First", ["First"], [])] })
            {
                var plan = new ModelTrainingPlan(Path.Combine(fixture.Root, "unused"), pools, fixture.Request.Training,
                    fixture.Request.SemEdxCondition.GetDetectorProfile(), false);
                TestAssert.Equal("分類する鉱物を2種類以上選択してください。", workflow.Validate(plan)!, "Zero and one class share a message.");
            }
            var notice = ModelTrainingResult.CreatePromoted(ocnFailureReason: "test failure");
            TestAssert.True(notice.Status == ModelTrainingStatus.Promoted && notice.CompletionMessage!.Contains("test failure"),
                "Optional OCN failure preserves promotion and explains why.");
        }

        // 261001Codex: ネイティブ検証では専用の合成poolを使い、少数クラスの全境界と従来の7クラスを確認する。
        internal static void RunNativeSmoke()
        {
            string modelRoot = Path.Combine(Path.GetTempPath(), "MSP_" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (int count in new[] { 2, 3, 4, 5, 6, 7 })
                {
                    using var fixture = new TestFixture(new SpectrumTimeSchedule(60, [new(50, 1)]),
                        Enumerable.Range(0, count).Select(index => TestFixture.CreateSolution("Mineral" + index)).ToArray());
                    fixture.Complete(TestFixture.Reservations(fixture.Workflow.CreateMissingSimulationPlan(fixture.Request, out _)));
                    var pools = fixture.Workflow.PrepareTrainingPools(fixture.Request).Pools;
                    var request = fixture.Request with
                    {
                        Paths = fixture.Request.Paths with { ModelOutputFolder = modelRoot }, ModelName = "M" + count,
                        Training = fixture.Request.Training with { Epochs = 1, BatchSize = 4, EarlyStoppingPatience = 0, UnknownDistanceScale = 1d }
                    };
                    var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
                    var plan = workflow.CreatePlan(request, pools, softmaxOcnSeed: 42);
                    TestAssert.True(workflow.Validate(plan) is null, "Small-class creation passes preflight.");
                    var result = workflow.RunAsync(plan).GetAwaiter().GetResult();
                    TestAssert.True(result.Status == ModelTrainingStatus.Promoted && result.CompletionMessage is null,
                        "Classifier and head are saved successfully.");
                    var artifact = SoftmaxOcnHeadArtifact.Load(plan.ModelOutputFolder);
                    TestAssert.Equal(Math.Min(3, count - SoftmaxOcnHeadTraining.GetHiddenClassCount(count)), artifact.InputCount,
                        "Saved width matches the training policy.");
                    TestAssert.Equal(1, artifact.BestEpoch, "The head honors the shared one-epoch limit.");
                    float[] spectrum = TensorFlowExecutor.Run(() => SpectrumDataLoader.LoadNormalizedSpectrum(
                        pools[0].Samples[0].FilePath, SpectrumPreprocessing.ForTraining()))!;
                    var service = new MineralClassificationPredictionService();
                    var single = service.Predict(plan.ModelOutputFolder, spectrum);
                    var rows = new float[1, spectrum.Length];
                    for (int column = 0; column < spectrum.Length; column++) rows[0, column] = spectrum[column];
                    string[] names = service.GetLabelNames(plan.ModelOutputFolder);
                    int predicted = service.PredictTop1Batch(plan.ModelOutputFolder, rows, CancellationToken.None, detectUnknown: true)[0];
                    TestAssert.True(single.UnknownDetectionApplied && single.UnknownThreshold == .5,
                        "PTS point predictions auto-load the saved head and use 0.5.");
                    TestAssert.Equal(single.IsUnknown ? PtsClassificationMapResult.UnknownLabelId : Array.IndexOf(names, single.PredictedMineral),
                        predicted, "Map batches and point predictions agree.");
                    var closed = service.PredictAsync(plan.ModelOutputFolder, spectrum, detectUnknown: false).GetAwaiter().GetResult();
                    TestAssert.True(!closed.UnknownDetectionApplied && !closed.IsUnknown, "Detection-off preserves closed-set prediction.");
                    // 261014Codex: 単発予測とファイル予測は通常の保存済み分類器の読み込み経路を共用する。
                    var files = new SpectrumBatchPredictionWorkflow(AppContext.BaseDirectory, _ => { })
                        .RunAsync(plan.ModelOutputFolder, Path.GetFileName(plan.ModelOutputFolder), [pools[0].Samples[0].FilePath]).GetAwaiter().GetResult();
                    TestAssert.Equal(single.IsUnknown, files.Items[0].Classification!.IsUnknown, "File classification uses the same saved decision.");
                    if (count == 2)
                    {
                        // 261001Codex: 任意のhead用に鉱物ごと6行を確保できなくても、有効な分類器は学習できる。
                        var shortPools = pools.Select(pool => pool with { Samples = pool.Samples.Take(5).ToArray() }).ToArray();
                        var fallback = workflow.CreatePlan(request with { ModelName = "Fallback" }, shortPools, softmaxOcnSeed: 42)
                            with { TrainingDataProvenance = null };
                        var saved = workflow.RunAsync(fallback).GetAwaiter().GetResult();
                        TestAssert.True(saved.Status == ModelTrainingStatus.Promoted && saved.CompletionMessage!.Contains("OCN判定器は作成できません"),
                            "Optional head failure saves the classifier and explains why.");
                        TestAssert.True(!SoftmaxOcnHeadArtifact.Exists(fallback.ModelOutputFolder), "Failed heads are never published.");
                        TestAssert.True(File.Exists(Path.Combine(ModelArtifactPaths.GetClassificationFolder(fallback.ModelOutputFolder), "saved_model.pb")),
                            "The fallback classifier is present.");
                        // 261001Codex: headの学習中に中止した場合、分類器だけの置換を保存せずに既存出力を保持する。
                        string cancelFolder = Path.Combine(modelRoot, "Cancel");
                        Directory.CreateDirectory(cancelFolder);
                        string marker = Path.Combine(cancelFolder, "existing.txt");
                        File.WriteAllText(marker, "existing model");
                        using var cancellation = new CancellationTokenSource();
                        var cancelWorkflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), message =>
                        {
                            if (message.StartsWith("分類＋OCN head: epoch", StringComparison.Ordinal))
                                cancellation.Cancel();
                        });
                        TestAssert.Throws<OperationCanceledException>(() => cancelWorkflow.RunAsync(
                            plan with { ModelOutputFolder = cancelFolder, AllowOverwriteExistingModel = true },
                            cancellationToken: cancellation.Token).GetAwaiter().GetResult(),
                            "Head cancellation stops the entire operation.");
                        TestAssert.Equal("existing model", File.ReadAllText(marker), "Head cancellation preserves old output.");
                        TestAssert.True(!SoftmaxOcnHeadArtifact.Exists(cancelFolder), "Cancelled heads are never promoted.");

                        // 261001Codex: 不変スナップショットの作成後に元データが変わった場合、分類で読めても公開を止める。
                        string changedFolder = Path.Combine(modelRoot, "ChangedInput");
                        Directory.CreateDirectory(changedFolder);
                        string changedMarker = Path.Combine(changedFolder, "existing.txt");
                        File.WriteAllText(changedMarker, "existing model");
                        var changedWorkflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), message =>
                        {
                            if (message.StartsWith("学習データの固定完了:", StringComparison.Ordinal))
                                File.AppendAllText(pools[0].Samples[0].FilePath, "\n#COMMENT: changed after snapshot\n");
                        });
                        TestAssert.Throws<IOException>(() => changedWorkflow.RunAsync(
                            plan with { ModelOutputFolder = changedFolder, AllowOverwriteExistingModel = true }).GetAwaiter().GetResult(),
                            "Changed inputs cannot publish a mismatched classifier and head.");
                        TestAssert.Equal("existing model", File.ReadAllText(changedMarker), "Changed inputs preserve old output.");
                        Console.WriteLine("PASS: Product OCN cancellation and changed-input preservation");
                    }
                    // 261014Codex: 壊れた判定器は各ファイルの失敗へ変換せず、一括分類の開始前に拒否する。
                    string headPath = Path.Combine(ModelArtifactPaths.GetClassificationFolder(plan.ModelOutputFolder), SoftmaxOcnHeadArtifact.FileName);
                    File.WriteAllText(headPath, JsonSerializer.Serialize(artifact with { InputCount = 0 }, ClassificationTrainingDataset.JsonOptions));
                    TestAssert.Throws<InvalidDataException>(() => new SpectrumBatchPredictionWorkflow(AppContext.BaseDirectory, _ => { })
                        .RunAsync(plan.ModelOutputFolder, Path.GetFileName(plan.ModelOutputFolder), [pools[0].Samples[0].FilePath]).GetAwaiter().GetResult(),
                        "Corrupt heads fail the batch before any per-file results are returned.");
                    Console.WriteLine($"PASS: Product OCN {count} mineral classes");
                }
            }
            finally
            {
                string resolved = Path.GetFullPath(modelRoot);
                TestAssert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(resolved),
                    "Cleanup stays in the test's TEMP child.");
                TestAssert.True(Path.GetFileName(resolved).StartsWith("MSP_", StringComparison.Ordinal)
                    && Guid.TryParseExact(Path.GetFileName(resolved)[4..], "N", out _), "Only the unique test folder is removed.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
