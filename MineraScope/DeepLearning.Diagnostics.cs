using System.Diagnostics;
using System.Globalization;
using Tensorflow.NumPy;
using static Tensorflow.Binding;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260924Codex: 任意の合成データ診断を、本番の学習ワークフローから分離する。
    public partial class DeepLearning
    {
        // 260609Claude: GUI なしで分類 custom loop 学習を回す開発用ヘッドレス smoke test。env MINERASCOPE_HEADLESS_TRAIN=1 で Program から呼ぶ。
        //              合成だが学習可能なデータ(クラス中心+小ノイズ)を実データ相当の形 [n,2048]/K クラスで作り、最適化が正しく回るか・速度・leak を実 pool 無しで検証する。
        //              合成なので精度の意味は無い(実データの精度 A/B は GUI/実 pool で別途)。結果は tf-train-debug.log に出る。
        internal static void RunHeadlessSmokeTest(Action<string> log)
        {
            int samples = ReadIntEnv("MINERASCOPE_SMOKE_SAMPLES", 6000);
            int classes = ReadIntEnv("MINERASCOPE_SMOKE_CLASSES", 29);
            int epochs = ReadIntEnv("MINERASCOPE_SMOKE_EPOCHS", 5);
            int batchSize = ReadIntEnv("MINERASCOPE_SMOKE_BATCH", 128);
            int valSamples = Math.Max(classes, samples / 5);

            TensorFlowTrainingDebugLog.Write("smoke-start", $"samples={samples} classes={classes} epochs={epochs} batchSize={batchSize}");
            log($"headless smoke test: samples={samples} classes={classes} epochs={epochs} batch={batchSize}");

            tf.set_random_seed(42);
            var rng = new Random(42);
            float[,] centers = new float[classes, SpectrumLength];
            for (int k = 0; k < classes; k++)
                for (int j = 0; j < SpectrumLength; j++)
                    centers[k, j] = (float)rng.NextDouble();

            var (xTrain, yTrain) = MakeSyntheticClassificationData(samples, classes, centers, rng);
            var (xTest, yTest) = MakeSyntheticClassificationData(valSamples, classes, centers, rng);

            var model = CreateClassificationModel(classes);
            model.compile(
                // 260918Codex: 合成Softmax検証のコンパイルメタデータを、グラフループの既定値にそろえる。
                // 261014Codex: 診断用モデルのcompile設定を、実際のgraph学習で使う既定学習率に揃える。
                optimizer: keras.optimizers.Adam(learning_rate: ModelTrainingSettings.DefaultClassificationLearningRate),
                loss: CreateSparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" });

            var sw = Stopwatch.StartNew();
            var result = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, 10, "classification:Smoke", log, null, default);
            sw.Stop();

            TensorFlowTrainingDebugLog.Write("smoke-end", $"totalMs={sw.ElapsedMilliseconds} trainedEpochs={result.CompletedEpochs} finalMetrics={TensorFlowTrainingDebugLog.Clean(result.LastEpochMetrics)}");
            log($"headless smoke done: totalMs={sw.ElapsedMilliseconds} epochs={result.CompletedEpochs} final={result.LastEpochMetrics}");
        }

        // 260609Claude: クラスごとに固有中心 + 小ノイズの学習可能な合成分類データを実データ相当の形で作る。
        private static (NDArray X, NDArray Y) MakeSyntheticClassificationData(int n, int classes, float[,] centers, Random rng)
        {
            float[,] x = new float[n, SpectrumLength];
            int[] y = new int[n];
            for (int i = 0; i < n; i++)
            {
                int label = rng.Next(classes);
                y[i] = label;
                for (int j = 0; j < SpectrumLength; j++)
                    x[i, j] = centers[label, j] + (float)(rng.NextDouble() * 0.1 - 0.05);
            }

            return (np.array(x), np.array(y));
        }

        private static int ReadIntEnv(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
                ? value
                : fallback;

    }
}
