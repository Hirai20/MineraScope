using System.Globalization;
using System.Text;

namespace MineraScope
{
    // 260620Claude: 予測バッチを2形式で書き出す。分析用 tidy CSV(WriteCsv) と閲覧用ブロック report(WriteReport)。
    //               どちらも UTF-8 BOM・CRLF・InvariantCulture・数値 F2 で揃える。失敗 item は CSV から除外し、report 末尾に一覧する。
    internal static class SpectrumPredictionExporter
    {
        private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

        private const string Divider = "────────────────────────";

        // 260620Claude: ブロック2列(項目名,値)の CSV。Excel で縦のブロックとして読める形にし、スペクトル間は空行で区切る。
        public static void WriteCsv(string path, SpectrumPredictionBatch batch)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var item in batch.Items)
            {
                if (!item.IsSuccess)
                    continue;

                if (!first)
                    sb.Append("\r\n");
                first = false;

                AppendRow(sb, "ファイルパス", item.FilePath);
                AppendRow(sb, "使用モデル", batch.ModelName);
                // 260621Claude: 並び順・0.00隠し・%整形は BlockFormatter を唯一の真実源にし、CSV は各行を2列へ落とすだけ。
                foreach (var row in SpectrumPredictionBlockFormatter.EnumerateBodyRows(item))
                    AppendRow(sb, row.Label, row.Value);
            }

            // 260705Codex: Keep CSV export as one overwrite operation; history append mode is intentionally removed.
            File.WriteAllText(path, sb.ToString(), Utf8Bom);
        }

        // 260621Codex: Keep report block separation consistent in one place.
        private static void AppendDivider(StringBuilder sb)
        {
            sb.Append("\r\n");
            sb.Append(Divider);
            sb.Append("\r\n");
        }

        // 260620Claude: 1行を「項目名,値」の2列で追記する。
        private static void AppendRow(StringBuilder sb, string label, string value)
            => sb.Append($"{Csv(label)},{Csv(value)}\r\n");

        // 260620Claude: 閲覧用 report。先頭にモデル・日時・件数、以降は1スペクトル1ブロックを区切り線で積む。失敗は末尾に一覧。
        public static void WriteReport(string path, SpectrumPredictionBatch batch)
        {
            var successes = batch.Items.Where(item => item.IsSuccess).ToList();
            var failures = batch.Items.Where(item => !item.IsSuccess).ToList();

            var sb = new StringBuilder();
            sb.Append($"使用モデル: {batch.ModelName}\r\n");
            sb.Append($"出力日時: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\r\n");
            sb.Append($"件数: {successes.Count}/{batch.Items.Count}\r\n");

            foreach (var item in successes)
            {
                AppendDivider(sb);
                sb.Append(SpectrumPredictionBlockFormatter.FormatBlock(item));
                sb.Append("\r\n");
            }

            if (failures.Count > 0)
            {
                AppendDivider(sb);
                sb.Append("【読み込み/予測に失敗したファイル】\r\n");
                foreach (var item in failures)
                    sb.Append($"  {item.FilePath}: {item.Error}\r\n");
            }

            File.WriteAllText(path, sb.ToString(), Utf8Bom);
        }

        // 260621Claude: 既定エクスポート名 =「ドロップしたもの_モデル名」。フォルダ1つ→フォルダ名/ファイル1つ→ファイル名/複数→先頭名_etcN。ドロップ元不明はモデル名のみ。
        public static string BuildDefaultFileName(string modelName, IReadOnlyList<string> droppedPaths)
        {
            string model = SpectrumPoolRepository.SanitizeFileName(modelName);
            string baseName = BuildDroppedBaseName(droppedPaths);
            return baseName.Length == 0 ? model : $"{SpectrumPoolRepository.SanitizeFileName(baseName)}_{model}";
        }

        private static string BuildDroppedBaseName(IReadOnlyList<string> droppedPaths)
        {
            if (droppedPaths.Count == 0)
                return string.Empty;

            string firstName = DroppedItemName(droppedPaths[0]);
            return droppedPaths.Count == 1 ? firstName : $"{firstName}_etc{droppedPaths.Count - 1}";
        }

        // 260621Claude: ドロップ項目の表示名。フォルダはフォルダ名、ファイルは拡張子なしのファイル名。
        private static string DroppedItemName(string path)
            => Directory.Exists(path)
                ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileNameWithoutExtension(path);

        // 260620Claude: RFC4180。カンマ・引用符・改行を含む場合だけ引用し、内部の引用符は二重化する。
        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
                return value;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
