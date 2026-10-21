using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MineraScope
{
    // 260901Codex: DTSA-IIのJava試行ごとにマーカーを保存し、孤児プロセスがEMSAを書いている間は再起動したアプリの復旧を拒否する。
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

        // 260901Codex: Process.Start前に未解決マーカーを書き、子PIDが存在する前のクラッシュの隙間をなくす。
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

        // 260901Codex: 子プロセスの起動と永続的な識別を一操作にし、登録失敗時は試行を続ける前に終了させる。
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
                // 260901Codex: 未登録の書き込みプロセスは安全でないためツリーごと終了させ、終了確認までは保留マーカーを残す。
                if (TryKillAndConfirmExit(process))
                    TryDeleteMarker();
                throw;
            }
        }

        // 260901Codex: poolの排他的な利用権を持つ間にマーカーを検査する。
        // 260917Claude: マーカー導入前の版向けの、java.exeの名前だけによる互換処理は削除し、全DTSA-II子プロセスをマーカーで追跡する。
        // 260909Codex: 共有読み取り側は稼働中の書き込みを確認できるが、古いマーカーファイルを削除できない。
        public static bool TryEnsureNoDtsaProcessCanWrite(out string failureReason, bool removeStaleMarkers = true)
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

            // 260909Codex: 上記の稼働中書き込みの検査をすべて維持しつつ、読み取り専用の入力準備ではマーカーを変更しない。
            if (!removeStaleMarkers)
            {
                failureReason = string.Empty;
                return true;
            }

            // 260901Codex: 追跡中のDTSA-II書き込みプロセスがなければ、未解決・破損・終了済み・PID再利用のマーカーは古く、安全に削除できる。
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

        // 260901Codex: 削除失敗時は次の排他利用時の検査用にマーカーを保守的に残し、試行結果を置き換えない。
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
