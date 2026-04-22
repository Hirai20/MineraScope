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
            // 260416Codex: 起動時は生成画面と解析画面を選べる LauncherForm を開きます。
            Application.Run(new LauncherForm());
        }
    }
}
