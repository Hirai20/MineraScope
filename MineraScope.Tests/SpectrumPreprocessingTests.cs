namespace MineraScope.Tests
{
    // 260930Codex: モデルのメタデータ破損によって学習マスクを暗黙に無効化しない。
    // 261016Codex: 公開版でも旧形式の互換性と不正な前処理記録の拒否を検証する。
    internal static class SpectrumPreprocessingTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), $"MineraScopePreprocessing_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                TestAssert.Equal(SpectrumPreprocessing.None, SpectrumPreprocessing.LoadFromModelFolder(root), "Legacy models without metadata remain compatible.");
                foreach (var settings in new[] { SpectrumPreprocessing.None, SpectrumPreprocessing.ForTraining(), new SpectrumPreprocessing(36, LowEnergyMaskOrder.Post) })
                {
                    settings.WriteToModelFolder(root);
                    TestAssert.Equal(settings, SpectrumPreprocessing.LoadFromModelFolder(root), "Saved preprocessing must round-trip.");
                }

                string path = Path.Combine(root, SpectrumPreprocessing.FileName);
                File.WriteAllText(path, """{"Version":1,"LowEnergyMask":{"CutoffChannels":36,"Order":"POST"}}""");
                TestAssert.Equal(new SpectrumPreprocessing(36, LowEnergyMaskOrder.Post), SpectrumPreprocessing.LoadFromModelFolder(root), "Existing case-insensitive metadata remains compatible.");

                string[] invalid =
                [
                    "{", "null", "[]", "{}",
                    """{"version":2,"lowEnergyMask":null}""",
                    """{"version":"1","lowEnergyMask":null}""",
                    """{"version":1}""",
                    """{"version":1,"lowEnergyMask":false}""",
                    """{"version":1,"lowEnergyMask":{}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":"36","order":"pre"}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":0,"order":"pre"}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":-1,"order":"pre"}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":2147483648,"order":"pre"}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":36,"order":null}}""",
                    """{"version":1,"lowEnergyMask":{"cutoffChannels":36,"order":"future"}}""",
                    $"{{\"version\":1,\"lowEnergyMask\":{{\"cutoffChannels\":{SpectrumAxis.Default.ChannelCount},\"order\":\"pre\"}}}}"
                ];
                foreach (string json in invalid)
                {
                    File.WriteAllText(path, json);
                    TestAssert.Throws<InvalidDataException>(() => SpectrumPreprocessing.LoadFromModelFolder(root), $"Reject invalid preprocessing: {json}");
                }
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    TestAssert.Throws<InvalidDataException>(() => SpectrumPreprocessing.LoadFromModelFolder(root), "An unreadable existing record must not disable masking.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
