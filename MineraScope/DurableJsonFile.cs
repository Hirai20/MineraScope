using System.IO;
using System.Text;

namespace MineraScope
{
    // 260728Claude: LocalApplicationData 配下の設定 JSON を安全に置き換える共通処理。
    //   FormUserSettingsStore と MineralColorPalette が同じフォルダへ書きながら、耐久性の実装が
    //   別々に育って食い違っていた (前者は flush + Replace + .bak、後者は WriteAllText + Move のみ)。
    //   壊れたファイルの退避 (*.invalid.json) も一字一句同じ実装が 2 つあったため、ここへ寄せる。
    internal static class DurableJsonFile
    {
        // 260728Claude: File.Replace が自動生成する 1 世代前の正常値。読み込みの第 2 候補になる。
        public const string BackupSuffix = ".bak";

        // 260728Claude: エンコーディングは File.WriteAllText と同じ BOM 無し UTF-8 (既存ファイルと互換)。
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public static string GetBackupPath(string path) => path + BackupSuffix;

        // 260728Claude: 一時ファイルへ書き → 物理ディスクまで flush → 原子的に差し替える。
        //   OS のバッファに載っただけでは電源断で失われ、「原子的に置き換わった空ファイル」が残りうるので
        //   flush を挟む順序が要点。File.Replace は差し替え前の内容を .bak へ退避するため、
        //   直前の正常値が必ず 1 世代残る (宛先が無い初回だけ Replace が使えないので Move)。
        public static void Replace(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DefaultStoragePaths.SettingsFolder);
            // 260728Claude: 一時ファイル名にプロセス ID を入れる。単一インスタンス制約が無いため、2 つ目の MineraScope が
            //   同時に保存を走らせると、共有名では「A が書いた .tmp を B が上書き → A が B の内容を置換 → B の Move が
            //   FileNotFound で例外」という取り違えが起こり得る。プロセスごとに分ければ両者が独立して完結する。
            string temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Utf8NoBom.GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                    File.Replace(temporaryPath, path, GetBackupPath(path));
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        // 260728Claude: 読めなかったファイルを *.invalid.json として残し、ユーザーへ見せる補足文を返す。
        //   ロック中などで退避自体が失敗することもあるので、その旨も文面で伝える。
        public static string Quarantine(string path)
        {
            if (!File.Exists(path))
                return string.Empty;

            string quarantinedPath = Path.Combine(
                Path.GetDirectoryName(path) ?? DefaultStoragePaths.SettingsFolder,
                $"{Path.GetFileNameWithoutExtension(path)}.invalid.json");
            try
            {
                File.Copy(path, quarantinedPath, true);
                return $"\r\n\r\n読めなかったファイルは次へ退避しました。\r\n{quarantinedPath}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"\r\n\r\n退避にも失敗しました。\r\n{ex.Message}";
            }
        }

        // 260728Claude: 後片付けの失敗で保存結果を上書きしない。
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
