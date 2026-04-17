namespace MineraScope
{
    // 260416Codex: 鉱物・成分判定の実行手順を Form から切り離して再利用しやすくします。
    internal sealed class MineralPredictionWorkflow
    {
        private readonly string _assemblyPath;
        private readonly Action<string> _log;

        // 260416Codex: 判定実行に必要な最小依存だけを受け取るようにします。
        public MineralPredictionWorkflow(string assemblyPath, Action<string> log)
        {
            _assemblyPath = assemblyPath;
            _log = log;
        }

        // 260416Codex: ドロップされたファイルやフォルダから判定対象スペクトルだけを抽出します。
        public static string[] CollectSpectrumFiles(IEnumerable<string> droppedPaths)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in droppedPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (File.Exists(path))
                {
                    if (IsSpectrumFile(path))
                    {
                        files.Add(path);
                    }

                    continue;
                }

                if (!Directory.Exists(path))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    if (IsSpectrumFile(file))
                    {
                        files.Add(file);
                    }
                }
            }

            return files.ToArray();
        }

        // 260416Codex: 判定ボタン側からはパス一覧を渡すだけで非同期実行できるようにします。
        public Task RunAsync(string modelPath, IReadOnlyCollection<string> files)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || files.Count == 0)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
                new DeepLearning(_log).RunPrediction(modelPath, files.ToList(), _assemblyPath));
        }

        // 260416Codex: 判定対象に使う拡張子判定をワークフロー内へ集約します。
        private static bool IsSpectrumFile(string path) =>
            path.EndsWith(".msa", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".emsa", StringComparison.OrdinalIgnoreCase);
    }
}
