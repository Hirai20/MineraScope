namespace MineraScope
{
    // 260924Codex: 学習済みheadと推論で、変換した同じ3つのMahalanobis距離を使う。
    internal static class SoftmaxOcnHeadFeatures
    {
        // 261106Codex: 再利用される読み取りバッファーを小分けに所有し、TFを含まない行ごとの計算を順序を保って並列化する。
        internal static float[][] TransformRows(IEnumerable<float[]> rows, int expectedRows,
            Func<float[], float[]> transform, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(transform);
            ArgumentOutOfRangeException.ThrowIfNegative(expectedRows);
            token.ThrowIfCancellationRequested();
            var features = new float[expectedRows][];
            var options = new ParallelOptions { CancellationToken = token };
            int offset = 0;
            // 261106Codex: 全スペクトルを保持せず、2048チャンネルなら約2MiBずつコピーしてから次の行を読む。
            foreach (float[][] batch in rows.Select(row => (float[])row.Clone()).Chunk(256))
            {
                token.ThrowIfCancellationRequested();
                if (batch.Length > expectedRows - offset)
                    throw new InvalidDataException("固定データの行数が記録より多くなっています。");
                int start = offset;
                Parallel.For(0, batch.Length, options, index => features[start + index] = transform(batch[index]));
                offset += batch.Length;
            }
            if (offset != expectedRows)
                throw new InvalidDataException("固定データの行数が記録より少なくなっています。");
            return features;
        }

        // 261109Codex: 採用しない閾値規格化の分岐と設定を削除し、Rawのlog(1+d)に限定する。
        public static float[] TransformAll(double[] distances, IReadOnlyList<double> thresholds)
        {
            if (distances.Length != thresholds.Count)
                throw new ArgumentException("距離とクラス別しきい値の数が一致しません。");
            var values = new float[distances.Length];
            for (int label = 0; label < distances.Length; label++)
                values[label] = (float)Math.Log(1 + distances[label]);
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
