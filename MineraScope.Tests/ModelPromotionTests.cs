using System.Reflection;
using System.Runtime.ExceptionServices;

namespace MineraScope.Tests
{
    // 260930Codex: Real filesystem failures cover promotion without starting TensorFlow or touching user models.
    internal static class ModelPromotionTests
    {
        public static void Run()
        {
            CleanupFailureKeepsNewModel();
            PromotionFailureRestoresOldModel();
            CleanupRetainsOtherModelBackups();
        }

        // 260930Codex: Lock a backup only after promotion; a partially removed backup must never replace the new model.
        private static void CleanupFailureKeepsNewModel()
        {
            using var fixture = new TestFixture();
            string model = Path.Combine(fixture.Root, "model");
            string staging = Path.Combine(fixture.Root, "staging");
            Directory.CreateDirectory(model);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(model, "old.txt"), "old");
            File.WriteAllText(Path.Combine(model, "removed.txt"), "old part");
            File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
            var logs = new List<string>();
            FileStream? locked = null;
            string? backup = null;
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), message =>
            {
                logs.Add(message);
                if (!message.StartsWith("モデル保存先を昇格しました:", StringComparison.Ordinal))
                    return;
                backup = Directory.GetDirectories(fixture.Root, "model.*.previous").Single();
                File.Delete(Path.Combine(backup, "removed.txt"));
                locked = new FileStream(Path.Combine(backup, "old.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            try
            {
                Promote(workflow, staging, model);
                TestAssert.Equal("new", File.ReadAllText(Path.Combine(model, "new.txt")), "Cleanup failure preserves the published model.");
                TestAssert.True(!File.Exists(Path.Combine(model, "old.txt")), "A partial old model must not be restored after success.");
                TestAssert.True(backup is not null && Directory.Exists(backup), "Locked backup remains available for manual cleanup.");
                TestAssert.True(logs.Any(message => message.Contains("手動で削除") && message.Contains(backup!)), "Report the retained backup path.");
            }
            finally
            {
                locked?.Dispose();
            }
        }

        // 260930Codex: A missing staging folder fails the real Move after backup creation and must restore the old files.
        private static void PromotionFailureRestoresOldModel()
        {
            using var fixture = new TestFixture();
            string model = Path.Combine(fixture.Root, "model");
            Directory.CreateDirectory(model);
            File.WriteAllText(Path.Combine(model, "old.txt"), "old");
            var workflow = new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { });
            bool failed = false;
            try
            {
                Promote(workflow, Path.Combine(fixture.Root, "missing"), model);
            }
            catch (DirectoryNotFoundException)
            {
                failed = true;
            }
            TestAssert.True(failed, "The promotion failure must reach its caller.");
            TestAssert.Equal("old", File.ReadAllText(Path.Combine(model, "old.txt")), "Failed promotion restores the full old model.");
            TestAssert.Equal(0, Directory.GetDirectories(fixture.Root, "model.*.previous").Length, "Restoration consumes this attempt's backup.");
        }

        // 260930Codex: Only exact model-name/GUID backups belong to this cleanup; a longer sibling model name is independent.
        private static void CleanupRetainsOtherModelBackups()
        {
            using var fixture = new TestFixture();
            string model = Path.Combine(fixture.Root, "model");
            string staging = Path.Combine(fixture.Root, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
            string abandoned = Path.Combine(fixture.Root, $"model.{Guid.NewGuid():N}.previous");
            Directory.CreateDirectory(abandoned);
            string[] retained = ["model.previous", $"model.child.{Guid.NewGuid():N}.previous", $"other.{Guid.NewGuid():N}.previous"];
            foreach (string name in retained)
                Directory.CreateDirectory(Path.Combine(fixture.Root, name));
            Promote(new ModelTrainingWorkflow(new DeepLearning(_ => { }), _ => { }), staging, model);
            TestAssert.True(!Directory.Exists(abandoned), "Remove this model's abandoned backup after successful promotion.");
            foreach (string name in retained)
                TestAssert.True(Directory.Exists(Path.Combine(fixture.Root, name)), $"Preserve independent folder {name}.");
        }

        // 260930Codex: Invoke the existing private filesystem boundary so tests need no new production injection API.
        private static void Promote(ModelTrainingWorkflow workflow, string staging, string model)
        {
            var method = typeof(ModelTrainingWorkflow).GetMethod("PromoteTemporaryFolder", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                method.Invoke(workflow, [staging, model, true]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
