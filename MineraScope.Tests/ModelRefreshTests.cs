using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;

namespace MineraScope.Tests
{
    // 260930Codex: Content identity detects same-size same-time changes, sidecars, additions and removals.
    internal static class ModelRefreshTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            string folder = Path.Combine(fixture.Root, "artifacts");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "weights.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            DateTime timestamp = File.GetLastWriteTimeUtc(file);
            string original = ModelArtifactFingerprint.Compute(folder);
            TestAssert.Equal(original, ModelArtifactFingerprint.Compute(folder), "Unchanged artifacts keep their identity.");
            File.WriteAllBytes(file, new byte[] { 3, 2, 1 });
            File.SetLastWriteTimeUtc(file, timestamp);
            TestAssert.True(original != ModelArtifactFingerprint.Compute(folder), "Equal-size equal-time replacement must be detected.");
            string changed = ModelArtifactFingerprint.Compute(folder);
            File.WriteAllText(Path.Combine(folder, "labels.json"), "{}");
            TestAssert.True(changed != ModelArtifactFingerprint.Compute(folder), "Added sidecars change the identity.");
            File.Delete(Path.Combine(folder, "labels.json"));
            TestAssert.Equal(changed, ModelArtifactFingerprint.Compute(folder), "Removing the added sidecar restores the identity.");
        }

        // 260930Codex: Save synthetic models twice at one path and exercise the actual cached prediction services.
        public static void RunNativeSmoke()
        {
            using var fixture = new TestFixture();
            string root = Path.Combine(fixture.Root, "model");
            string classification = ModelArtifactPaths.GetClassificationFolder(root);
            Directory.CreateDirectory(classification);
            float[] spectrum = new float[SpectrumDataLoader.SpectrumLength];
            var service = new MineralClassificationPredictionService();
            SaveModel("CreateClassificationModel", classification, 0, ModelArtifactPaths.LabelEncoderFileName);
            TestAssert.Equal("First", service.Predict(root, spectrum).PredictedMineral, "The original classifier is loaded.");
            object firstModel = CachedModel(service);
            TestAssert.Equal("First", service.Predict(root, spectrum).PredictedMineral, "Unchanged classifier predictions are stable.");
            TestAssert.True(ReferenceEquals(firstModel, CachedModel(service)), "Unchanged files reuse the loaded classifier.");
            SaveModel("CreateClassificationModel", classification, 1, ModelArtifactPaths.LabelEncoderFileName);
            TestAssert.Equal("Second", service.Predict(root, spectrum).PredictedMineral, "Single predictions reload overwritten weights.");
            SaveModel("CreateClassificationModel", classification, 0, ModelArtifactPaths.LabelEncoderFileName);
            service.GetLabelNames(root);
            object mapModel = CachedModel(service);
            TestAssert.Equal(0, service.PredictTop1Batch(root, new float[1, SpectrumDataLoader.SpectrumLength], CancellationToken.None, false)[0],
                "Map preparation reloads the overwritten classifier before chunk inference.");
            TestAssert.True(ReferenceEquals(mapModel, CachedModel(service)), "Map chunks reuse the prepared classifier.");
            var timer = Stopwatch.StartNew();
            ModelArtifactFingerprint.Compute(classification);
            timer.Stop();
            Console.WriteLine($"Selected model check: {Directory.EnumerateFiles(classification, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)} bytes, {timer.Elapsed.TotalMilliseconds:F2} ms (synthetic model).");

            string regression = Path.Combine(root, "Sample_Regression");
            var regressionService = new MineralRegressionPredictionService();
            SaveModel("CreateRegressionModel", regression, 0, ModelArtifactPaths.ComponentIndexFileName);
            TestAssert.True(regressionService.Predict(regression, spectrum).Components[0].Ratio > .99f, "The original regressor is loaded.");
            SaveModel("CreateRegressionModel", regression, 1, ModelArtifactPaths.ComponentIndexFileName);
            TestAssert.True(regressionService.Predict(regression, spectrum).Components[1].Ratio > .99f, "Regression reloads overwritten weights.");
        }

        // 260930Codex: Framework work remains on the production executor; all artifacts belong to this test fixture.
        private static void SaveModel(string factoryName, string folder, int preferred, string indexFile)
        {
            TensorFlowExecutor.Run(() =>
            {
                var factory = typeof(DeepLearning).GetMethod(factoryName, BindingFlags.NonPublic | BindingFlags.Static)!;
                var model = (Model)factory.Invoke(null, new object[] { 2 })!;
                var weights = model.get_weights();
                var replacement = weights.Select(weight => np.array(new float[weight.ToArray<float>().Length]).reshape(weight.shape)).ToList();
                replacement[^1].Dispose();
                replacement[^1] = np.array(preferred == 0 ? new[] { 4f, 0f } : new[] { 0f, 4f });
                model.set_weights(replacement);
                Directory.CreateDirectory(folder);
                model.save(folder);
                File.WriteAllText(Path.Combine(folder, indexFile), JsonSerializer.Serialize(new Dictionary<string, int> { ["First"] = 0, ["Second"] = 1 }));
                foreach (var weight in replacement)
                    weight.Dispose();
                return true;
            });
        }

        // 260930Codex: Verify cache reuse directly instead of relying on unstable timing thresholds.
        private static object CachedModel(MineralClassificationPredictionService service) =>
            typeof(MineralClassificationPredictionService).GetField("_classificationModel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
    }
}
