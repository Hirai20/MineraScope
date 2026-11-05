// 261106Codex: 検証済みの公式DTSA-II直接起動を公開版へ移す。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MineraScope
{
    // 260626Codex: dtsa2.msiのアプリイメージ配置を一元化し、UIの既定値とプロセス起動を一致させる。
    // 261030Claude: 改変版 (DTSA2Plus の MSI 版) をやめ、NIST 公式版を改変せずに使う。GUI・DTSA-II.exe・--script を通さず、
    //   同梱の Java で Jython を直接起動して DTSA-II の jar を部品として読む。Derby の DB を開かないので、並列起動でも DB は競合しない。
    internal static class DtsaInstallation
    {
        public const string NotFoundMessage = "指定フォルダに NIST 公式版 DTSA-II が見つかりません。インストール先（例: %LOCALAPPDATA%\\NIST\\NIST DTSA-II Polaris 2026-09-10）を指定してください。";
        public const string JavaNotFoundMessage = "DTSA-II は見つかりましたが、同梱の java-runtime\\bin\\java.exe がありません。NIST 公式版 DTSA-II を入れ直してください。";

        // 261030Claude: DTSA-II 本体の Main-Class は GUI を開くので使わず、Jython の起動クラスを直接指定する。
        private const string JythonMainClass = "org.python.util.jython";
        // 261030Claude: NIST のインストーラーは既定で %LOCALAPPDATA%\NIST\NIST DTSA-II <版名> <yyyy-MM-dd> に入れる。
        private const string InstallFolderSearchPattern = "NIST DTSA-II *";
        private const string InstallFolderDateFormat = "yyyy-MM-dd";
        private const string ImplementationVersionKey = "Implementation-Version:";
        // 261030Claude: 単体テスト用の jar は Jython の起動に要らないので classpath から外す。
        private static readonly string[] ExcludedJarNames = ["junit", "hamcrest-core"];

        private static string NistFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NIST");

        // 261030Claude: Jython の package cache の置き場所。公式版のフォルダへは書き込まない。
        private static string JythonCacheFolder => Path.Combine(DefaultStoragePaths.SettingsFolder, "JythonCache");

        // 261030Claude: 保存値が使えるならそれを優先し、空か使えないとき (旧 MSI 版の %LOCALAPPDATA%\dtsa2 など) だけ自動検出した版に置き換える。
        //   検出できなければ入力値を返し、実行前の検査で利用者へ理由を示す。
        public static string ResolveFolder(string? folder) => ResolveFolder(folder, NistFolder);

        internal static string ResolveFolder(string? folder, string nistFolder)
        {
            string trimmed = folder?.Trim() ?? string.Empty;
            if (GetValidationError(trimmed) is null)
                return trimmed;

            return FindNewestInstallation(nistFolder) ?? trimmed;
        }

        // 261030Claude: 複数の版が入っていれば、フォルダ名の日付が最も新しい使える版を選ぶ。日付の無い名前は後回しにする。
        internal static string? FindNewestInstallation(string nistFolder)
        {
            string[] candidates;
            try
            {
                candidates = Directory.Exists(nistFolder)
                    ? Directory.GetDirectories(nistFolder, InstallFolderSearchPattern)
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return candidates
                .Where(static candidate => GetValidationError(candidate) is null)
                .OrderByDescending(ReadInstallDate)
                .ThenByDescending(static candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        // 260727Claude: 参照 0 の forwarding helper (IsUsableInstallFolder) を削除。呼び出し側は GetValidationError を直接使う。
        // 261030Claude: Jython の起動に要る EPQ と Jython の jar、同梱 Java だけを確かめる。
        public static string? GetValidationError(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return NotFoundMessage;

            string[] jarPaths = GetJarPaths(folder);
            if (FindJar(jarPaths, "epq") is null || FindJar(jarPaths, "jython-standalone") is null)
                return NotFoundMessage;

            return File.Exists(GetJavaPath(folder)) ? null : JavaNotFoundMessage;
        }

        public static ProcessStartInfo CreateStartInfo(string folder, string scriptPath)
        {
            string? validationError = GetValidationError(folder);
            if (validationError is not null)
                throw new DirectoryNotFoundException(validationError);

            // 260626Codex: 標準出力のマーカーを安定して使えるコンソール版java.exeを優先し、スクリプト実行後の終了が不安定なdtsa2.exeを避ける。
            ProcessStartInfo startInfo = new()
            {
                FileName = GetJavaPath(folder),
                // 261030Claude: DB を使わないので公式版のフォルダで動かす必要はない。書き込みが起きても MineraScope 側に残るよう、スクリプトの場所にする。
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? DefaultStoragePaths.SettingsFolder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // 261030Claude: Polaris (同梱 Java 26) の EPQ はプレビュー機能付きでコンパイルされており、付けないと UnsupportedClassVersionError で止まる。
            //   Oberon (同梱 Java 21) に付けても動くことを確認済みなので、版で分けない。
            startInfo.ArgumentList.Add("--enable-preview");
            // 261030Claude: package cache を有効にしないと、Jython が EPQ のパッケージを import できない。
            startInfo.ArgumentList.Add("-Dpython.cachedir=" + JythonCacheFolder);
            startInfo.ArgumentList.Add("-Dpython.cachedir.skip=false");
            // 261030Claude: <folder>\* の指定でも同じく import できないため、jar を 1 つずつ列挙する。
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(string.Join(Path.PathSeparator, GetClassPath(folder)));
            startInfo.ArgumentList.Add(JythonMainClass);
            startInfo.ArgumentList.Add(scriptPath);
            return startInfo;
        }

        // 261030Claude: 生成に使った版を run ログへ残す。Oberon の epq.jar には版番号が無いので、版名と日付を含むフォルダの場所を併記する。
        public static string Describe(string folder) =>
            $"folder={folder} epqVersion={TryReadEpqVersion(folder) ?? "unknown"}";

        private static string GetJavaPath(string folder) =>
            Path.Combine(folder, "java-runtime", "bin", "java.exe");

        private static IEnumerable<string> GetClassPath(string folder) =>
            GetJarPaths(folder).Where(static path => !ExcludedJarNames.Any(name => IsJar(path, name)));

        // 261030Claude: jar 名は版ごとに変わるので固定名で探さない。名前順に並べ、同じ配置なら毎回同じ classpath にする。
        private static string[] GetJarPaths(string folder)
        {
            try
            {
                if (!Directory.Exists(folder))
                    return [];

                return Directory.GetFiles(folder, "*.jar")
                    .Where(static path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return [];
            }
        }

        private static string? FindJar(string[] jarPaths, string baseName) =>
            jarPaths.FirstOrDefault(path => IsJar(path, baseName));

        // 261030Claude: 版番号の有無 (Polaris の epq-15.1.57.jar、Oberon の epq.jar) にかかわらず同じ部品として扱う。
        private static bool IsJar(string path, string baseName)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(baseName + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime ReadInstallDate(string folder)
        {
            string name = Path.GetFileName(folder);
            return DateTime.TryParseExact(
                name[(name.LastIndexOf(' ') + 1)..],
                InstallFolderDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date)
                ? date
                : DateTime.MinValue;
        }

        private static string? TryReadEpqVersion(string folder)
        {
            string? epqJarPath = FindJar(GetJarPaths(folder), "epq");
            if (epqJarPath is null)
                return null;

            try
            {
                using ZipArchive archive = ZipFile.OpenRead(epqJarPath);
                ZipArchiveEntry? manifestEntry = archive.GetEntry("META-INF/MANIFEST.MF");
                if (manifestEntry is null)
                    return null;

                using var reader = new StreamReader(manifestEntry.Open());
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.StartsWith(ImplementationVersionKey, StringComparison.OrdinalIgnoreCase))
                        return line[ImplementationVersionKey.Length..].Trim();
                }

                return null;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
