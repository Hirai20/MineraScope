using System.Globalization;
using System.Text;

namespace MineraScope
{
    // 260620Claude: 1スペクトルの予測結果をテキストブロックへ整形する。画面の結果欄と report(.txt) で共有する。
    //               画面側はファイルパス・モデル名を別コントロールで持つため本体(FormatBody)だけを使い、report 側はファイルパスを足した FormatBlock を使う。
    internal static class SpectrumPredictionBlockFormatter
    {
        // 260620Claude: 画面結果欄用。分類結果・詳細確率・端成分比率・化学組成式だけを縦に並べる。
        public static string FormatBody(SpectrumPredictionItem item)
        {
            var sb = new StringBuilder();

            if (!item.IsSuccess)
            {
                sb.AppendLine("分類結果: (失敗)");
                sb.AppendLine($"  {item.Error}");
                return sb.ToString().TrimEnd();
            }

            var classification = item.Classification!;
            sb.AppendLine($"分類結果: {classification.PredictedMineral}");

            // 260621Claude: 並び順は画像に合わせ、端成分比率→化学組成式→詳細確率の順にする（確率は行数が可変なので一番下）。
            if (item.Endmembers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("【端成分比率】");
                // 260620Claude: 端成分は検証対象なので 0 も隠さず全成分を縦に並べる。
                foreach (var component in item.Endmembers)
                    sb.AppendLine($"  {component.ComponentName}: {FormatPercent(component.Ratio)}");
            }

            if (!string.IsNullOrEmpty(item.ChemicalFormula))
            {
                sb.AppendLine();
                sb.AppendLine($"化学組成式: {item.ChemicalFormula}");
            }

            sb.AppendLine();
            sb.AppendLine("【詳細確率】");
            foreach (var probability in classification.Probabilities)
            {
                // 260620Claude: テキストボックス表示と同じく、丸めて 0.00 になる確率は隠す。
                string? percent = FormatVisiblePercent(probability.Confidence);
                if (percent is null)
                    continue;
                sb.AppendLine($"  {probability.MineralName}: {percent}");
            }

            return sb.ToString().TrimEnd();
        }

        // 260620Claude: report(.txt) 用。standalone なので先頭にファイルパスを足す。
        public static string FormatBlock(SpectrumPredictionItem item)
            => $"ファイルパス: {item.FilePath}\r\n{FormatBody(item)}";

        private static string FormatPercent(float ratio)
            => (ratio * 100).ToString("F2", CultureInfo.InvariantCulture);

        // 260620Claude: 丸めて 0.00 になるものは null を返し、呼び出し側で除外させる。
        private static string? FormatVisiblePercent(float ratio)
        {
            string percent = FormatPercent(ratio);
            return percent == "0.00" ? null : percent;
        }
    }
}
