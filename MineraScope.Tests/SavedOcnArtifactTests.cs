using System.Text.Json;
namespace MineraScope.Tests
{
    // 261001Codex: Seedや独立OCNのワークフローを使わず、保存した通常機能のheadを検証する。
    internal static class SavedOcnArtifactTests
    {
        internal static void Run()
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            using var fixture = new TestFixture();
            foreach (int count in Enumerable.Range(2, 7))
            {
                int hidden = count >= 7 ? 3 : count / 2;
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
                string artifactPath = Path.Combine(classification, SoftmaxOcnHeadArtifact.FileName);
                File.WriteAllText(artifactPath, JsonSerializer.Serialize(artifact with { InputCount = 0 }, jsonOptions));
                TestAssert.Throws<InvalidDataException>(() => SoftmaxOcnHeadArtifact.Load(model), "Malformed widths are rejected.");
                File.WriteAllText(artifactPath, JsonSerializer.Serialize(artifact, jsonOptions));
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
        }
    }
}
