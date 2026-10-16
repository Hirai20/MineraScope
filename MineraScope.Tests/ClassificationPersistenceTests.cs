namespace MineraScope.Tests
{
    // 261016Codex: 採用重みの一時配列を解放した後も、通常分類の保存・再読込ができることを確認する。
    internal static class ClassificationPersistenceTests
    {
        public static void RunNativeSmoke()
        {
            using var fixture = new TestFixture();
            var pools = new List<SpectrumTrainingPool>();
            foreach (var (name, peak) in new[] { ("First", 100), ("Second", 120) })
            {
                var samples = new List<SpectrumTrainingSample>();
                for (int index = 0; index < 8; index++)
                {
                    string path = Path.Combine(fixture.Root, $"{name}{index}.emsa");
                    File.WriteAllLines(path, Enumerable.Range(0, SpectrumDataLoader.SpectrumLength)
                        .Select(channel => channel == peak ? "10" : "0"));
                    samples.Add(new(path, new Dictionary<string, double> { [name] = 1d }));
                }
                pools.Add(new(name, [name], samples));
            }
            string output = Path.Combine(fixture.Root, "model");
            Directory.CreateDirectory(output);
            var result = new DeepLearning(_ => { }).RunTraining(pools, 3, 4, 3, .25f, 1.5, output);
            TestAssert.Equal(DeepLearningTrainingStatus.Completed, result.Status, "複数エポックの分類学習を完了できる。");
            TestAssert.True(result.ModelMetrics.Single().BestEpoch is >= 1, "最良エポックの重みが選択される。");
            string classification = ModelArtifactPaths.GetClassificationFolder(output);
            TestAssert.True(File.Exists(Path.Combine(classification, ModelArtifactPaths.LabelEncoderFileName)), "ラベルを保存できる。");
            TestAssert.True(SpectrumPreprocessing.LoadFromModelFolder(classification).HasLowEnergyMask, "前処理を保存できる。");
            var service = new MineralClassificationPredictionService();
            foreach (var pool in pools)
            {
                float[] input = SpectrumDataLoader.LoadNormalizedSpectrum(pool.Samples[0].FilePath,
                    SpectrumPreprocessing.LoadFromModelFolder(classification))!;
                var prediction = service.Predict(output, input);
                TestAssert.True(prediction.Probabilities.Count == 2, "保存モデルの再読込後も両クラスの出力を得られる。");
                TestAssert.True(prediction.Probabilities.All(item => float.IsFinite(item.Confidence)), "出力確率が有限である。");
                TestAssert.True(Math.Abs(prediction.Probabilities.Sum(item => item.Confidence) - 1f) < 1e-5,
                    "確率の総和を保持する。");
            }
        }
    }
}
