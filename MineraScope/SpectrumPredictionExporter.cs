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

                var classification = item.Classification!;
                AppendRow(sb, "ファイルパス", item.FilePath);
                AppendRow(sb, "使用モデル", batch.ModelName);
                AppendRow(sb, "分類結果", classification.PredictedMineral);

                // 260621Claude: 並び順は画像に合わせ、端成分比率→化学組成式→詳細確率の順にする（確率は行数が可変なので一番下）。
                if (item.Endmembers.Count > 0)
                {
                    AppendRow(sb, "【端成分比率】", string.Empty);
                    foreach (var component in item.Endmembers)
                        AppendRow(sb, component.ComponentName, Percent(component.Ratio));
                }

                if (!string.IsNullOrEmpty(item.ChemicalFormula))
                    AppendRow(sb, "化学組成式", item.ChemicalFormula);

                AppendRow(sb, "【詳細確率】", string.Empty);
                foreach (var probability in classification.Probabilities)
                {
                    // 260620Claude: テキストボックスと同じく 0.00 になる確率は出さない。
                    string percent = Percent(probability.Confidence);
                    if (percent == "0.00")
                        continue;
                    AppendRow(sb, probability.MineralName, percent);
                }
            }

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

        // 260620Claude: % 2桁。確率・端成分比率は 0-1 を 100倍して F2 で出す。
        private static string Percent(float ratio)
            => (ratio * 100).ToString("F2", CultureInfo.InvariantCulture);

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
