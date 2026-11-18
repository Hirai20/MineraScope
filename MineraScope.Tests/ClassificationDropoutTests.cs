using System.Text.Json;

namespace MineraScope.Tests
{
    // 261119Codex: マスクの再現性と値、学習条件の保存をネイティブ実行なしで検証する。
    internal static class ClassificationDropoutTests
    {
        internal static void Run()
        {
            TestAssert.Equal(.2, ClassificationDropoutPolicy.Rate, "Classification adopts dropout 0.2.");
            TestAssert.Equal(20261112, ClassificationDropoutPolicy.MaskSeed, "Masks use the independent dropout seed.");
            var first = new float[256, 128];
            var repeat = new float[256, 128];
            var random = new Random(ClassificationDropoutPolicy.MaskSeed);
            ClassificationDropoutPolicy.FillMask(random, first);
            ClassificationDropoutPolicy.FillMask(new Random(ClassificationDropoutPolicy.MaskSeed), repeat);
            TestAssert.SequenceEqual(first.Cast<float>(), repeat.Cast<float>(), "The same independent seed produces exactly the same masks.");
            TestAssert.True(first.Cast<float>().All(value => value is 0 or 1.25f), "Only dropped units or inverse-keep-scaled units occur.");
            double dropFraction = first.Cast<float>().Count(value => value == 0) / (double)first.Length;
            TestAssert.True(dropFraction is > .19 and < .21, "The fixed large mask realizes the selected dropout rate.");
            ClassificationDropoutPolicy.FillMask(random, repeat);
            TestAssert.True(!first.Cast<float>().SequenceEqual(repeat.Cast<float>()), "Later training batches draw new masks.");

            using var fixture = new TestFixture();
            var specification = new TrainingResultsSpecification(1, 4, 1, .2f, 42, "val_loss", .0001f);
            var metric = new TrainingModelMetrics("classification", "Classification", "trained", null, null, 8, 6, 2, 2,
                null, 1, 1, 1, "cross_entropy", .5, .5, null, "legacy");
            // 261118Codex: 旧条件と現行条件で同じ保存先を検証し、パスの組み立てを重複させない。
            string resultsPath = Path.Combine(fixture.Root, ModelArtifactPaths.TrainingResultsFileName);
            TrainingResultsWriter.Write(fixture.Root, specification, [metric]);
            using (var old = JsonDocument.Parse(File.ReadAllText(resultsPath)))
                TestAssert.True(!old.RootElement.GetProperty("settings").TryGetProperty("classificationDropout", out _),
                    "Unspecified legacy metadata is not retroactively filled with the new policy.");
            TrainingResultsWriter.Write(fixture.Root, specification with { ClassificationDropout = ClassificationDropoutPolicy.Settings }, [metric]);
            using var saved = JsonDocument.Parse(File.ReadAllText(resultsPath));
            AssertRecorded(saved.RootElement.GetProperty("settings"));
        }

        // 261119Codex: 同じ保存契約を通常の分類＋OCNの実学習でも確かめる。
        internal static void AssertRecorded(JsonElement container)
        {
            var recorded = container.GetProperty("classificationDropout");
            TestAssert.Equal(.2, recorded.GetProperty("rate").GetDouble(), "The actual dropout rate is saved.");
            TestAssert.Equal(20261112, recorded.GetProperty("maskSeed").GetInt32(), "The independent mask seed is saved.");
            TestAssert.Equal("after-first-relu", recorded.GetProperty("position").GetString(), "The first hidden layer position is saved.");
            TestAssert.Equal("inverted-training-only", recorded.GetProperty("mode").GetString(), "Validation and inference remain mask-free.");
        }
    }
}
