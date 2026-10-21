using System.Text.Json;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260928Codex: 固定した分類器とデータセットからUnknown判定を学習し、クローズドセットの予測を変えない。
    internal static class SoftmaxOcnHeadTraining
    {
        private const int EpisodesPerEpoch = 50;
        private const int DefaultMaxEpochs = 300;
        private const int DefaultPatience = 20;
        // 261001Codex: 独立した実験構成に依存せず、採用したheadの学習予定を保持する。
        private const int KnownQueryPerClass = 6;
        private const int ValidationScheduleSeed = 20260916;
        private const int ValidationRepeats = 3;

        internal static void TrainAndSave(string modelRoot, string datasetFolder, int seed, string source,
            CancellationToken cancellationToken, Action<string> log,
            int maxEpochs = DefaultMaxEpochs, int patience = DefaultPatience,
            DenseClassificationFeatureExtractor? classifierFeatures = null)
        {
            // 261001Codex: 通常の作成では、分類器と共通の停止設定を渡す。
            if (maxEpochs < 1 || patience < 0)
                throw new ArgumentOutOfRangeException(nameof(maxEpochs));
            ArgumentException.ThrowIfNullOrWhiteSpace(modelRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(datasetFolder);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ArgumentNullException.ThrowIfNull(log);
            cancellationToken.ThrowIfCancellationRequested();

            ClassificationTrainingDatasetInfo info = ClassificationTrainingDataset.ReadInfo(datasetFolder, cancellationToken);
            string classification = ModelArtifactPaths.GetClassificationFolder(modelRoot);
            var encoder = JsonSerializer.Deserialize<Dictionary<string, int>>(
                File.ReadAllText(Path.Combine(classification, ModelArtifactPaths.LabelEncoderFileName)))
                ?? throw new InvalidDataException("分類モデルのlabelEncoder.jsonを読み込めません。");
            string[] classNames = new string[encoder.Count];
            foreach (var pair in encoder)
            {
                if (pair.Value < 0 || pair.Value >= classNames.Length || classNames[pair.Value] is not null)
                    throw new InvalidDataException("分類モデルのラベル番号が不正です。");
                classNames[pair.Value] = pair.Key;
            }
            if (classNames.Length < 2 || classNames.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("分類＋OCNの学習には2種類以上の鉱物が必要です。");

            // 260928Codex: 既存のTensorFlow実行基盤上で、特徴抽出器がモデル重みをマネージド配列へコピーする。
            // 261001Codex: 通常作成では採用重みを直接渡し、従来の研究用呼び出しでは既存のローダーを維持する。
            DenseClassificationFeatureExtractor extractor = classifierFeatures ?? TensorFlowExecutor.Run(
                () => DenseClassificationFeatureExtractor.FromModel(keras.models.load_model(classification)));
            if (!MineralUnknownDetector.TryLoad(classification, classNames.Length, extractor.EmbeddingDim, out var detector, out string reason)
                || detector is null)
                throw new InvalidDataException($"分類モデルの距離検知器を読み込めません: {reason}");
            int[] labelMap = info.ClassNames.Select(name => encoder.TryGetValue(name, out int id)
                ? id : throw new InvalidDataException($"固定データの鉱物 {name} が分類モデルにありません。")).ToArray();
            if (labelMap.Distinct().Count() != classNames.Length)
                throw new InvalidDataException("固定データと分類モデルの鉱物集合が一致しません。");

            var features = new float[info.SampleCount][];
            var labels = new int[info.SampleCount];
            var distances = new double[classNames.Length];
            int rowIndex = 0;
            foreach (float[] spectrum in ClassificationTrainingDataset.ReadRows(datasetFolder, info, cancellationToken))
            {
                detector.Distances(extractor.Transform(spectrum), distances);
                features[rowIndex] = SoftmaxOcnHeadFeatures.TransformAll(distances, detector.Thresholds, normalized: false);
                labels[rowIndex] = labelMap[info.Samples[rowIndex].Label];
                rowIndex++;
            }
            int[][] training = Group(info.TrainingIndices, labels, classNames.Length);
            int[][] validation = Group(info.ValidationIndices, labels, classNames.Length);
            if (training.Any(group => group.Length < KnownQueryPerClass)
                || validation.Any(group => group.Length == 0))
                throw new InvalidDataException("分類＋OCNのepisode学習または検証に必要な鉱物別データが不足しています。");

            var random = new Random(seed);
            // 261001Codex: 既存の多数クラス結果では隠すクラス数を3に保ち、少数クラスではほぼ半分に分ける。
            int hiddenCount = GetHiddenClassCount(classNames.Length);
            int inputCount = Math.Min(3, classNames.Length - hiddenCount);
            var head = new SoftmaxOcnHead(random, inputCount);
            int[][] validationGroups = FixedHiddenGroups(classNames.Length, hiddenCount);
            float[] best = head.Snapshot();
            double bestScore = double.NegativeInfinity;
            int bestEpoch = 0;
            for (int epoch = 1; epoch <= maxEpochs; epoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double loss = 0;
                for (int episode = 0; episode < EpisodesPerEpoch; episode++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (inputs, targets) = SampleEpisode(random, features, training, classNames.Length, hiddenCount, inputCount);
                    loss += head.TrainStep(inputs, targets);
                }
                var (known, unknown) = Validate(head, features, validation, validationGroups, cancellationToken);
                double score = (known + unknown) / 2;
                if (score > bestScore)
                {
                    best = head.Snapshot();
                    bestScore = score;
                    bestEpoch = epoch;
                }
                if (epoch % 10 == 0 || epoch == bestEpoch)
                    log($"分類＋OCN head: epoch {epoch}, loss {loss / EpisodesPerEpoch:F4}, known {known:P1}, pseudo-unknown {unknown:P1}, best {bestScore:F4}");
                // 261001Codex: Patienceが0なら、分類と同じく最初の改善しないエポックで停止する。
                if (epoch > bestEpoch && epoch - bestEpoch >= patience)
                    break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            SoftmaxOcnHeadArtifact.Save(modelRoot, classNames, best, seed, bestEpoch, bestScore, source, inputCount);
            SoftmaxOcnHeadArtifact.Load(modelRoot);
            log($"分類＋OCN head保存完了: 採用epoch {bestEpoch}, Score {bestScore:F4}");
        }

        // 260928Codex: 疑似Unknownのエピソード全体で、各クラスの固定した学習・検証所属を分離する。
        private static int[][] Group(int[] indices, int[] labels, int count)
        {
            var groups = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
            foreach (int index in indices)
                groups[labels[index]].Add(index);
            return groups.Select(group => group.ToArray()).ToArray();
        }

        // 261001Codex: 通常機能のクラス数方針を、独立した実験用OCN手順から独立させる。
        // 261001Codex: 入れ子の条件式を使わず、クラス数の3条件を直接示す。
        internal static int GetHiddenClassCount(int count) => count switch
        {
            < 2 => throw new ArgumentOutOfRangeException(nameof(count)),
            < 7 => count / 2,
            _ => 3
        };

        // 261001Codex: 学習と検証で同じマスク構築処理を使い、疑似Unknownクラスを示す。
        private static bool[] CreateHiddenMask(int count, int[] hiddenLabels)
        {
            var hidden = new bool[count];
            foreach (int label in hiddenLabels)
                hidden[label] = true;
            return hidden;
        }

        private static int[][] FixedHiddenGroups(int count, int hiddenCount)
        {
            var random = new Random(ValidationScheduleSeed);
            var groups = new List<int[]>();
            for (int repeat = 0; repeat < ValidationRepeats; repeat++)
            {
                int[] order = Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToArray();
                for (int start = 0; start + hiddenCount <= count; start += hiddenCount)
                    groups.Add(order[start..(start + hiddenCount)]);
            }
            return groups.ToArray();
        }

        private static (List<float[]> Inputs, List<int> Targets) SampleEpisode(Random random, float[][] features,
            int[][] groups, int count, int hiddenCount, int inputCount)
        {
            int[] hiddenLabels = Enumerable.Range(0, count).OrderBy(_ => random.Next()).Take(hiddenCount).ToArray();
            var hidden = CreateHiddenMask(count, hiddenLabels);
            int pseudoPerHidden = (count - hiddenCount) * KnownQueryPerClass / hiddenCount;
            var inputs = new List<float[]>();
            var targets = new List<int>();
            for (int label = 0; label < count; label++)
            {
                int take = hidden[label] ? pseudoPerHidden : KnownQueryPerClass;
                foreach (int index in groups[label].OrderBy(_ => random.Next()).Take(take))
                {
                    inputs.Add(SoftmaxOcnHeadFeatures.Nearest(features[index], hidden, inputCount));
                    // 261001Claude: 伏せたクラスが未学習側です。出力番号は SoftmaxOcnHead の並びを名前で参照します。
                    targets.Add(hidden[label] ? SoftmaxOcnHead.UnknownOutput : SoftmaxOcnHead.KnownOutput);
                }
            }
            return (inputs, targets);
        }

        private static (double Known, double Unknown) Validate(SoftmaxOcnHead head, float[][] features,
            int[][] groups, int[][] hiddenGroups, CancellationToken token)
        {
            long knownCorrect = 0, knownTotal = 0, unknownCorrect = 0, unknownTotal = 0;
            foreach (int[] hiddenLabels in hiddenGroups)
            {
                token.ThrowIfCancellationRequested();
                var hidden = CreateHiddenMask(groups.Length, hiddenLabels);
                for (int label = 0; label < groups.Length; label++)
                    foreach (int index in groups[label])
                    {
                        bool predictedUnknown = head.Predict(SoftmaxOcnHeadFeatures.Nearest(features[index], hidden, head.InputCount)) > .5;
                        if (hidden[label])
                        {
                            unknownTotal++;
                            if (predictedUnknown) unknownCorrect++;
                        }
                        else
                        {
                            knownTotal++;
                            if (!predictedUnknown) knownCorrect++;
                        }
                    }
            }
            return ((double)knownCorrect / knownTotal, (double)unknownCorrect / unknownTotal);
        }
    }
}
