namespace MineraScope.Tests
{
    // 261016Codex: 使えない分類器で各入力の失敗を繰り返す前に、一括処理が止まることを確認する。
    internal static class BatchPredictionTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            var logs = new List<string>();
            var workflow = new SpectrumBatchPredictionWorkflow(typeof(DeepLearning).Assembly.Location, logs.Add);
            string missing = Path.Combine(fixture.Root, "missing-model");
            string[] inputs = [Path.Combine(fixture.Root, "unreadable1.emsa"), Path.Combine(fixture.Root, "unreadable2.emsa")];
            TestAssert.Throws<DirectoryNotFoundException>(() => workflow.RunAsync(missing, "missing", inputs).GetAwaiter().GetResult(),
                "分類器がなければ入力別の失敗結果を返さず、開始前に拒否する。");
            TestAssert.True(!logs.Any(line => line.StartsWith("分類を開始", StringComparison.Ordinal)),
                "使えないモデルでは分類開始を通知しない。");
        }
    }
}
