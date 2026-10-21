namespace MineraScope
{
    // 260924Codex: 学習済みheadと推論で、変換した同じ3つのMahalanobis距離を使う。
    internal static class SoftmaxOcnHeadFeatures
    {
        public static float[] TransformAll(double[] distances, IReadOnlyList<double> thresholds, bool normalized)
        {
            if (distances.Length != thresholds.Count)
                throw new ArgumentException("距離とクラス別しきい値の数が一致しません。");
            var values = new float[distances.Length];
            for (int label = 0; label < distances.Length; label++)
                values[label] = (float)(normalized
                    ? Math.Log(Math.Max(distances[label], 1e-12) / thresholds[label])
                    : Math.Log(1 + distances[label]));
            return values;
        }

        // 261001Codex: 学習と推論で、モデルに保存した距離入力数を使う。
        public static float[] Nearest(float[] perLabel, bool[]? hidden, int inputCount = 3)
        {
            if (inputCount is < 1 or > 3 || perLabel.Length < inputCount || hidden is not null && hidden.Length != perLabel.Length)
                throw new ArgumentException("OCN入力を作るクラス数が不正です。");
            var values = new List<float>(perLabel.Length);
            for (int label = 0; label < perLabel.Length; label++)
                if (hidden is null || !hidden[label])
                    values.Add(perLabel[label]);
            if (values.Count < inputCount)
                throw new ArgumentException("OCN入力に必要な可視クラスが足りません。");
            values.Sort();
            return values.Take(inputCount).ToArray();
        }
    }
}
