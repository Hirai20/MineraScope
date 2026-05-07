using System.Collections.Generic;

namespace MineraScope
{
    // 260507Codex: manifest で選ばれた Completed spectrum と回帰ラベルを学習側へ渡します。
    internal sealed record SpectrumTrainingSample(
        string FilePath,
        IReadOnlyDictionary<string, double> EndmemberFractions);

    // 260507Codex: 分類ラベル単位の学習入力をまとめ、DeepLearning がフォルダ走査に依存しない形にします。
    internal sealed record SpectrumTrainingPool(
        string MineralName,
        IReadOnlyList<string> EndmemberNames,
        IReadOnlyList<SpectrumTrainingSample> Samples);
}
