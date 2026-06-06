using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MineraScope
{
    // 260605Claude: 推論側ロガーと同形式で学習側のステージ時間・GC・スレッド・メモリを即 flush で記録する。ボトルネック特定のための一次データを残す。
    internal static class TensorFlowTrainingDebugLog
    {
        private static readonly object Gate = new();
        private static readonly string SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        private static int _sessionStarted;

        // 260605Codex: Keep the folder explicit so writes do not derive it back from LogPath.
        private static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MineraScope",
            "Logs");

        public static string LogPath { get; } = Path.Combine(LogDirectory, "tf-train-debug.log");

        public static uint CurrentNativeThreadId => GetCurrentThreadId();

        public static void Write(string eventName, string details = "")
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                EnsureSessionStarted();
                AppendLine(BuildLine(eventName, details));
            }
            catch
            {
            }
        }

        public static string Clean(string value)
            => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static void EnsureSessionStarted()
        {
            if (Interlocked.Exchange(ref _sessionStarted, 1) != 0)
                return;

            AppendLine(BuildLine("session-start", $"pid={Environment.ProcessId} baseDir={Clean(AppContext.BaseDirectory)}"));
        }

        private static string BuildLine(string eventName, string details)
        {
            using var process = Process.GetCurrentProcess();
            long workingSetMb = process.WorkingSet64 / (1024 * 1024);
            long privateMb = process.PrivateMemorySize64 / (1024 * 1024);
            string cleanedDetails = Clean(details);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:O}\tsession={SessionId}\tevent={eventName}\tmanagedThread={Environment.CurrentManagedThreadId}\tnativeThread={CurrentNativeThreadId}\tprocessThreads={process.Threads.Count}\tworkingSetMB={workingSetMb}\tprivateMB={privateMb}\tgc0={GC.CollectionCount(0)}\tgc1={GC.CollectionCount(1)}\tgc2={GC.CollectionCount(2)}\t{cleanedDetails}");
        }

        private static void AppendLine(string line)
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
