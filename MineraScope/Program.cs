namespace MineraScope
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 260621Codex: VS 実行でも TensorFlow/oneDNN が初期化される前に CPU スレッド既定値を入れます。
            ApplyTensorFlowStartupDefaults();

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            // 260609Claude: 開発用ヘッドレス。env で GUI を出さず学習系を回して終了する。結果は tf-train-debug.log。例外は debug log へ残す。
            if (Environment.GetEnvironmentVariable("MINERASCOPE_HEADLESS_TRAIN") == "1")
            {
                RunHeadless("train", () => DeepLearning.RunHeadlessSmokeTest(Console.WriteLine));
                return;
            }
            // 260416Codex: Open FormMain at startup.
            Application.Run(new FormMain());
        }

        // 260621Codex: 外部環境変数があれば尊重し、無ければこの PC でまず試す既定値を process env にだけ適用します。
        private static void ApplyTensorFlowStartupDefaults()
        {
            string? mode = Environment.GetEnvironmentVariable("MINERASCOPE_TF_THREADS");
            if (IsDisabled(mode))
            {
                TensorFlowTrainingDebugLog.Write("tf-startup-defaults", "status=disabled switch=MINERASCOPE_TF_THREADS");
                return;
            }

            int threadCount = ResolveTensorFlowThreadCount(mode);
            var values = new Dictionary<string, string>
            {
                ["TF_NUM_INTRAOP_THREADS"] = threadCount.ToString(),
                ["TF_NUM_INTEROP_THREADS"] = ResolveInterOpThreadCount(threadCount).ToString(),
                ["OMP_NUM_THREADS"] = threadCount.ToString(),
                ["MINERASCOPE_CLASSIFICATION_LOAD_PARALLELISM"] = threadCount.ToString()
            };

            var applied = values.Select(pair => SetDefaultEnvironmentVariable(pair.Key, pair.Value));
            TensorFlowTrainingDebugLog.Write(
                "tf-startup-defaults",
                $"processorCount={Environment.ProcessorCount} requested={TensorFlowTrainingDebugLog.Clean(mode ?? "auto")} {string.Join(" ", applied)}");
        }

        private static bool IsDisabled(string? value) =>
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || value == "0";

        private static int ResolveTensorFlowThreadCount(string? value)
        {
            if (int.TryParse(value, out int requested) && requested > 0)
                return requested;

            int logicalProcessors = Environment.ProcessorCount;
            return logicalProcessors <= 4
                ? Math.Max(1, logicalProcessors)
                : Math.Max(1, Math.Min(16, logicalProcessors / 2));
        }

        private static int ResolveInterOpThreadCount(int intraOpThreadCount) =>
            intraOpThreadCount >= 8 ? 2 : 1;

        private static string SetDefaultEnvironmentVariable(string name, string value)
        {
            string? existing = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(existing))
                return $"{name}={TensorFlowTrainingDebugLog.Clean(existing)}(existing)";

            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            return $"{name}={value}(default)";
        }

        // 260609Claude: ヘッドレス実行の例外を握りつぶさず debug log に残す(WinExe はコンソール出力が無いため)。
        private static void RunHeadless(string mode, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                TensorFlowTrainingDebugLog.Write("headless-exception", $"mode={mode} type={ex.GetType().Name} message={TensorFlowTrainingDebugLog.Clean(ex.Message)} stack={TensorFlowTrainingDebugLog.Clean(ex.StackTrace ?? string.Empty)}");
            }
        }
    }
}
