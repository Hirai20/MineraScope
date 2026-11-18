namespace MineraScope
{
    // 261112Codex: 分類MLPの学習専用Dropout条件を保存し、旧記録の欠落は未記録のまま扱う。
    internal sealed record ClassificationDropoutSettings(double Rate, int MaskSeed, string Position, string Mode);

    // 261119Codex: 分類MLPで率・位置・独立したマスク系列を固定する。
    internal static class ClassificationDropoutPolicy
    {
        internal const double Rate = 0.2;
        internal const int MaskSeed = 20261112;
        internal static readonly ClassificationDropoutSettings Settings = new(Rate, MaskSeed, "after-first-relu", "inverted-training-only");

        // 261112Codex: 初期化乱数を消費せず、サンプル・ユニットごとにinverted dropoutのマスクを更新する。
        internal static void FillMask(Random random, float[,] mask)
        {
            float keepScale = (float)(1 / (1 - Rate));
            for (int row = 0; row < mask.GetLength(0); row++)
                for (int unit = 0; unit < mask.GetLength(1); unit++)
                    mask[row, unit] = random.NextDouble() >= Rate ? keepScale : 0;
        }
    }
}
