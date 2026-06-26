using System;
using System.Diagnostics;
using System.IO;

namespace MineraScope
{
    // 260626Codex: Centralize the dtsa2.msi app-image layout so UI defaults and process launch stay in sync.
    internal static class DtsaMsiInstallation
    {
        public const string NotFoundMessage = "指定フォルダに DTSA-II が見つかりません。dtsa2.msi のインストール先を指定してください。";

        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dtsa2");

        public static string UseDefaultIfBlank(string? folder) =>
            string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder.Trim();

        public static bool IsUsableInstallFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            return File.Exists(GetJarPath(folder))
                && (File.Exists(GetBundledJavaPath(folder)) || File.Exists(GetLauncherPath(folder)));
        }

        public static ProcessStartInfo CreateStartInfo(string folder, string scriptPath)
        {
            if (!IsUsableInstallFolder(folder))
                throw new DirectoryNotFoundException(NotFoundMessage);

            string javaPath = GetBundledJavaPath(folder);
            string launcherPath = GetLauncherPath(folder);
            string executablePath = File.Exists(javaPath) ? javaPath : launcherPath;

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (File.Exists(javaPath))
            {
                startInfo.ArgumentList.Add("-jar");
                startInfo.ArgumentList.Add(GetJarPath(folder));
            }

            startInfo.ArgumentList.Add("--no_gui");
            startInfo.ArgumentList.Add("--script");
            startInfo.ArgumentList.Add(scriptPath);
            return startInfo;
        }

        private static string GetBundledJavaPath(string folder) =>
            Path.Combine(folder, "runtime", "bin", "java.exe");

        private static string GetJarPath(string folder) =>
            Path.Combine(folder, "app", "dtsa2.jar");

        private static string GetLauncherPath(string folder) =>
            Path.Combine(folder, "dtsa2.exe");
    }
}
