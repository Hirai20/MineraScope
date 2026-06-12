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
