namespace MineraScope.Tests
{
    // 260930Codex: Adapt the committed private catalog test without importing unrelated concurrency infrastructure.
    internal static class ModelCatalogTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            string modelRoot = Path.Combine(fixture.Root, "catalog");
            string[] visible = ["finished", "user.previous", "user.tmp"];
            string[] hidden = [$".{Guid.NewGuid():N}.tmp", $"active.{Guid.NewGuid():N}.tmp", $"old.{Guid.NewGuid():N}.previous"];
            foreach (string name in visible.Concat(hidden))
                Directory.CreateDirectory(Path.Combine(modelRoot, name));
            var catalog = new ModelCatalog();
            catalog.Update(modelRoot);
            TestAssert.SequenceEqual(visible, catalog.ModelNames,
                "Hide only private staging and backup names, preserving ordinary model names.");
        }
    }
}
