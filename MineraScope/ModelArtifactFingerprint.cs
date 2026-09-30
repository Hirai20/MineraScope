using System.Security.Cryptography;
using System.Text;

namespace MineraScope
{
    // 260930Codex: Check saved model contents at operation boundaries, including equal-size and equal-time replacements.
    internal static class ModelArtifactFingerprint
    {
        internal static string Compute(string folder)
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(folder, path), StringComparer.Ordinal))
            {
                digest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, path)));
                digest.AppendData(new byte[] { 0 });
                using var stream = File.OpenRead(path);
                digest.AppendData(SHA256.HashData(stream));
            }
            return Convert.ToHexString(digest.GetHashAndReset());
        }
    }
}
