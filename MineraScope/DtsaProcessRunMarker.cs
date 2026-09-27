using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MineraScope
{
    // 260901Codex: Persist one marker per DTSA-II Java attempt so a restarted app can refuse recovery while an orphan still writes EMSA files.
    internal sealed class DtsaProcessRunMarker : IDisposable
    {
        private const string MarkerSearchPattern = "dtsa-run-*.json";
        private static readonly object RegistryLock = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _markerPath;
        private readonly MarkerDocument _document;
        private Process? _process;
        private bool _startFailed;

        private DtsaProcessRunMarker(string markerPath, MarkerDocument document)
        {
            _markerPath = markerPath;
            _document = document;
        }

        // 260901Codex: Write the unresolved marker before Process.Start, closing the crash gap before a child PID exists.
        public static DtsaProcessRunMarker CreatePending(string scriptName)
        {
            Directory.CreateDirectory(MarkerFolder);
            string markerPath = Path.Combine(MarkerFolder, $"dtsa-run-{Guid.NewGuid():N}.json");
            using var currentProcess = Process.GetCurrentProcess();
            var document = new MarkerDocument
            {
                OwnerProcessId = Environment.ProcessId,
                OwnerStartTimeUtcTicks = TryGetStartTimeUtcTicks(currentProcess),
                CreatedUtc = DateTimeOffset.UtcNow,
                ScriptName = scriptName
            };
            var marker = new DtsaProcessRunMarker(markerPath, document);
            marker.Persist();
            return marker;
        }

        // 260901Codex: Start and durably identify the child as one operation; failed registration kills it before the attempt can continue.
        public void StartAndRegister(Process process)
        {
            ArgumentNullException.ThrowIfNull(process);
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("DTSA-II process did not start.");
            }
            catch
            {
                _startFailed = true;
                TryDeleteMarker();
                throw;
            }

            _process = process;
            _document.ChildProcessId = process.Id;
            _document.ChildStartTimeUtcTicks = TryGetStartTimeUtcTicks(process);
            _document.ChildExecutablePath = TryGetExecutablePath(process);

            try
            {
                Persist();
            }
            catch
            {
                // 260901Codex: An unregistered writer is unsafe; kill its tree and retain the pending marker unless exit is confirmed.
                if (TryKillAndConfirmExit(process))
                    TryDeleteMarker();
                throw;
            }
        }

        // 260901Codex: Inspect markers under the exclusive pool lease, then bridge the currently running old build with a conservative java.exe check.
        public static bool TryEnsureNoDtsaProcessCanWrite(out string failureReason)
        {
            var markerPaths = new List<string>();
            try
            {
                if (Directory.Exists(MarkerFolder))
                    markerPaths.AddRange(Directory.EnumerateFiles(MarkerFolder, MarkerSearchPattern));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failureReason = $"DTSA-II process markerを確認できません。\r\n\r\n{exception.Message}";
                return false;
            }

            foreach (string markerPath in markerPaths)
            {
                if (!TryRead(markerPath, out var document)
                    || document.ChildProcessId is null)
                    continue;

                if (!IsRecordedProcessRunning(document.ChildProcessId.Value, document.ChildStartTimeUtcTicks))
                    continue;

                failureReason =
                    $"前回のDTSA-II process（PID {document.ChildProcessId}）がまだ動作中です。" +
                    "終了を確認してからspectrum poolの復旧を再実行してください。";
                return false;
            }

            // 260901Codex: The active pre-marker build can only be covered conservatively; any remaining java.exe may be its orphan.
            int? untrackedJavaProcessId = FindJavaProcessId();
            if (untrackedJavaProcessId is not null)
            {
                failureReason =
                    $"java.exe（PID {untrackedJavaProcessId}）が動作中です。" +
                    "旧版DTSA-IIの残存processと区別できないため、DTSA-II生成中でないことを確認し、" +
                    "必要ならJava処理を終了してから再実行してください。";
                return false;
            }

            // 260901Codex: With no Java writer left, unresolved, corrupt, exited, or PID-reused markers are stale and safe to remove.
            foreach (string markerPath in markerPaths)
            {
                try
                {
                    DeleteMarkerFiles(markerPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failureReason = $"終了済みDTSA-II process markerを整理できません。\r\n\r\n{exception.Message}";
                    return false;
                }
            }

            failureReason = string.Empty;
            return true;
        }

        public void Dispose()
        {
            if (_startFailed || _process is null || IsProcessExitConfirmed(_process))
                TryDeleteMarker();
        }

        private static string MarkerFolder =>
            Path.Combine(DefaultStoragePaths.SettingsFolder, "DtsaProcessMarkers");

        private void Persist()
        {
            lock (RegistryLock)
                DurableJsonFile.Replace(_markerPath, JsonSerializer.Serialize(_document, JsonOptions));
        }

        // 260901Codex: Cleanup failure leaves a conservative stale marker for the next exclusive lease inspection and must not replace the attempt result.
        private void TryDeleteMarker()
        {
            try
            {
                lock (RegistryLock)
                    DeleteMarkerFiles(_markerPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }

        private static void DeleteMarkerFiles(string markerPath)
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);

            string backupPath = DurableJsonFile.GetBackupPath(markerPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }

        private static bool TryRead(string markerPath, out MarkerDocument document)
        {
            try
            {
                document = JsonSerializer.Deserialize<MarkerDocument>(File.ReadAllText(markerPath)) ?? new MarkerDocument();
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                document = new MarkerDocument();
                return false;
            }
        }

        private static bool IsRecordedProcessRunning(int processId, long? expectedStartTimeUtcTicks)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return false;
                if (expectedStartTimeUtcTicks is null)
                    return true;

                long? actualStartTimeUtcTicks = TryGetStartTimeUtcTicks(process);
                return actualStartTimeUtcTicks is null
                    || Math.Abs(actualStartTimeUtcTicks.Value - expectedStartTimeUtcTicks.Value) <= TimeSpan.TicksPerSecond;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
            {
                return true;
            }
        }

        private static bool TryKillAndConfirmExit(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                return process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
            {
                return false;
            }
        }

        private static bool IsProcessExitConfirmed(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }

        private static long? TryGetStartTimeUtcTicks(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return null;
            }
        }

        private static string? TryGetExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return null;
            }
        }

        private static int? FindJavaProcessId()
        {
            foreach (var process in Process.GetProcessesByName("java"))
            {
                using (process)
                    return process.Id;
            }

            return null;
        }

        private sealed class MarkerDocument
        {
            public int OwnerProcessId { get; set; }
            public long? OwnerStartTimeUtcTicks { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public string ScriptName { get; set; } = string.Empty;
            public int? ChildProcessId { get; set; }
            public long? ChildStartTimeUtcTicks { get; set; }
            public string? ChildExecutablePath { get; set; }
        }
    }
}
