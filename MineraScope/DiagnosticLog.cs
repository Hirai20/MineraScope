using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;

namespace MineraScope
{
    // 260606Claude: 推論側(tf-predict-debug)と学習側(tf-train-debug)で重複していた診断ロガーの実体を 1 つへ集約する。
    // クラッシュ耐性のため毎回 flush し、時刻・session・スレッド・プロセススレッド数・メモリ・GC 回数を 1 行へ記録する。
    internal sealed class DiagnosticLog(string fileName)
    {
        private readonly object _gate = new();
        private readonly string _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        private int _sessionStarted;

        // 260605Codex: Keep the folder explicit so writes do not derive it back from LogPath.
        private static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MineraScope",
            "Logs");

        private string LogPath { get; } = Path.Combine(LogDirectory, fileName);

        public static uint CurrentNativeThreadId => GetCurrentThreadId();

        public static string Clean(string value)
            => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        public void Write(string eventName, string details = "")
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

        private void EnsureSessionStarted()
        {
            if (Interlocked.Exchange(ref _sessionStarted, 1) != 0)
                return;

            AppendLine(BuildLine("session-start", $"pid={Environment.ProcessId} baseDir={Clean(AppContext.BaseDirectory)}"));
        }

        private string BuildLine(string eventName, string details)
        {
            using var process = Process.GetCurrentProcess();
            long workingSetMb = process.WorkingSet64 / (1024 * 1024);
            long privateMb = process.PrivateMemorySize64 / (1024 * 1024);
            // 260621Codex: CPU をどれだけ使えたか後で集計できるよう、プロセス累積 CPU 時間も残します。
            long processCpuMs = (long)process.TotalProcessorTime.TotalMilliseconds;
            // 260608Claude: Server GC 計測用に GC モード・累積 pause 時間・累積 alloc を追加する。precise:false は計測自身が GC 圧/コストを足さないため。
            long gcPauseMs = (long)GC.GetTotalPauseDuration().TotalMilliseconds;
            long allocMb = GC.GetTotalAllocatedBytes(false) / (1024 * 1024);
            string cleanedDetails = Clean(details);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:O}\tsession={_sessionId}\tevent={eventName}\tmanagedThread={Environment.CurrentManagedThreadId}\tnativeThread={CurrentNativeThreadId}\tprocessThreads={process.Threads.Count}\tprocessCpuMs={processCpuMs}\tworkingSetMB={workingSetMb}\tprivateMB={privateMb}\tgc0={GC.CollectionCount(0)}\tgc1={GC.CollectionCount(1)}\tgc2={GC.CollectionCount(2)}\tgcServer={GCSettings.IsServerGC}\tgcPauseMs={gcPauseMs}\tallocMB={allocMb}\t{cleanedDetails}");
        }

        private void AppendLine(string line)
        {
            lock (_gate)
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
