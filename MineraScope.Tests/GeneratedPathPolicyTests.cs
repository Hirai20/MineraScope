namespace MineraScope.Tests
{
    // 261021Claude: モデル作成が、TensorFlowの保存中に作る最深ファイルのパス長を学習前に検査することを確かめる。
    internal static class GeneratedPathPolicyTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            TestAssert.True(GeneratedPathPolicy.FindTooLong(Path.Combine(fixture.Root, "shallow"), 32, "test") is null,
                "A shallow folder is accepted.");

            // 261021Claude: 保存中の最深ファイルが上限ちょうどなら受理し、1文字超えたら学習前に拒否する。
            var samples = Enumerable.Range(0, 100).Select(index => new SpectrumTrainingSample(
                Path.Combine(fixture.Root, $"{index}.emsa"), new Dictionary<string, double> { ["A"] = .5, ["B"] = .5 })).ToArray();
            var pools = new[] { new SpectrumTrainingPool("First", ["First"], samples), new SpectrumTrainingPool("Second", ["Second"], samples) };
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
            int savingRelative = $".{Guid.Empty:N}.tmp".Length + 1 + ModelArtifactPaths.ClassificationFolderName.Length + 1
                + ModelArtifactPaths.SavedModelSavingDeepestLength;
            string parent = Path.Combine(fixture.Root,
                new string('p', GeneratedPathPolicy.MaxFullPathLength - 1 - savingRelative - fixture.Root.Length - 1));
            var plan = new ModelTrainingPlan(Path.Combine(parent, "M"), pools, fixture.Request.Training,
                fixture.Request.SemEdxCondition.GetDetectorProfile(), false);
            TestAssert.True(workflow.Validate(plan) is null, "A save path of exactly MAX_PATH is accepted.");
            TestAssert.True(workflow.Validate(plan with { ModelOutputFolder = Path.Combine(parent + "p", "M") })!
                .Contains("保存先が深すぎます", StringComparison.Ordinal), "One character beyond MAX_PATH is refused before training.");
            // 261021Claude: 定量の内部フォルダー名が分類より長ければ、その鉱物名で判定する。
            var regression = new SpectrumTrainingPool("LongerThanClassifierFolder", ["A", "B"], samples);
            TestAssert.True(workflow.Validate(plan with { TrainingPools = [.. pools, regression] }) is not null,
                "A long regression folder name is included in the save path check.");
        }
    }
}
