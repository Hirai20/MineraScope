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
            // 260416Codex: 起動時は Form1 ではなく LauncherForm を開く
            Application.Run(new LauncherForm());
        }
    }
}
