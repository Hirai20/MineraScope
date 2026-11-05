// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using System.Globalization;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 261104Codex: GUIなし生成の上書き値を、実DTSA-II・保存設定・ログに触れずに検証する。
    internal static class SimulationHeadlessRunnerTests
    {
        // 261104Codex: カンマ小数のロケールでも、未指定・蒸着なし・正整数・不正な値を区別する。
        public static void Run()
        {
            RunLegacyCarbonSettings();
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                foreach (var (text, expected) in new (string? Text, double? Expected)[]
                {
                    (null, null), ("", null), ("0", 0), ("0.02", 0.02), ("2e-2", 0.02), (" 0.02 ", 0.02)
                })
                {
                    TestAssert.True(SimulationHeadlessRunner.TryParseCarbonOverride(text, out double? actual),
                        $"The coating override '{text}' must be accepted.");
                    TestAssert.Equal(expected, actual, "An explicit zero must remain zero; only an omitted override uses saved settings.");
                }

                foreach (string text in new[] { "-0.001", "NaN", "Infinity", "-Infinity", "1e309", "0,02", "bad", " " })
                {
                    TestAssert.True(!SimulationHeadlessRunner.TryParseCarbonOverride(text, out double? actual),
                        $"The invalid coating override '{text}' must be refused.");
                    TestAssert.True(actual is null, "An invalid coating override must not return a usable thickness.");
                }

                foreach (var (text, expected) in new (string? Text, int? Expected)[]
                {
                    (null, null), ("", null), ("1", 1), ("100", 100), (" +25 ", 25), ("2147483647", int.MaxValue)
                })
                {
                    TestAssert.True(SimulationHeadlessRunner.TryParseTargetOverride(text, out int? actual),
                        $"The spectrum-count override '{text}' must be accepted.");
                    TestAssert.Equal(expected, actual, "A valid spectrum count must be preserved exactly.");
                }

                foreach (string text in new[] { "0", "-1", "0.5", "1.5", "100.0", "1e2", "2147483648", "NaN", "Infinity", "1,000", "bad", " " })
                {
                    TestAssert.True(!SimulationHeadlessRunner.TryParseTargetOverride(text, out int? actual),
                        $"The invalid spectrum-count override '{text}' must be refused without truncation or overflow.");
                    TestAssert.True(actual is null, "An invalid spectrum-count override must not return a usable count.");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        // 261106Codex: 旧GUIなし生成のばらつき設定を読んでも、膜厚は0を含む指定値に固定する。
        private static void RunLegacyCarbonSettings()
        {
            foreach (double thickness in new[] { 0, 0.02 })
            {
                string legacyJson = "{\"Version\":1,\"CarbonThickness\":"
                    + thickness.ToString(CultureInfo.InvariantCulture) + ",\"CarbonThicknessJitterPercent\":20}";
                var settings = JsonSerializer.Deserialize<GeneratorFormUserSettings>(legacyJson)!;
                TestAssert.Equal(thickness, settings.CarbonThickness, "Legacy settings preserve the specified coating, including zero.");
                TestAssert.True(!JsonSerializer.Serialize(settings).Contains("CarbonThicknessJitterPercent", StringComparison.Ordinal),
                    "The discontinued randomization setting is not written again.");

                using var fixture = new TestFixture();
                var request = fixture.Request with
                {
                    SemEdxCondition = fixture.Request.SemEdxCondition with { CarbonCoatThickness = settings.CarbonThickness }
                };
                var jobs = fixture.Workflow.CreateMissingSimulationPlan(request, out _).Batches.SelectMany(batch => batch.Jobs).ToArray();
                TestAssert.True(jobs.All(job => job.Property.CarbonCoatThickness == thickness), "Every job keeps the specified coating.");
                string script = new SimulationScriptGenerator().Generate(jobs[0].Property);
                TestAssert.True(!script.Contains("random.uniform", StringComparison.Ordinal) && !script.Contains("effectiveThickness", StringComparison.Ordinal),
                    "Legacy randomization cannot change the generated thickness.");
            }
        }
    }
}
