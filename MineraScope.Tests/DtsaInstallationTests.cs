// 261106Codex: 検証済みの公式DTSA-II直接起動を公開版へ移す。
using System.Diagnostics;
using System.IO.Compression;

namespace MineraScope.Tests
{
    // 261030Claude: NIST 公式版の配置判定・版の選択・起動引数を、偽のフォルダ構成で DTSA-II を起動せずに検証する。
    internal static class DtsaInstallationTests
    {
        public static void Run()
        {
            using var fixture = new TestFixture();
            string nist = Path.Combine(fixture.Root, "NIST");
            string polaris = CreateInstallation(nist, "NIST DTSA-II Polaris 2026-09-10", "15.1.57", "2.7.4");
            string oberon = CreateInstallation(nist, "NIST DTSA-II Oberon 2024-06-11", null, "2.7.3");
            // 261030Claude: 日付が新しくても、同梱 Java の無い配置は自動検出で選ばない。
            string withoutJava = CreateInstallation(nist, "NIST DTSA-II Zeta 2027-01-01", "16.0.0", "2.7.5");
            File.Delete(Path.Combine(withoutJava, "java-runtime", "bin", "java.exe"));
            string msi = Path.Combine(fixture.Root, "dtsa2");
            CreateEmptyFile(Path.Combine(msi, "app", "dtsa2.jar"));
            CreateEmptyFile(Path.Combine(msi, "runtime", "bin", "java.exe"));

            TestAssert.True(DtsaInstallation.GetValidationError(polaris) is null, "A Polaris layout with versioned jar names is accepted.");
            TestAssert.True(DtsaInstallation.GetValidationError(oberon) is null, "An Oberon layout with unversioned jar names is accepted.");
            TestAssert.Equal(DtsaInstallation.JavaNotFoundMessage, DtsaInstallation.GetValidationError(withoutJava),
                "A layout without the bundled Java reports the missing java.exe.");
            TestAssert.Equal(DtsaInstallation.NotFoundMessage, DtsaInstallation.GetValidationError(msi),
                "The former MSI layout is not treated as the official DTSA-II.");
            TestAssert.Equal(DtsaInstallation.NotFoundMessage, DtsaInstallation.GetValidationError(" "), "A blank folder is refused.");

            TestAssert.Equal(polaris, DtsaInstallation.FindNewestInstallation(nist), "The usable installation with the newest folder date is chosen.");
            TestAssert.Equal(oberon, DtsaInstallation.ResolveFolder(" " + oberon + " ", nist), "A usable saved folder is kept.");
            TestAssert.Equal(polaris, DtsaInstallation.ResolveFolder(msi, nist), "An unusable saved folder is replaced by the detected installation.");
            TestAssert.Equal(polaris, DtsaInstallation.ResolveFolder(null, nist), "A blank saved folder is replaced by the detected installation.");
            TestAssert.Equal(msi, DtsaInstallation.ResolveFolder(msi, Path.Combine(fixture.Root, "missing")),
                "Without any installation the saved folder is kept so that validation can explain the problem.");

            string scriptPath = Path.Combine(fixture.Root, "scripts", "job.py");
            ProcessStartInfo startInfo = DtsaInstallation.CreateStartInfo(polaris, scriptPath);
            string[] arguments = [.. startInfo.ArgumentList];
            TestAssert.Equal(Path.Combine(polaris, "java-runtime", "bin", "java.exe"), startInfo.FileName, "The bundled Java is used.");
            TestAssert.Equal(Path.GetDirectoryName(scriptPath), startInfo.WorkingDirectory, "The process runs outside the DTSA-II folder.");
            TestAssert.Equal("--enable-preview", arguments[0], "Preview features are enabled for the Polaris EPQ classes.");
            TestAssert.True(arguments.Contains("-Dpython.cachedir.skip=false"), "The Jython package cache is enabled.");
            string cacheArgument = arguments.Single(static argument => argument.StartsWith("-Dpython.cachedir=", StringComparison.Ordinal));
            TestAssert.True(!cacheArgument.Contains(nist, StringComparison.OrdinalIgnoreCase), "The Jython cache is not written into the DTSA-II folder.");
            string[] classPath = arguments[Array.IndexOf(arguments, "-cp") + 1].Split(Path.PathSeparator);
            TestAssert.SequenceEqual(
                ["dtsa2-15.1.57.jar", "epq-15.1.57.jar", "jython-standalone-2.7.4.jar"],
                classPath.Select(static path => Path.GetFileName(path)),
                "Each jar except the unit-test jars is listed in name order.");
            TestAssert.SequenceEqual(["org.python.util.jython", scriptPath], arguments[^2..], "Jython runs the generated script directly.");
            TestAssert.Throws<DirectoryNotFoundException>(() => DtsaInstallation.CreateStartInfo(msi, scriptPath),
                "An unusable folder is refused before the process starts.");

            TestAssert.True(DtsaInstallation.Describe(polaris).EndsWith("epqVersion=15.1.57", StringComparison.Ordinal),
                "The EPQ Implementation-Version is recorded.");
            TestAssert.True(DtsaInstallation.Describe(oberon).EndsWith("epqVersion=unknown", StringComparison.Ordinal),
                "An EPQ jar without a version is recorded as unknown.");
        }

        // 261030Claude: 配置判定に使う名前だけを再現する。jar の中身は版の読み取りに使う epq の manifest だけを作る。
        //   Polaris は jar 名と manifest の両方に版番号を持ち、Oberon はどちらにも持たない。
        private static string CreateInstallation(string nist, string name, string? version, string jythonVersion)
        {
            string folder = Path.Combine(nist, name);
            string suffix = version is null ? string.Empty : "-" + version;
            CreateEmptyFile(Path.Combine(folder, "java-runtime", "bin", "java.exe"));
            CreateEmptyFile(Path.Combine(folder, $"dtsa2{suffix}.jar"));
            CreateEmptyFile(Path.Combine(folder, $"jython-standalone-{jythonVersion}.jar"));
            CreateEmptyFile(Path.Combine(folder, "junit-4.13.2.jar"));
            CreateEmptyFile(Path.Combine(folder, "hamcrest-core-1.3.jar"));

            using ZipArchive epq = ZipFile.Open(Path.Combine(folder, $"epq{suffix}.jar"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(epq.CreateEntry("META-INF/MANIFEST.MF").Open());
            writer.Write("Manifest-Version: 1.0\r\n");
            if (version is not null)
                writer.Write($"Implementation-Version: {version}\r\n");

            return folder;
        }

        private static void CreateEmptyFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
    }
}
