using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MineraScope
{
    // 260621Claude: ブロック1行の種別。Field=項目名+値、Header=【…】見出し、Item=見出し配下の項目(端成分・確率)。
    internal enum SpectrumBlockRowKind { Field, Header, Item }

    // 260621Claude: ブロック1行の (種別, 項目名, 値)。CSV とテキストの両レンダラがこの並びを唯一の真実源として消費する。
    internal readonly record struct SpectrumBlockRow(SpectrumBlockRowKind Kind, string Label, string Value);

    // 260620Claude: 1スペクトルの予測結果をブロックへ整形する。画面結果欄・report(.txt)・CSV が同じ並び順・0.00隠し・%整形を共有する。
    internal static class SpectrumPredictionBlockFormatter
    {
        // 260621Claude: ブロック本体の行を順番に列挙する(成功 item 専用)。並び順は画像準拠: 分類結果→端成分比率→化学組成式→詳細確率(行数可変なので末尾)。
        //               確率は丸めて 0.00 になるものを隠す。ファイルパス・モデル名は呼び出し側レンダラが前置する。
        public static IEnumerable<SpectrumBlockRow> EnumerateBodyRows(SpectrumPredictionItem item)
        {
            var classification = item.Classification!;
            yield return new(SpectrumBlockRowKind.Field, "分類結果", classification.PredictedMineral);

            if (item.Endmembers.Count > 0)
            {
                yield return new(SpectrumBlockRowKind.Header, "【端成分比率】", string.Empty);
                // 260620Claude: 端成分は検証対象なので 0 も隠さず全成分を出す。
                foreach (var component in item.Endmembers)
                    yield return new(SpectrumBlockRowKind.Item, component.ComponentName, FormatPercent(component.Ratio));
            }

            if (!string.IsNullOrEmpty(item.ChemicalFormula))
                yield return new(SpectrumBlockRowKind.Field, "化学組成式", item.ChemicalFormula);

            yield return new(SpectrumBlockRowKind.Header, "【詳細確率】", string.Empty);
            foreach (var probability in classification.Probabilities)
            {
                // 260620Claude: テキストボックス表示と同じく、丸めて 0.00 になる確率は隠す。
                string percent = FormatPercent(probability.Confidence);
                if (percent == "0.00")
                    continue;
                yield return new(SpectrumBlockRowKind.Item, probability.MineralName, percent);
            }
        }

        // 260620Claude: 画面結果欄用。ファイルパス・モデル名は別コントロールで持つので本体だけを縦テキストにする。
        public static string FormatBody(SpectrumPredictionItem item)
        {
            if (!item.IsSuccess)
                return $"分類結果: (失敗)\r\n  {item.Error}";

            var sb = new StringBuilder();
            bool first = true;
            foreach (var row in EnumerateBodyRows(item))
            {
                // 260621Claude: 先頭以外のセクション(見出し・独立フィールド)の前に空行を入れて読みやすくする。Item の前には入れない。
                if (!first && row.Kind != SpectrumBlockRowKind.Item)
                    sb.AppendLine();

                sb.AppendLine(row.Kind switch
                {
                    SpectrumBlockRowKind.Header => row.Label,
                    SpectrumBlockRowKind.Item => $"  {row.Label}: {row.Value}",
                    _ => $"{row.Label}: {row.Value}",
                });
                first = false;
            }

            return sb.ToString().TrimEnd();
        }

        // 260620Claude: report(.txt) 用。standalone なので先頭にファイルパスを足す。
        public static string FormatBlock(SpectrumPredictionItem item)
            => $"ファイルパス: {item.FilePath}\r\n{FormatBody(item)}";

        // 260620Claude: % 2桁。確率・端成分比率は 0-1 を 100倍して F2 で出す。
        public static string FormatPercent(float ratio)
            => (ratio * 100).ToString("F2", CultureInfo.InvariantCulture);
    }
}
