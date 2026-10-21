using System;
using System.IO;

namespace MineraScope
{
    // 260901Codex: OSが解放する排他ファイルハンドルを復旧・予約・シミュレーション中に保持し、2つのアプリが同じpoolを並行変更しないようにする。
    internal sealed class SpectrumPoolMutationLease : IDisposable
    {
        private const string LockFileName = "spectrum-pool-mutation.lock";
        private FileStream? _stream;

        private SpectrumPoolMutationLease(FileStream stream)
        {
            _stream = stream;
        }

        // 260901Codex: 元の保護処理は、ロック導入前の実行ファイルにも対応するため全アプリプロセスを確認していた。
        // 260917Codex: 更新済みインスタンスは書込所有権で調停し、待機中・学習中のインスタンスは開いたままにできる。
        public static bool TryAcquire(
            out SpectrumPoolMutationLease? lease,
            out string failureReason)
        {
            lease = null;
            try
            {
                Directory.CreateDirectory(DefaultStoragePaths.SettingsFolder);
                string lockPath = Path.Combine(DefaultStoragePaths.SettingsFolder, LockFileName);
                // 260917Codex: 別の待機・学習プロセスを許容し、2つ目の書き込みだけを競合とする。
                var stream = OpenWriter(lockPath);

                // 260901Codex: マーカー検査前に利用権を取得するため、この検査中に別の更新済み実行が子を起動することはできない。
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

        // 260909Codex: 入力固定に、スペクトル書き込み側と同じロックパスを使う。
        // 260917Codex: Completedだけを使う読み取り側も、更新済み書き込み側と共存できる。
        internal static bool TryAcquireReadOnly(out SpectrumPoolMutationLease? lease, out string failureReason)
        {
            lease = null;
            FileStream? stream = null;
            try
            {
                Directory.CreateDirectory(DefaultStoragePaths.SettingsFolder);
                // 260917Codex: 読み取り側は公開済みCompleted項目だけを使うため、実行中のDTSA子プロセスで妨げられない。
                stream = OpenReader(Path.Combine(DefaultStoragePaths.SettingsFolder, LockFileName));
                lease = new SpectrumPoolMutationLease(stream);
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 260917Codex: 新しい読み取り専用利用権を妨げるのは、従来の排他ハンドルかアクセスエラーだけである。
                failureReason = "旧版のMineraScopeがデータを使用中か、読み取り用の共有ロックを開けません。" +
                    $"旧版の処理終了後に再実行するか、アクセス権を確認してください。\r\n\r\n{exception.Message}";
                return false;
            }
            finally
            {
                if (lease is null)
                    stream?.Dispose();
            }
        }

        // 260917Codex: 既存のロックパスを互換性のため保持し、旧排他書き込みも新ハンドルをすべて阻止できるようにする。
        internal static FileStream OpenWriter(string lockPath) =>
            new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        // 260917Codex: 読み取りアクセスで並行公開を許し、ReadWrite共有で上記の書き込み1つだけを許す。
        internal static FileStream OpenReader(string lockPath) =>
            new(lockPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
    }
}
