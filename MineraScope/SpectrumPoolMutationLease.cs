using System;
using System.Diagnostics;
using System.IO;

namespace MineraScope
{
    // 260901Codex: Hold an OS-released exclusive file handle across recovery, reservation, and simulation so two app instances cannot mutate one pool concurrently.
    internal sealed class SpectrumPoolMutationLease : IDisposable
    {
        private const string LockFileName = "spectrum-pool-mutation.lock";
        private FileStream? _stream;

        private SpectrumPoolMutationLease(FileStream stream)
        {
            _stream = stream;
        }

        // 260901Codex: Detect the currently running pre-guard executable as well as updated instances that already hold the lock file.
        public static bool TryAcquire(
            out SpectrumPoolMutationLease? lease,
            out string failureReason)
        {
            lease = null;
            int? otherProcessId = FindOtherMineraScopeProcessId();
            if (otherProcessId is not null)
            {
                failureReason =
                    $"別の MineraScope（PID {otherProcessId}）が起動中です。" +
                    "spectrum生成が終了してそのアプリを閉じてから再実行してください。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(DefaultStoragePaths.SettingsFolder);
                string lockPath = Path.Combine(DefaultStoragePaths.SettingsFolder, LockFileName);
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                // 260901Codex: The lease is held before marker inspection, so another updated run cannot launch a child during this check.
                if (!DtsaProcessRunMarker.TryEnsureNoDtsaProcessCanWrite(out failureReason))
                {
                    stream.Dispose();
                    return false;
                }

                lease = new SpectrumPoolMutationLease(stream);
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failureReason =
                    "別の処理がspectrum poolを更新中か、排他ファイルを開けません。" +
                    $"処理終了後に再実行してください。\r\n\r\n{exception.Message}";
                return false;
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        // 260901Codex: The old executable does not know the new lock file, so process detection closes that one-version compatibility gap.
        private static int? FindOtherMineraScopeProcessId()
        {
            foreach (var process in Process.GetProcessesByName("MineraScope"))
            {
                using (process)
                    if (process.Id != Environment.ProcessId)
                        return process.Id;
            }

            return null;
        }
    }
}
