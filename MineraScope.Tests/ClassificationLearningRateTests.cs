using System.Reflection;
using System.Text.Json;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;

namespace MineraScope.Tests
{
    // 260930Codex: Check ordinary defaults, old JSON compatibility, validation and persisted effective rates.
    internal static class ClassificationLearningRateTests
    {
        private const string LegacyJson = "{\"Epochs\":1,\"BatchSize\":4,\"EarlyStoppingPatience\":1,\"ValidationSplit\":0.2,\"UnknownDistanceScale\":1.5}";

        public static void Run()
        {
            var current = new ModelTrainingSettings(1, 4, 1, .2f, 1.5);
            TestAssert.Equal(.0001f, current.LearningRate, "New ordinary settings use 0.0001.");
            using var fixture = new TestFixture();
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
            foreach (var options in new[] { new JsonSerializerOptions(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase } })
            {
                string oldJson = options.PropertyNamingPolicy is null ? LegacyJson
                    : "{\"epochs\":1,\"batchSize\":4,\"earlyStoppingPatience\":1,\"validationSplit\":0.2,\"unknownDistanceScale\":1.5}";
                TestAssert.Equal(.001f, JsonSerializer.Deserialize<ModelTrainingSettings>(oldJson, options)!.LearningRate,
                    "Settings written before the rate field retain 0.001.");
                foreach (float rate in new[] { .0001f, .001f, .01f })
                {
                    var settings = current with { LearningRate = rate };
                    TestAssert.Equal(settings, JsonSerializer.Deserialize<ModelTrainingSettings>(JsonSerializer.Serialize(settings, options), options),
                        "Explicit rates survive JSON round trips.");
                    TrainingResultsWriter.Write(fixture.Root, new TrainingResultsSpecification(1, 4, 1, .2f,
                        SplitSeed: 42, EarlyStoppingMonitor: "val_loss", ClassificationLearningRate: rate),
                        new[] { new TrainingModelMetrics("classification", "Classification", "completed", null, null, 8, 6, 2, 2, null, 1, 1, 1, "cross_entropy", .5, .5, null, "legacy") });
                    using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root, ModelArtifactPaths.TrainingResultsFileName)));
                    TestAssert.Equal(rate, record.RootElement.GetProperty("settings").GetProperty("classificationLearningRate").GetSingle(),
                        "Results record the rate actually requested.");
                }
            }
            foreach (float rate in new[] { 0f, -.001f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var plan = workflow.CreatePlan(fixture.Request with { Training = current with { LearningRate = rate } }, Array.Empty<SpectrumTrainingPool>());
                TestAssert.True(workflow.Validate(plan)!.Contains("学習率"), "The workflow rejects invalid rates before training.");
                TestAssert.Throws<ArgumentOutOfRangeException>(() => new DeepLearning(_ => { }).RunTraining(
                    Array.Empty<SpectrumTrainingPool>(), 1, 4, 1, .2f, 1.5, fixture.Root, classificationLearningRate: rate),
                    "The direct training entry also rejects invalid rates.");
            }
        }

        // 260930Codex: Compare actual graph updates from identical initial weights and synthetic spectra.
        public static void RunNativeSmoke()
        {
            TensorFlowExecutor.Run(() =>
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                var create = typeof(DeepLearning).GetMethod("CreateClassificationModel", flags)!;
                var fit = typeof(DeepLearning).GetMethod("FitModelWithCancellation", flags)!;
                var model = (Model)create.Invoke(null, new object[] { 2 })!;
                var initial = model.get_weights();
                float[,] values = new float[8, 2048];
                for (int row = 0; row < 8; row++)
                    values[row, row % 2] = 1;
                using var x = np.array(values);
                using var y = np.array(new[] { 0, 1, 0, 1, 0, 1, 0, 1 });
                var snapshots = new List<float[]>();
                foreach (object rate in new object[] { Type.Missing, .0001f, .001f })
                {
                    model.set_weights(initial);
                    // 260930Codex: Resolve optional arguments by name so later training refactors keep this check valid.
                    object?[] arguments = fit.GetParameters().Select(parameter => parameter.Name switch
                    {
                        "model" => (object)model,
                        "xTrain" or "xValidation" => x,
                        "yTrain" or "yValidation" => y,
                        "batchSize" => 4,
                        "epochs" or "patience" => 1,
                        "operationName" => "classification:LearningRateTest",
                        "logAction" => (Action<string>)(_ => { }),
                        "cancellationToken" => CancellationToken.None,
                        "classificationLearningRate" => rate,
                        _ => null
                    }).ToArray();
                    fit.Invoke(null, arguments);
                    snapshots.Add(model.get_weights().SelectMany(weight => weight.ToArray<float>()).ToArray());
                }
                TestAssert.SequenceEqual(snapshots[0], snapshots[1], "The omitted rate performs exactly the explicit 0.0001 updates.");
                TestAssert.True(!snapshots[0].SequenceEqual(snapshots[2]), "The graph optimizer responds to the selected rate.");
                TestAssert.True(!initial.SelectMany(weight => weight.ToArray<float>()).SequenceEqual(snapshots[0]), "Training updates the initial weights.");
                return true;
            });
        }
    }
}
