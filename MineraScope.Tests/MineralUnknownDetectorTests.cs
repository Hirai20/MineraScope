using System.Text.Json;
using System.Text.Json.Nodes;

namespace MineraScope.Tests
{
    // 261110Codex: 検証側だけにある偏差から、全入力の中心・共分散・最大距離を解析解と照合する。
    internal static class MineralUnknownDetectorTests
    {
        internal static void Run()
        {
            float[,] training = { { 0, 0 }, { 2, 0 }, { 10, 0 }, { 12, 0 } };
            int[] trainingLabels = [0, 0, 1, 1];
            float[,] validation = { { 4, 2 }, { 14, 2 } };
            int[] validationLabels = [0, 1];
            using var fixture = new TestFixture();

            foreach (double expansion in new[] { 1d, 1.5d })
            {
                var detector = MineralUnknownDetector.Build(training, trainingLabels, validation, validationLabels, expansion, CancellationToken.None);
                string folder = Path.Combine(fixture.Root, expansion == 1 ? "unit" : "expanded");
                detector.Save(folder);
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, MineralUnknownDetector.FileName)));
                var root = json.RootElement;
                TestAssert.Equal(MineralUnknownDetector.AllDataStatisticsSource, root.GetProperty("StatisticsSource").GetString(), "All-data statistics are identified.");
                TestAssert.Equal(6, root.GetProperty("StatisticsSampleCount").GetInt32(), "Validation rows are counted once.");
                double[][] means = JsonSerializer.Deserialize<double[][]>(root.GetProperty("Means"))!;
                double[][] precision = JsonSerializer.Deserialize<double[][]>(root.GetProperty("Precision"))!;
                double ridge = (4d + 4d / 3d) / 2d * 1e-3;
                Near(ridge, detector.Ridge, "Ridge uses the full-data pooled covariance trace.");
                Near(2, means[0][0], "Class zero includes the validation composition.");
                Near(12, means[1][0], "Class one includes the validation composition.");
                Near(2d / 3d, means[0][1], "The validation-only feature direction affects the center.");
                Near(2d / 3d, means[1][1], "Both class centers include validation rows.");

                // 261110Codex: 全6本・2クラスの分母4から得る共分散 [[4,2],[2,4/3]] を直接反転する。
                double a = 4 + ridge, b = 2, d = 4d / 3d + ridge, determinant = a * d - b * b;
                double[,] expectedPrecision = { { d / determinant, -b / determinant }, { -b / determinant, a / determinant } };
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                        Near(expectedPrecision[i, j], precision[i][j], "Precision uses N-K and validation scatter.");
                // 261110Codex: 平行移動した2クラスで共通の期待最大距離は一度だけ計算する。
                double max = 0;
                foreach (double x in new[] { 0d, 2d, 4d })
                {
                    double dx = x - 2, dy = (x == 4 ? 2 : 0) - 2d / 3d;
                    max = Math.Max(max, dx * dx * expectedPrecision[0, 0] + 2 * dx * dy * expectedPrecision[0, 1] + dy * dy * expectedPrecision[1, 1]);
                }
                double expectedThreshold = max * expansion * expansion;
                for (int label = 0; label < 2; label++)
                    Near(expectedThreshold, detector.Thresholds[label], "Threshold is the full-data maximum squared distance times radius squared.");
                TestAssert.True(MineralUnknownDetector.TryLoad(folder, 2, 2, out var loaded, out _), "New metadata round-trips.");
                TestAssert.Equal<int?>(6, loaded!.StatisticsSampleCount, "Loaded all-data count is preserved.");
                TestAssert.Equal(detector.Evaluate(new float[] { 5, .5f }), loaded.Evaluate(new float[] { 5, .5f }), "Saving/loading leaves predictions unchanged.");
            }

            // 261110Codex: 検証0本は訓練入力を重複させず、検証だけにあるクラスも全入力から推定する。
            var noValidation = MineralUnknownDetector.Build(training, trainingLabels, new float[0, 2], [], 1, CancellationToken.None);
            TestAssert.Equal<int?>(4, noValidation.StatisticsSampleCount, "No validation rows do not duplicate training rows.");
            Near(.001, noValidation.Ridge, "No-validation covariance retains the training-only degrees of freedom.");
            Near(1 / 2.001, noValidation.Thresholds[0], "No-validation threshold uses the original four rows.");
            var validationClass = MineralUnknownDetector.Build(new float[,] { { 0 }, { 2 } }, [0, 0],
                new float[,] { { 10 }, { 12 } }, [1, 1], 1, CancellationToken.None);
            TestAssert.Equal(2, validationClass.LabelCount, "Statistics include classes present only in validation.");
            TestAssert.SequenceEqual(new float[] { 0, 0, 2, 0, 10, 0, 12, 0 }, training.Cast<float>(), "Statistics do not modify input embeddings.");
            TestAssert.SequenceEqual(new[] { 0, 0, 1, 1 }, trainingLabels, "Statistics do not modify labels.");

            LegacyArtifacts(fixture.Root);
            TestAssert.Throws<InvalidDataException>(() => MineralUnknownDetector.Build(training, trainingLabels,
                new float[1, 1], [0], 1, CancellationToken.None), "Mismatched feature widths are rejected.");
            TestAssert.Throws<InvalidDataException>(() => MineralUnknownDetector.Build(training, trainingLabels,
                validation, [0], 1, CancellationToken.None), "Missing validation labels are rejected.");
            TestAssert.Throws<OperationCanceledException>(() => MineralUnknownDetector.Build(training, trainingLabels,
                validation, validationLabels, 1, new CancellationToken(true)), "Cancellation precedes statistics construction.");
        }

        // 261110Codex: 旧version 1の共通閾値とversion 2のクラス別閾値を読み替えず保持する。
        private static void LegacyArtifacts(string root)
        {
            foreach (int version in new[] { 1, 2 })
            {
                string folder = Path.Combine(root, "legacy" + version);
                Directory.CreateDirectory(folder);
                var artifact = new JsonObject
                {
                    ["Version"] = version, ["EmbeddingDim"] = 2, ["LabelCount"] = 2,
                    ["Threshold"] = .75, ["ThresholdQuantile"] = 1, ["ThresholdRadiusExpansion"] = 1, ["Ridge"] = .001,
                    ["Means"] = JsonSerializer.SerializeToNode(new double[][] { [1, 0], [11, 0] }),
                    ["Precision"] = JsonSerializer.SerializeToNode(new double[][] { [.5, 0], [0, 1000] })
                };
                if (version == 2)
                    artifact["Thresholds"] = JsonSerializer.SerializeToNode(new[] { .5, .75 });
                File.WriteAllText(Path.Combine(folder, MineralUnknownDetector.FileName), artifact.ToJsonString());
                TestAssert.True(MineralUnknownDetector.TryLoad(folder, 2, 2, out var detector, out _), "Legacy detectors still load.");
                TestAssert.Equal("classification_training_split", detector!.StatisticsSource, "Legacy centers/covariance retain their original source.");
                TestAssert.True(detector.StatisticsSampleCount is null, "Legacy input counts are not invented.");
                var prediction = detector.Evaluate(new float[] { 1, 0 });
                Near(0, prediction.Score, "Legacy center is not replaced by the new center.");
                Near(version == 1 ? .75 : .5, prediction.Threshold, "Legacy global/per-class thresholds are preserved.");
                string saved = Path.Combine(root, "legacy-copy" + version);
                detector.Save(saved);
                using var roundTrip = JsonDocument.Parse(File.ReadAllText(Path.Combine(saved, MineralUnknownDetector.FileName)));
                Near(1, roundTrip.RootElement.GetProperty("Means")[0][0].GetDouble(), "Legacy statistics remain unchanged on save.");
            }
        }

        // 261110Codex: 解析解との浮動小数点誤差だけを許容する。
        private static void Near(double expected, double actual, string message) =>
            TestAssert.True(Math.Abs(expected - actual) < 1e-10, $"{message} Expected={expected:R}, actual={actual:R}");
    }
}
