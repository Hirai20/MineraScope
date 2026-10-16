namespace MineraScope.Tests
{
    // 260930Codex: 合成ファイルだけを使い、正規化と学習で空入力を拒否することを確認する。
    // 261016Codex: 公開版の合成入力で、無信号の拒否と既存出力の保持を検証する。
    internal static class SpectrumSignalTests
    {
        public static void RunNativeSmoke()
        {
            using var fixture = new TestFixture();
            string zero = Path.Combine(fixture.Root, "zero.emsa");
            string masked = Path.Combine(fixture.Root, "masked.emsa");
            string valid = Path.Combine(fixture.Root, "valid.emsa");
            foreach (var (path, signalChannel) in new[] { (zero, -1), (masked, 1), (valid, 100) })
                File.WriteAllLines(path, Enumerable.Range(0, SpectrumDataLoader.SpectrumLength)
                    .Select(channel => channel == signalChannel ? "10" : "0"));

            TensorFlowExecutor.Run(() =>
            {
                foreach (var order in new[] { LowEnergyMaskOrder.Pre, LowEnergyMaskOrder.Post })
                {
                    var preprocessing = new SpectrumPreprocessing(36, order);
                    foreach (string path in new[] { zero, masked })
                    {
                        var values = SpectrumDataLoader.LoadNormalizedSpectrum(path, preprocessing, out bool signal);
                        TestAssert.True(values is not null && !signal, "Readable empty input is distinguished from unreadable input.");
                        TestAssert.True(SpectrumDataLoader.LoadNormalizedSpectrum(path, preprocessing) is null,
                            "Existing callers cannot accept empty spectra.");
                    }
                    var validValues = SpectrumDataLoader.LoadNormalizedSpectrum(valid, preprocessing, out bool validSignal);
                    TestAssert.True(validSignal && validValues![100] == 1f, "Normal input retains its normalization.");
                    var samples = new[] { zero, masked, valid }.Select(path => new SpectrumTrainingSample(path,
                        new Dictionary<string, double> { ["First"] = 1d })).ToArray();
                    var pool = new SpectrumTrainingPool("First", ["First"], samples);
                    var cache = new SpectrumDataLoader.NormalizedSpectrumCache([zero, masked, valid]);
                    var loaded = SpectrumDataLoader.LoadClassificationData([pool], cache: cache, preprocessing: preprocessing);
                    TestAssert.Equal(1, loaded.Labels.Count, "Only valid signal enters classification training.");
                    TestAssert.Equal(2, loaded.Failures.Count, "Each empty input has a failure reason.");
                    TestAssert.True(loaded.Failures.All(failure => failure.Kind == SpectrumDataLoader.ClassificationLoadFailureKind.NoSignal),
                        "Empty inputs are not misreported as read failures.");
                    TestAssert.Equal(1, cache.CachedCount, "Empty inputs do not enter the regression cache.");
                    var regression = SpectrumDataLoader.LoadRegressionData(pool, cache: cache, preprocessing: preprocessing);
                    TestAssert.Equal(1L, regression.Spectra.shape[0], "Quantitative training also excludes empty inputs.");
                    // 260930Codex: モデル学習や既存成果物への接触を伴わず、通常の学習処理の境界を検証する。
                    string output = Path.Combine(fixture.Root, "existing-model");
                    Directory.CreateDirectory(output);
                    string sentinel = Path.Combine(output, "sentinel.txt");
                    File.WriteAllText(sentinel, "preserve");
                    var logs = new List<string>();
                    var result = new DeepLearning(logs.Add).RunTraining([pool], 1, 1, 1, .2f, 1.5, output);
                    TestAssert.True(result.Status == DeepLearningTrainingStatus.NotCompleted,
                        "An empty training input prevents model creation.");
                    TestAssert.True(logs.Any(line => line.Contains("信号なし", StringComparison.Ordinal)),
                        "Training reports the specific reason.");
                    TestAssert.Equal("preserve", File.ReadAllText(sentinel), "Failed training preserves existing output.");
                }
                TestAssert.True(SpectrumDataLoader.LoadNormalizedSpectrum(masked, SpectrumPreprocessing.None, out bool unmasked) is not null && unmasked,
                    "Signal removed by a mask remains valid when no mask is configured.");
                TestAssert.True(SpectrumDataLoader.LoadNormalizedSpectrum(Path.Combine(fixture.Root, "missing.emsa"),
                    SpectrumPreprocessing.None, out bool missing) is null && !missing, "Missing files retain read-failure semantics.");
                return true;
            });
        }
    }
}
