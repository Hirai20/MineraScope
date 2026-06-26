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

        // 260621Codex: VS 実行でも TensorFlow/oneDNN 初期化前に、この PC 向けの既定スレッド数を process env にだけ適用します。
        // 260626Claude: 実験用の上書きノブ MINERASCOPE_TF_THREADS / MINERASCOPE_CLASSIFICATION_LOAD_PARALLELISM を撤去。
        //   スレッド数はコア数由来の既定式で決め、読込並列度は SpectrumDataLoader 側の既定 (Clamp(コア数,2,8)) に委ねる。
        private static void ApplyTensorFlowStartupDefaults()
        {
            int threadCount = ResolveTensorFlowThreadCount();
            var values = new Dictionary<string, string>
            {
                ["TF_NUM_INTRAOP_THREADS"] = threadCount.ToString(),
                ["TF_NUM_INTEROP_THREADS"] = ResolveInterOpThreadCount(threadCount).ToString(),
                ["OMP_NUM_THREADS"] = threadCount.ToString()
            };

            var applied = values.Select(pair => SetDefaultEnvironmentVariable(pair.Key, pair.Value));
            TensorFlowTrainingDebugLog.Write(
                "tf-startup-defaults",
                $"processorCount={Environment.ProcessorCount} threads={threadCount} {string.Join(" ", applied)}");
        }

        // 260626Claude: コア数からこの PC の既定スレッド数を決める (外部上書きは廃止)。
        private static int ResolveTensorFlowThreadCount()
        {
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
