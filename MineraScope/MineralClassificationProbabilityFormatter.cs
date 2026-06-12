using System.Globalization;

namespace MineraScope
{
    // 260611Claude: 分類確率の表示行整形を一箇所へ集約する。F2 で丸めて 0.00% になる候補は隠し、
    //              FormMain のドロップ判定 (DeepLearning.RunPrediction) と AnalyzerForm の表示で同じルールを共有する。
    internal static class MineralClassificationProbabilityFormatter
    {
        // 260611Claude: 表示する候補だけを "  鉱物名: 12.34%" 形式の行で返す。F2 で "0.00" になる 0.01% 未満は除外。
        public static IEnumerable<string> VisibleLines(IReadOnlyList<MineralClassificationProbability> probabilities)
        {
            foreach (var probability in probabilities)
            {
                string percentText = (probability.Confidence * 100).ToString("F2", CultureInfo.InvariantCulture);
                if (percentText == "0.00")
                    continue;
                yield return $"  {probability.MineralName}: {percentText}%";
            }
        }
    }
}
