namespace MineraScope
{
    // 260416Codex: ドロップされたファイルやフォルダから判定対象スペクトルだけを抽出します。
    // 260727Claude: 旧 MineralPredictionWorkflow から改名。判定の実行は 6ba8adc (260621) で
    //   SpectrumBatchPredictionWorkflow へ移っており、このクラスに残るのはドロップパスの収集だけなので、
    //   実態に合わせて名前を「スペクトルファイル収集」へ寄せた。
    internal static class SpectrumFileCollector
    {
        // 260416Codex: ドロップされたファイルやフォルダから判定対象スペクトルだけを抽出します。
        public static string[] Collect(IEnumerable<string> droppedPaths)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in droppedPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (File.Exists(path))
                {
                    if (IsSpectrumFile(path))
                        files.Add(path);

                    continue;
                }

                if (!Directory.Exists(path))
                    continue;

                foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    if (IsSpectrumFile(file))
                        files.Add(file);
                }
            }

            return files.ToArray();
        }

        // 260416Codex: 判定対象に使う拡張子判定をワークフロー内へ集約します。
        // 260613Claude: バイナリ .eds も判定対象に含める。
        private static bool IsSpectrumFile(string path) =>
            path.EndsWith(".msa", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".emsa", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".eds", StringComparison.OrdinalIgnoreCase);
    }
}
