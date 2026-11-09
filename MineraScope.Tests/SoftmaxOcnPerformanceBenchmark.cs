using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 261106Codex: 保存済み分類器のコピーだけでOCNを再学習し、同じ入力で時間と採用重みを比較する任意検証。
    internal static class SoftmaxOcnPerformanceBenchmark
    {
        // 261106Codex: コピーの準備と時間を測る処理を分け、計測範囲を明示する。
        internal static void Run(string sourceModel, string datasetFolder, string seedText, string outputFolder)
        {
            int seed = int.Parse(seedText, CultureInfo.InvariantCulture);
            if (seed < 0) throw new ArgumentOutOfRangeException(nameof(seedText));
            string output = Path.GetFullPath(outputFolder);
            string model = PrepareModelCopy(sourceModel, output);
            // 261109Codex: 少数CPUでは全数、それ以外では半数を使い、同じ1～16の範囲に収める。
            int processorCount = Environment.ProcessorCount;
            int threads = Math.Clamp(processorCount <= 4 ? processorCount : processorCount / 2, 1, 16);
            foreach (var pair in new Dictionary<string, int>
            {
                ["TF_NUM_INTRAOP_THREADS"] = threads,
                ["TF_NUM_INTEROP_THREADS"] = threads >= 8 ? 2 : 1,
                ["OMP_NUM_THREADS"] = threads
            })
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(pair.Key)))
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                var process = Process.GetCurrentProcess();
                TimeSpan cpuStart = process.TotalProcessorTime;
                var clock = Stopwatch.StartNew();
                double? firstEpochSeconds = null;
                SoftmaxOcnHeadTraining.TrainAndSave(model, datasetFolder, seed, "OCN performance comparison",
                    cancellation.Token, message =>
                    {
                        if (message.StartsWith("分類＋OCN head: epoch 1,", StringComparison.Ordinal))
                            firstEpochSeconds = clock.Elapsed.TotalSeconds;
                        string line = $"{clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}s {message}";
                        Console.WriteLine(line);
                        File.AppendAllText(Path.Combine(output, "benchmark.log"), line + Environment.NewLine);
                    });
                clock.Stop();
                var result = new
                {
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    FirstEpochSeconds = firstEpochSeconds,
                    CpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds,
                    Environment.ProcessorCount,
                    Dataset = ClassificationTrainingDataset.ReadInfo(datasetFolder, cancellation.Token).NormalizedDataSha256,
                    Artifact = SoftmaxOcnHeadArtifact.Load(model)
                };
                File.WriteAllText(Path.Combine(output, "benchmark-result.json"), JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"OCN benchmark completed: {clock.Elapsed.TotalSeconds:F3}s; {output}");
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }

        // 261106Codex: 出力先と最深パスを検査してから分類器を複製し、既存モデルの上書きを防ぐ。
        private static string PrepareModelCopy(string sourceModel, string output)
        {
            if (Directory.Exists(output) || File.Exists(output))
                throw new IOException("検証の出力先には存在しないフォルダーを指定してください。");
            string source = Path.GetFullPath(sourceModel);
            // 261109Codex: 相対パスを一度だけ求め、保存先の長さ検査とコピーで共用する。
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).StartsWith(SoftmaxOcnHeadArtifact.FileName, StringComparison.Ordinal))
                .Select(path => (Source: path, RelativePath: Path.GetRelativePath(source, path))).ToArray();
            string model = Path.Combine(output, "model");
            // 261109Codex: 公開版の既存APIで同じパス長検査を行い、長すぎる場合はコピー前に止める。
            string? pathError = GeneratedPathPolicy.FindTooLong(output, Math.Max("benchmark-result.json".Length,
                files.Max(file => Path.Combine("model", file.RelativePath).Length)), "OCN速度比較");
            if (pathError is not null)
                throw new PathTooLongException(pathError);
            foreach (var file in files)
            {
                string destination = Path.Combine(model, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file.Source, destination, overwrite: false);
            }
            return model;
        }
    }
}
