using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MineraScope
{
    // 260917Codex: A named OS mutex protects one path/operation across app processes without adding files to model bundles.
    // Acquire and dispose on the same synchronous workflow thread. Process termination releases ownership automatically.
    internal sealed class PathOperationLease : IDisposable
    {
        private Mutex? _mutex;

        private PathOperationLease(Mutex mutex) => _mutex = mutex;

        public static PathOperationLease Acquire(string path, string operation, int millisecondsTimeout = 0)
        {
            string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToUpperInvariant();
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
            var mutex = new Mutex(false, $@"Local\MineraScope.{operation}.{key}");
            try
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(millisecondsTimeout);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                    throw new IOException($"同じ保存先が別の処理で使用中です。別の保存先を指定するか、処理終了後に再実行してください: {path}");
                return new PathOperationLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_mutex is null)
                return;
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
