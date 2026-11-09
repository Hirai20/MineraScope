namespace MineraScope.Tests
{
    // 261106Codex: 再利用バッファーをまたぐ行の対応と、検証キャッシュが学習中の出力を固定しないことを確認する。
    internal static class SoftmaxOcnTrainingPerformanceTests
    {
        internal static void Run()
        {
            float[][] rows = SoftmaxOcnHeadFeatures.TransformRows(ReusedRows(531), 531,
                row => [row[0] + row[1], row[0] * row[1]], CancellationToken.None);
            TestAssert.SequenceEqual(Enumerable.Range(0, 531).Select(index => (float)(index + 2 * index)),
                rows.Select(row => row[0]), "Parallel rows retain source order across batch boundaries.");
            TestAssert.SequenceEqual(Enumerable.Range(0, 531).Select(index => (float)(index * 2 * index)),
                rows.Select(row => row[1]), "A reused input buffer is copied before enumeration advances.");
            TestAssert.Throws<InvalidDataException>(() => SoftmaxOcnHeadFeatures.TransformRows(ReusedRows(257), 256,
                row => row, CancellationToken.None), "Extra rows cannot overwrite the feature matrix.");
            TestAssert.Throws<InvalidDataException>(() => SoftmaxOcnHeadFeatures.TransformRows(ReusedRows(256), 257,
                row => row, CancellationToken.None), "Missing rows cannot leave null features.");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            TestAssert.Throws<OperationCanceledException>(() => SoftmaxOcnHeadFeatures.TransformRows(ReusedRows(1), 1,
                row => row, cancellation.Token), "Feature preparation honors cancellation.");

            foreach (int count in new[] { 2, 3, 4, 5, 6, 7, 8, 39 })
                CheckValidation(count, cancellation.Token);
        }

        // 261106Codex: 実際の固定データ読み取りと同じく、同じ配列を更新して次の行を返す。
        private static IEnumerable<float[]> ReusedRows(int count)
        {
            var row = new float[2];
            for (int index = 0; index < count; index++)
            {
                row[0] = index;
                row[1] = 2 * index;
                yield return row;
            }
        }

        // 261106Codex: 少数クラスと39クラスで、元の可視距離・集計順を独立に作り、更新前後のheadと照合する。
        private static void CheckValidation(int count, CancellationToken cancelled)
        {
            int hiddenCount = SoftmaxOcnHeadTraining.GetHiddenClassCount(count);
            int width = Math.Min(3, count - hiddenCount);
            int[][] groups = Enumerable.Range(0, count).Select(label => Enumerable.Range(label * 3, 3).ToArray()).ToArray();
            float[][] features = Enumerable.Range(0, count * 3)
                .Select(row => Enumerable.Range(0, count).Select(label => (float)((row * 13 + label * 7) % 23) / 4).ToArray()).ToArray();
            int[][] masks = [Enumerable.Range(0, hiddenCount).ToArray(), Enumerable.Range(count - hiddenCount, hiddenCount).ToArray()];
            var reference = new List<(float[] Values, bool Unknown)>();
            foreach (int[] mask in masks)
                for (int label = 0; label < count; label++)
                    foreach (int index in groups[label])
                        reference.Add((features[index].Where((_, candidate) => !mask.Contains(candidate)).Order().Take(width).ToArray(),
                            mask.Contains(label)));
            var cached = SoftmaxOcnHeadTraining.PrepareValidation(features, groups, masks, width, CancellationToken.None);
            TestAssert.Equal(reference.Count, cached.Length, "Every validation row and mask is retained.");
            // 261106Codex: 件数を照合した後に期待値と実測値を組にし、同じ添字の繰り返しを避ける。
            foreach (var (expected, actual) in reference.Zip(cached))
            {
                TestAssert.SequenceEqual(expected.Values.Select(BitConverter.SingleToInt32Bits),
                    actual.Values.Select(BitConverter.SingleToInt32Bits), "Cached nearest inputs are bit-identical.");
                TestAssert.Equal(expected.Unknown, actual.IsUnknown, "Validation target and row order are unchanged.");
            }
            var head = new SoftmaxOcnHead(new Random(11), width);
            // 261109Codex: 更新するのはheadだけなので、各stepに渡す固定の入力と正解を一度だけ用意する。
            var trainingInputs = reference.Select(input => input.Values).ToList();
            var trainingTargets = reference.Select(input => input.Unknown
                ? SoftmaxOcnHead.UnknownOutput : SoftmaxOcnHead.KnownOutput).ToList();
            for (int step = 0; step < 3; step++)
            {
                long knownCorrect = 0, knownTotal = 0, unknownCorrect = 0, unknownTotal = 0;
                foreach (var input in reference)
                {
                    bool prediction = head.Predict(input.Values) > .5;
                    if (input.Unknown) { unknownTotal++; if (prediction) unknownCorrect++; }
                    else { knownTotal++; if (!prediction) knownCorrect++; }
                }
                TestAssert.Equal(((double)knownCorrect / knownTotal, (double)unknownCorrect / unknownTotal),
                    SoftmaxOcnHeadTraining.Validate(head, cached, CancellationToken.None), "Cached validation applies the current head exactly.");
                head.TrainStep(trainingInputs, trainingTargets);
            }
            TestAssert.Throws<OperationCanceledException>(() => SoftmaxOcnHeadTraining.PrepareValidation(features, groups, masks, width, cancelled),
                "Validation preparation honors cancellation.");
            TestAssert.Throws<OperationCanceledException>(() => SoftmaxOcnHeadTraining.Validate(head, cached, cancelled),
                "Validation inference honors cancellation.");
        }
    }
}
