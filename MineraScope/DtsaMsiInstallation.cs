using System.Diagnostics;
using System.IO;

namespace MineraScope
{
    // 260626Codex: Centralize the dtsa2.msi app-image layout so UI defaults and process launch stay in sync.
    internal static class DtsaMsiInstallation
    {
        public const string NotFoundMessage = "指定フォルダに DTSA-II が見つかりません。dtsa2.msi のインストール先を指定してください。";
        public const string JavaNotFoundMessage = "DTSA-II は見つかりましたが、実行用の java.exe が見つかりません。DTSA-II の runtime\\bin\\java.exe または PATH 上の Java を確認してください。";

        public static string DefaultFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dtsa2");

        public static string UseDefaultIfBlank(string? folder) =>
            string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder.Trim();

        public static bool IsUsableInstallFolder(string? folder) =>
            GetValidationError(folder) is null;

        public static string? GetValidationError(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !File.Exists(GetJarPath(folder)))
                return NotFoundMessage;

            return FindJavaExecutable(folder) is null ? JavaNotFoundMessage : null;
        }

        public static ProcessStartInfo CreateStartInfo(string folder, string scriptPath)
        {
            string? validationError = GetValidationError(folder);
            if (validationError is not null)
                throw new DirectoryNotFoundException(validationError);

            // 260626Codex: Prefer a console java.exe so stdout markers remain reliable; dtsa2.exe can run scripts but may not exit cleanly.
            string executablePath = FindJavaExecutable(folder)!;

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(GetJarPath(folder));
            startInfo.ArgumentList.Add("--no_gui");
            startInfo.ArgumentList.Add("--script");
            startInfo.ArgumentList.Add(scriptPath);
            return startInfo;
        }

        private static string GetBundledJavaPath(string folder) =>
            Path.Combine(folder, "runtime", "bin", "java.exe");

        private static string GetJarPath(string folder) =>
            Path.Combine(folder, "app", "dtsa2.jar");

        private static string? FindJavaExecutable(string folder)
        {
            string bundledJavaPath = GetBundledJavaPath(folder);
            if (File.Exists(bundledJavaPath))
                return bundledJavaPath;

            return FindExecutableOnPath("java.exe");
        }

        private static string? FindExecutableOnPath(string fileName)
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
                return null;

            foreach (string folder in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;

                string candidate = Path.Combine(folder.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
