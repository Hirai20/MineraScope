using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Tensorflow;
using Tensorflow.Keras;
using Tensorflow.Keras.Callbacks;
using Tensorflow.Keras.Engine;
using Tensorflow.Keras.Losses;
using Tensorflow.NumPy;
using Tensorflow.Util;
using static Tensorflow.Binding;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260606Claude: 学習の進捗を UI へ運ぶ DTO。OverallFraction は分類1個+回帰N個を「モデル均等×エポック比」で 0..1 に正規化した全体進捗。経過時間は UI 側の操作ストップウォッチで表示する。
    internal sealed record TrainingProgress(
        string ModelName,
        int ModelIndex,
        int TotalModels,
        int Epoch,
        int RequestedEpochs,
        double OverallFraction);

    public class DeepLearning
    {
        private readonly Action<string> _logAction;
        public Dictionary<string, int>? ComponentIndex { get; private set; }

        public DeepLearning(Action<string> logAction)
        {
            _logAction = logAction;
            ComponentIndex = new Dictionary<string, int>();
        }

        private void Log(string message)
        {
            _logAction?.Invoke(message);
        }

        // 260430Codex: スペクトル長はローダー側の共通定数を参照し、学習モデル定義だけで使います。
        private const int SpectrumLength = SpectrumDataLoader.SpectrumLength;

        // 260514Codex: 分類モデルの層構成を 1 箇所に集め、入力長とクラス数だけを呼び出し側から渡します。
        private static Model CreateClassificationModel(int numClasses) =>
            keras.Sequential(new List<ILayer>
            {
                keras.layers.Dense(128, activation: "relu", input_shape: new Shape(SpectrumLength)),
                keras.layers.Dense(64, activation: "relu"),
                keras.layers.Dense(numClasses, activation: "softmax")
            });

        // 260514Codex: 回帰モデルの層構成を 1 箇所に集め、端成分数だけを呼び出し側から渡します。
        private static Model CreateRegressionModel(int numComponents) =>
            keras.Sequential(new List<ILayer>
            {
                keras.layers.Dense(64, activation: "relu", input_shape: new Shape(SpectrumLength)),
                keras.layers.Dense(64, activation: "relu"),
                keras.layers.Dense(numComponents)
            });

        // 260416Codex: 評価辞書から優先キー順で値を取る helper を追加し、回帰・分類で同じ流れを再利用します。
        private static double GetMetricValue<TMetric>(IReadOnlyDictionary<string, TMetric> metrics, int fallbackIndex, params string[] keys)
            where TMetric : struct, IConvertible
        {
            foreach (var key in keys)
            {
                if (metrics.TryGetValue(key, out var value))
                    return value.ToDouble(CultureInfo.InvariantCulture);
            }

            if (metrics.Count > fallbackIndex)
                return metrics.ElementAt(fallbackIndex).Value.ToDouble(CultureInfo.InvariantCulture);

            return 0;
        }

        // 260605Codex: Keep debug metric formatting culture-stable for log parsing.
        private static string FormatMetric(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

        // 260606Codex: Cache counters are cumulative for the training run, so log them with each stage for before/after comparison.
        private static string FormatSpectrumCacheStats(SpectrumDataLoader.NormalizedSpectrumCache? cache) =>
            cache is null
                ? string.Empty
                : $" cacheableSpectra={cache.CacheablePathCount} cachedSpectra={cache.CachedCount} cacheHits={cache.HitCount} cacheMisses={cache.MissCount} cacheStores={cache.StoreCount}";

        // 260605Codex: Epoch logs can include validation metrics when validation_data is passed to fit.
        private static string FormatEpochLogs(IEnumerable<KeyValuePair<string, float>> logs) =>
            string.Join(",", logs.Select(kv => $"{kv.Key}={kv.Value.ToString("G9", CultureInfo.InvariantCulture)}"));

        // 260605Codex: Patience is intended to watch validation/test loss rather than training loss.
        private const string EarlyStoppingMonitor = "val_loss";

        // 260609Claude: 等価性テスト前提の再現性確認用。env MINERASCOPE_DETERMINISTIC=1 のとき fit の毎epoch shuffle を切る(通常実行は従来どおり true)。
        //              スレッド数/op決定論は launch 時の env (TF_NUM_INTRAOP_THREADS / TF_NUM_INTEROP_THREADS / OMP_NUM_THREADS / TF_DETERMINISTIC_OPS) で設定する。
        private static readonly bool Deterministic =
            Environment.GetEnvironmentVariable("MINERASCOPE_DETERMINISTIC") == "1";

        // 260609Claude: env MINERASCOPE_CUSTOMLOOP=1 で分類学習を fit から自前 GradientTape ループへ切替(fit の毎batch強制 GC.Collect 撤去が目的。回帰は当面 fit)。
        //              ★Dense-only 前提: Dropout/BatchNorm/regularizer/non-trainable が無いので Apply(training:true/false) は同一。層を足したらこの前提を見直す。
        private static readonly bool CustomLoop =
            Environment.GetEnvironmentVariable("MINERASCOPE_CUSTOMLOOP") == "1";

        // 260609Claude: native handle を bound するため毎batchではなく N batch ごとにだけ GC する(fit の毎batch GC の代替)。env で sweep 可能、既定 32。
        private static int CustomLoopGcInterval =>
            int.TryParse(Environment.GetEnvironmentVariable("MINERASCOPE_CUSTOMLOOP_GC_N"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 32;

        // 260605Codex: Return the observed fit result because TensorFlow.Keras History is not reliable enough here.
        private sealed record TrainingFitResult(int RequestedEpochs, int CompletedEpochs, int LastEpoch, string LastEpochMetrics);

        // 260605Codex: validation_data is supplied explicitly, so patience watches val_loss.
        private static EarlyStopping CreateEarlyStopping(Model model, int epochs, int patience)
        {
            var cbParams = new CallbackParams
            {
                Model = model,
                Epochs = epochs,
                Verbose = 1
            };

            return new EarlyStopping(
                parameters: cbParams,
                monitor: EarlyStoppingMonitor,
                patience: patience,
                verbose: 1,
                mode: "auto",
                // 260606Codex: TensorFlow.Keras 0.15.0 uses 0f as the unset baseline sentinel; NaN prevents wait reset after improvement.
                baseline: 0f,
                restore_best_weights: true,
                start_from_epoch: 0
            );
        }

        // 260606Claude: 分類1個+回帰N個を1本のバーで表すため、モデル境界とエポックを「モデル均等×エポック比」で 0..1 の全体進捗へ畳み込みます。
        //              EarlyStopping で早期終了しても CompleteModel でその区間を 100% へスナップし、単調増加を保ちます。
        private sealed class TrainingProgressReporter
        {
            private readonly IProgress<TrainingProgress>? _progress;
            private readonly int _totalModels;
            private readonly int _requestedEpochs;
            private int _completedModels;
            private string _currentModelName = "";

            public TrainingProgressReporter(
                IProgress<TrainingProgress>? progress,
                int totalModels,
                int requestedEpochs)
            {
                _progress = progress;
                _totalModels = Math.Max(1, totalModels);
                _requestedEpochs = Math.Max(1, requestedEpochs);
            }

            public void BeginModel(string modelName)
            {
                _currentModelName = modelName;
                Report(_completedModels + 1, epoch: 0, (double)_completedModels / _totalModels);
            }

            public void ReportEpoch(int epoch)
            {
                double epochFraction = Math.Min(epoch + 1, _requestedEpochs) / (double)_requestedEpochs;
                Report(_completedModels + 1, epoch + 1, (_completedModels + epochFraction) / _totalModels);
            }

            public void CompleteModel()
            {
                _completedModels = Math.Min(_completedModels + 1, _totalModels);
                Report(_completedModels, _requestedEpochs, (double)_completedModels / _totalModels);
            }

            private void Report(int modelIndex, int epoch, double fraction) =>
                _progress?.Report(new TrainingProgress(
                    _currentModelName,
                    Math.Min(modelIndex, _totalModels),
                    _totalModels,
                    epoch,
                    _requestedEpochs,
                    Math.Clamp(fraction, 0d, 1d)));
        }

        // 260430Codex: 端成分解析はスペクトルデータ loader に委譲し、DeepLearning は学習/予測入口だけを持ちます。
        // 260514Codex: Keras の epoch 境界で token を確認し、batch の途中では止めないキャンセル callback です。
        // 260605Claude: ボトルネック特定のため、エポック開始/終了で経過時間と loss を tf-train-debug.log に残す。
        private sealed class CancellationTrainingCallback : ICallback
        {
            private readonly CancellationToken _cancellationToken;
            private readonly string _operationName;
            // 260606Codex: The visible training log needs requested epochs and a UI log sink for each epoch.
            private readonly int _requestedEpochs;
            private readonly Action<string> _logAction;
            // 260606Claude: epoch 終了ごとに全体進捗レポーターへ epoch index を渡すためのフック。
            private readonly Action<int>? _onEpochEnd;
            private readonly Stopwatch _epochStopwatch = new();
            private Dictionary<string, List<float>> _history = [];

            // 260605Codex: Track the actual number of epochs completed for fit-end/model-train-end logs.
            public int CompletedEpochs { get; private set; }
            public int LastEpoch { get; private set; } = -1;
            public string LastEpochMetrics { get; private set; } = "";

            public CancellationTrainingCallback(CancellationToken cancellationToken, string operationName, int requestedEpochs, Action<string> logAction, Action<int>? onEpochEnd = null)
            {
                _cancellationToken = cancellationToken;
                _operationName = operationName;
                _requestedEpochs = requestedEpochs;
                _logAction = logAction;
                _onEpochEnd = onEpochEnd;
            }

            public Dictionary<string, List<float>> history
            {
                get => _history;
                set => _history = value;
            }

            public void on_train_begin() => _cancellationToken.ThrowIfCancellationRequested();

            public void on_train_end()
            {
            }

            public void on_epoch_begin(int epoch)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _epochStopwatch.Restart();
                TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={TensorFlowTrainingDebugLog.Clean(_operationName)} epoch={epoch}");
            }

            public void on_epoch_end(int epoch, Dictionary<string, float> epoch_logs)
            {
                long durationMs = _epochStopwatch.ElapsedMilliseconds;
                string logs = FormatEpochLogs(epoch_logs);
                CompletedEpochs++;
                LastEpoch = epoch;
                LastEpochMetrics = logs;
                TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={TensorFlowTrainingDebugLog.Clean(_operationName)} epoch={epoch} durationMs={durationMs} {TensorFlowTrainingDebugLog.Clean(logs)}");

                // 260606Codex: Mirror epoch metrics into the visible training log so EarlyStopping decisions can be checked from the UI.
                _logAction($"  Epoch {epoch + 1}/{_requestedEpochs} [{_operationName}]: {logs}");
                if (!epoch_logs.ContainsKey(EarlyStoppingMonitor))
                {
                    string availableMetrics = string.Join(",", epoch_logs.Keys);
                    _logAction($"  EarlyStopping 警告: 監視値 {EarlyStoppingMonitor} が epoch ログにありません。利用可能: {availableMetrics}");
                    TensorFlowTrainingDebugLog.Write("early-stopping-monitor-missing", $"operation={TensorFlowTrainingDebugLog.Clean(_operationName)} monitor={EarlyStoppingMonitor} available={TensorFlowTrainingDebugLog.Clean(availableMetrics)}");
                }

                // 260606Claude: epoch 完了を全体進捗バーへ反映します（キャンセル確認より前に出して直近の進捗を残す）。
                _onEpochEnd?.Invoke(epoch);
                _cancellationToken.ThrowIfCancellationRequested();
            }

            public void on_train_batch_begin(long step)
            {
            }

            public void on_train_batch_end(long end_step, Dictionary<string, float> logs)
            {
            }

            public void on_test_begin()
            {
            }

            public void on_test_end(Dictionary<string, float> logs)
            {
            }

            public void on_test_batch_begin(long step)
            {
            }

            public void on_test_batch_end(long end_step, Dictionary<string, float> logs)
            {
            }

            public void on_predict_begin()
            {
            }

            public void on_predict_end()
            {
            }

            public void on_predict_batch_begin(long step)
            {
            }

            public void on_predict_batch_end(long end_step, Dictionary<string, Tensors> logs)
            {
            }
        }

        // 260514Codex: 既存の EarlyStopping にキャンセル callback を追加して fit 終了後にも token を確認します。
        // 260605Claude: EarlyStopping が monitor=loss を見ているので validation_split は常に 0。検証 forward pass の無駄を排除する（B-1）。
        //              operationName はエポックログに紐づけてどのモデルの fit かを識別する。
        // 260605Codex: validation_split remains zero because xTest/yTest are passed as explicit validation_data.
        private static TrainingFitResult FitModelWithCancellation(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            NDArray xValidation,
            NDArray yValidation,
            int batchSize,
            int epochs,
            int patience,
            string operationName,
            Action<string> logAction,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 260609Claude: 分類のみ custom loop に分岐(回帰は当面 fit)。同一構成で fit/custom を env で A/B するためのスイッチ。
            if (CustomLoop && operationName.StartsWith("classification", StringComparison.Ordinal))
                return RunCustomClassificationLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken);

            ValidationDataPack validationData = (xValidation, yValidation);
            var trainingCallback = new CancellationTrainingCallback(cancellationToken, operationName, epochs, logAction, reportEpoch);
            model.fit(
                xTrain,
                yTrain,
                batch_size: batchSize,
                epochs: epochs,
                validation_split: 0f,
                validation_data: validationData,
                // 260609Claude: 決定論モードでは毎epoch shuffle を切り再現性を確保する。通常は true。
                shuffle: !Deterministic,
                callbacks: new List<ICallback>
                {
                    trainingCallback,
                    CreateEarlyStopping(model, epochs, patience)
                }
            );
            cancellationToken.ThrowIfCancellationRequested();
            return new TrainingFitResult(epochs, trainingCallback.CompletedEpochs, trainingCallback.LastEpoch, trainingCallback.LastEpochMetrics);
        }

        // 260609Claude: 分類用 custom training loop。fit の毎batch GC.Collect を撤去し、batch-local tensor を明示 Dispose + N batch ごと GC に置換。
        //              GradientTape+Apply+Adam.apply_gradients で fit と同じ最適化を自前で回す。data は1回 tensor 化して tf.slice でバッチ化(NumSharp スライス回避)。
        //              EarlyStopping(val_loss, restore_best_weights) は自前。validation は Apply(training:false) の一括 forward。★Dense-only 前提。
        private static TrainingFitResult RunCustomClassificationLoop(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            NDArray xValidation,
            NDArray yValidation,
            int batchSize,
            int epochs,
            int patience,
            string operationName,
            Action<string> logAction,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string op = TensorFlowTrainingDebugLog.Clean(operationName);
            int gcInterval = CustomLoopGcInterval;
            TensorFlowTrainingDebugLog.Write("custom-loop-start", $"operation={op} gcInterval={gcInterval}");

            var lossFn = keras.losses.SparseCategoricalCrossentropy();
            var optimizer = keras.optimizers.Adam();

            // 260609Claude: 毎batchの NumSharp スライスを避けるため、学習データと検証データを1回だけ native tensor 化する。
            var xAll = tf.constant(xTrain);
            var yAll = tf.constant(yTrain);
            var xVal = tf.constant(xValidation);
            var yVal = tf.constant(yValidation);
            int sampleCount = (int)xTrain.shape[0];
            int features = (int)xTrain.shape[1];
            int valCount = (int)xValidation.shape[0];

            List<NDArray>? bestWeights = null;
            double bestValLoss = double.PositiveInfinity;
            int wait = 0;
            int completedEpochs = 0;
            int lastEpoch = -1;
            string lastEpochMetrics = "";
            long globalStep = 0;
            var epochStopwatch = new Stopwatch();

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                epochStopwatch.Restart();
                TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={op} epoch={epoch} customLoop=1");

                double lossWeightedSum = 0d;
                double lossStepSum = 0d;
                long correct = 0;
                int steps = 0;

                for (int start = 0; start < sampleCount; start += batchSize)
                {
                    int count = Math.Min(batchSize, sampleCount - start);
                    var xb = tf.slice(xAll, new[] { start, 0 }, new[] { count, features });
                    var yb = tf.slice(yAll, new[] { start }, new[] { count });

                    Tensor predT;
                    Tensor lossT;
                    Tensor[] grads;
                    using (var tape = tf.GradientTape())
                    {
                        predT = model.Apply(xb, training: true);
                        lossT = lossFn.Call(yb, predT);
                        grads = tape.gradient(lossT, model.TrainableVariables);
                    }
                    optimizer.apply_gradients(zip(grads, model.TrainableVariables.Select(v => (v as ResourceVariable)!)));

                    float lossValue = lossT.numpy().ToArray<float>()[0];
                    lossWeightedSum += (double)lossValue * count;
                    lossStepSum += lossValue;
                    steps++;

                    // 260609Claude: 訓練 accuracy はネイティブ op で集計し、scalar だけ取り出す。
                    var predIdx = tf.arg_max(predT, 1);
                    var correctT = tf.reduce_sum(tf.cast(tf.equal(predIdx, tf.cast(yb, predIdx.dtype)), tf.int32));
                    correct += correctT.numpy().ToArray<int>()[0];

                    DisposeTensors(xb, yb, predT, lossT, predIdx, correctT);
                    foreach (var g in grads)
                        g?.Dispose();

                    globalStep++;
                    // 260609Claude: fit の毎batch GC.Collect の代わりに N batch ごとにだけ回収して native handle を bound する。
                    if (gcInterval > 0 && globalStep % gcInterval == 0)
                        GC.Collect();
                }

                var (valLoss, valAccuracy) = EvaluateClassification(model, xVal, yVal, valCount, lossFn);
                double trainLoss = sampleCount > 0 ? lossWeightedSum / sampleCount : 0d;
                double trainLossStepMean = steps > 0 ? lossStepSum / steps : 0d;
                double trainAccuracy = sampleCount > 0 ? (double)correct / sampleCount : 0d;

                // 260609Claude: epoch loss は sample-weighted mean を主に、step mean も併記して fit との平均化方式を後で突き合わせる。
                string logs = $"loss={FormatMetric(trainLoss)},accuracy={FormatMetric(trainAccuracy)},val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},loss_step_mean={FormatMetric(trainLossStepMean)}";
                completedEpochs++;
                lastEpoch = epoch;
                lastEpochMetrics = logs;
                TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} durationMs={epochStopwatch.ElapsedMilliseconds} {TensorFlowTrainingDebugLog.Clean(logs)}");
                logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                // 260609Claude: EarlyStopping(monitor=val_loss, min_delta=0, restore_best_weights) を自前で再現。
                if (valLoss < bestValLoss)
                {
                    bestValLoss = valLoss;
                    bestWeights = model.get_weights();
                    wait = 0;
                }
                else if (++wait >= patience)
                {
                    reportEpoch?.Invoke(epoch);
                    break;
                }

                reportEpoch?.Invoke(epoch);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (bestWeights != null)
                model.set_weights(bestWeights);

            DisposeTensors(xAll, yAll, xVal, yVal);
            TensorFlowTrainingDebugLog.Write("custom-loop-end", $"operation={op} trainedEpochs={completedEpochs} lastEpoch={lastEpoch} bestValLoss={FormatMetric(bestValLoss)}");
            return new TrainingFitResult(epochs, completedEpochs, lastEpoch, lastEpochMetrics);
        }

        // 260609Claude: validation を Keras evaluate(DataHandler 経路)に戻さず、Apply(training:false) の一括 forward で val_loss / val_accuracy を出す。検証 tensor は呼び出し側で使い回すのでここでは破棄しない。
        private static (double ValLoss, double ValAccuracy) EvaluateClassification(
            Model model, Tensor xValidation, Tensor yValidation, int total, ILossFunc lossFn)
        {
            Tensor preds = model.Apply(xValidation, training: false);
            Tensor lossT = lossFn.Call(yValidation, preds);
            double valLoss = lossT.numpy().ToArray<float>()[0];

            var predIdx = tf.arg_max(preds, 1);
            var correctT = tf.reduce_sum(tf.cast(tf.equal(predIdx, tf.cast(yValidation, predIdx.dtype)), tf.int32));
            double valAccuracy = total > 0 ? (double)correctT.numpy().ToArray<int>()[0] / total : 0d;

            DisposeTensors(preds, lossT, predIdx, correctT);
            return (valLoss, valAccuracy);
        }

        // 260609Claude: batch-local tensor を明示破棄して native handle を即解放する(N batch GC と併用して leak を抑える)。
        private static void DisposeTensors(params Tensor[] tensors)
        {
            foreach (var t in tensors)
                t?.Dispose();
        }

        // 260609Claude: GUI なしで分類学習(custom loop / fit)を回す開発用ヘッドレス smoke test。env MINERASCOPE_HEADLESS_TRAIN=1 で Program から呼ぶ。
        //              合成だが学習可能なデータ(クラス中心+小ノイズ)を実データ相当の形 [n,2048]/K クラスで作り、最適化が正しく回るか・速度・leak を実 pool 無しで検証する。
        //              合成なので精度の意味は無い(実データの精度 A/B は GUI/実 pool で別途)。結果は tf-train-debug.log に出る。
        internal static void RunHeadlessSmokeTest(Action<string> log)
        {
            int samples = ReadIntEnv("MINERASCOPE_SMOKE_SAMPLES", 6000);
            int classes = ReadIntEnv("MINERASCOPE_SMOKE_CLASSES", 29);
            int epochs = ReadIntEnv("MINERASCOPE_SMOKE_EPOCHS", 5);
            int batchSize = ReadIntEnv("MINERASCOPE_SMOKE_BATCH", 128);
            int valSamples = Math.Max(classes, samples / 5);

            TensorFlowTrainingDebugLog.Write("smoke-start", $"customLoop={CustomLoop} samples={samples} classes={classes} epochs={epochs} batchSize={batchSize}");
            log($"headless smoke test: customLoop={CustomLoop} samples={samples} classes={classes} epochs={epochs} batch={batchSize}");

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
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" });

            var sw = Stopwatch.StartNew();
            var result = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, 10, "classification:Smoke", log, null, default);
            sw.Stop();

            TensorFlowTrainingDebugLog.Write("smoke-end", $"customLoop={CustomLoop} totalMs={sw.ElapsedMilliseconds} trainedEpochs={result.CompletedEpochs} finalMetrics={TensorFlowTrainingDebugLog.Clean(result.LastEpochMetrics)}");
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

        // 260609Claude: 等価性テスト。同一初期重み W0 を fit と custom loop の両方へ set_weights でコピーし、同じデータ・同じ順序(shuffle off)で回して更新後 weight を突き合わせる。
        //              run間決定論は不要(W0 を明示コピーするため)。FP残ノイズを抑えるため単スレッド env (TF_NUM_INTRAOP/INTEROP_THREADS=1, OMP_NUM_THREADS=1) で起動推奨。
        //              実装バグ(Adam slot/timestep, loss reduction, batch順, partial batch)があれば weight 差が FP ノイズ(~1e-5)を大きく超える。env MINERASCOPE_HEADLESS_PARITY=1 で Program から呼ぶ。
        internal static void RunHeadlessParityTest(Action<string> log)
        {
            // 260609Claude: TF_DETERMINISTIC_OPS=1 では random init op に seed が要る。重み初期化前に graph seed を設定して落ちないようにする(W0 は両経路へコピーするので値自体は不問)。
            tf.set_random_seed(42);
            int classes = ReadIntEnv("MINERASCOPE_PARITY_CLASSES", 8);
            var rng = new Random(42);
            float[,] centers = new float[classes, SpectrumLength];
            for (int k = 0; k < classes; k++)
                for (int j = 0; j < SpectrumLength; j++)
                    centers[k, j] = (float)rng.NextDouble();

            TensorFlowTrainingDebugLog.Write("parity-start", $"classes={classes}");
            log($"parity test: classes={classes}");
            // T1: 1 batch / 1 epoch(restore は 1 epoch なので確実に no-op、最もクリーンな実装バグ検出)。
            RunOneParityCase(log, "T1-1batch-1epoch", classes, centers, rng, samples: 64, batchSize: 64, epochs: 1);
            // T2: 4 batch x 2 epoch(Adam slot 初期化 + timestep 継続 + epoch ループ)。
            RunOneParityCase(log, "T2-4batch-2epoch", classes, centers, rng, samples: 256, batchSize: 64, epochs: 2);
            // T3: partial batch を含む(2,2,1)。partial batch の勾配・平均化を検出。
            RunOneParityCase(log, "T3-partialbatch", classes, centers, rng, samples: 5, batchSize: 2, epochs: 2);
            TensorFlowTrainingDebugLog.Write("parity-end");
            log("parity test done");
        }

        // 260609Claude: 1 ケース分の parity。W0 から fit と custom を回し、更新後 weight の最大絶対/相対差を出す。
        //              ※この合成データは val_loss が単調減少するので custom の restore_best_weights は best=last=no-op。よって fit の last-epoch weight と直接比較できる。
        private static void RunOneParityCase(Action<string> log, string name, int classes, float[,] centers, Random rng, int samples, int batchSize, int epochs)
        {
            var (x, y) = MakeSyntheticClassificationData(samples, classes, centers, rng);
            int patience = epochs + 5;

            var model = CreateClassificationModel(classes);
            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" });
            var w0 = model.get_weights();

            model.set_weights(w0);
            model.fit(x, y, batch_size: batchSize, epochs: epochs, verbose: 0, shuffle: false);
            var wFit = model.get_weights();

            model.set_weights(w0);
            RunCustomClassificationLoop(model, x, y, x, y, batchSize, epochs, patience, "classification:Parity", log, null, default);
            var wCustom = model.get_weights();

            var (maxAbs, maxRel) = MaxWeightDiff(wFit, wCustom);
            TensorFlowTrainingDebugLog.Write("parity-case", $"case={name} samples={samples} batch={batchSize} epochs={epochs} weightTensors={wFit.Count} maxAbsDiff={FormatMetric(maxAbs)} maxRelDiff={FormatMetric(maxRel)}");
            log($"parity {name}: weightTensors={wFit.Count} maxAbsDiff={maxAbs:G6} maxRelDiff={maxRel:G6}");
        }

        // 260609Claude: 2 つの重みセットの要素ごと最大絶対差と最大相対差(atol 1e-6 で小重みのゼロ割を回避)。
        private static (double MaxAbs, double MaxRel) MaxWeightDiff(IReadOnlyList<NDArray> a, IReadOnlyList<NDArray> b)
        {
            double maxAbs = 0d;
            double maxRel = 0d;
            int tensorCount = Math.Min(a.Count, b.Count);
            for (int i = 0; i < tensorCount; i++)
            {
                float[] fa = a[i].ToArray<float>();
                float[] fb = b[i].ToArray<float>();
                int m = Math.Min(fa.Length, fb.Length);
                for (int j = 0; j < m; j++)
                {
                    double abs = Math.Abs((double)fa[j] - fb[j]);
                    if (abs > maxAbs)
                        maxAbs = abs;
                    double rel = abs / (Math.Abs((double)fb[j]) + 1e-6);
                    if (rel > maxRel)
                        maxRel = rel;
                }
            }

            return (maxAbs, maxRel);
        }

        #region モデル訓練
        // 260507Codex: 新方式では manifest の Completed から選ばれた spectrum だけを学習に使います。
        // 260514Codex: workflow から渡された token を分類と各回帰モデルの学習へ伝播します。
        internal void RunTraining(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            IProgress<TrainingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputPath))
            {
                Log("モデルの保存先を指定してください。");
                return;
            }

            var orderedPools = trainingPools
                .Where(pool => pool.Samples.Count > 0)
                .OrderBy(pool => pool.MineralName)
                .ToArray();

            // 260605Claude: 全モデルを通した累計時間を測り、分類1個+回帰N個のうちどこに時間が偏るかを比較できるようにする。
            var runTimer = Stopwatch.StartNew();
            int regressionCount = orderedPools.Count(pool => pool.EndmemberNames.Count >= 2);
            TensorFlowTrainingDebugLog.Write("training-run-start", $"pools={orderedPools.Length} regressionModels={regressionCount} epochs={epochs} batchSize={batchSize} patience={patience} deterministic={Deterministic}");

            // 260606Claude: 分類1個+回帰N個を1本のバーで表す。各モデルを BeginModel/CompleteModel で挟み、epoch 進捗は reporter.ReportEpoch で報告する。
            var reporter = new TrainingProgressReporter(progress, 1 + regressionCount, epochs);

            // 260606Codex: 回帰モデルで再利用する spectrum だけを分類ロード時に保持し、重複ファイル読込を避けます。
            var spectrumCache = new SpectrumDataLoader.NormalizedSpectrumCache(
                orderedPools
                    .Where(pool => pool.EndmemberNames.Count >= 2)
                    .SelectMany(pool => pool.Samples)
                    .Select(sample => sample.FilePath));
            TensorFlowTrainingDebugLog.Write("spectrum-cache-created", $"cacheableSpectra={spectrumCache.CacheablePathCount}");

            cancellationToken.ThrowIfCancellationRequested();
            string classificationOutputPath = Path.Combine(outputPath, "AllMinerals_Classification");
            Log("分類モデル学習開始");
            reporter.BeginModel("AllMinerals_Classification");
            TrainClassificationModel(orderedPools, epochs, batchSize, patience, testSplit, classificationOutputPath, spectrumCache, reporter.ReportEpoch, cancellationToken);
            reporter.CompleteModel();

            foreach (var pool in orderedPools.Where(pool => pool.EndmemberNames.Count >= 2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string regressionOutputPath = Path.Combine(outputPath, $"{pool.MineralName}_Regression");
                Log($"回帰モデル学習開始: {pool.MineralName}");
                reporter.BeginModel($"{pool.MineralName}_Regression");
                TrainRegressionModel(pool, epochs, batchSize, patience, testSplit, regressionOutputPath, spectrumCache, reporter.ReportEpoch, cancellationToken);
                reporter.CompleteModel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            Log(" 指定された全鉱物の処理が完了しました");
            TensorFlowTrainingDebugLog.Write("training-run-end", $"totalMs={runTimer.ElapsedMilliseconds}");
        }

        // 260507Codex: manifest の endmemberFractions から回帰ラベルを作り、ファイル名パースを通らずに学習します。
        // 260514Codex: manifest 由来の回帰学習でも読み込み、fit、評価、保存の境目でキャンセルを確認します。
        // 260605Claude: 各ステージで Stopwatch を取り、データ読み込み/split/build/fit/evaluate/save の所要時間を tf-train-debug.log に残す。
        private void TrainRegressionModel(
            SpectrumTrainingPool trainingPool,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            SpectrumDataLoader.NormalizedSpectrumCache? spectrumCache,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken)
        {
            string op = $"regression:{trainingPool.MineralName}";
            var modelTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("model-train-start", $"op={op} epochs={epochs} batchSize={batchSize} patience={patience} samples={trainingPool.Samples.Count}");

            TensorFlowTrainingDebugLog.Write("clear-session-before", $"op={op}");
            keras.backend.clear_session();
            TensorFlowTrainingDebugLog.Write("clear-session-after", $"op={op}");
            tf.set_random_seed(42);
            Log("端成分割合予測モデル \n");
            Log($"対象鉱物: {trainingPool.MineralName}");

            Log("manifest からスペクトルデータを読み込み中...");
            var dataTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("data-load-start", $"op={op}");
            var (allSpectra, allLabels, componentIdx) = SpectrumDataLoader.LoadRegressionData(trainingPool, cancellationToken, spectrumCache);
            TensorFlowTrainingDebugLog.Write("data-load-end", $"op={op} spectra={allSpectra.shape[0]} durationMs={dataTimer.ElapsedMilliseconds}{FormatSpectrumCacheStats(spectrumCache)}");
            ComponentIndex = componentIdx;

            if (allSpectra.shape[0] == 0 || ComponentIndex == null)
            {
                Log("エラー: 端成分情報が見つかりません。");
                TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=skipped totalMs={modelTimer.ElapsedMilliseconds}");
                return;
            }

            Log($"  ファイル数: {allSpectra.shape[0]}");
            Log($"  端成分数: {ComponentIndex.Count}");
            Log($"  端成分一覧:");
            foreach (var kvp in ComponentIndex.OrderBy(x => x.Value))
            {
                Log($"    [{kvp.Value}] {kvp.Key}");
            }
            Log("");

            var splitTimer = Stopwatch.StartNew();
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitRegression(
                allSpectra, allLabels, testSize: testSplit, randomState: 42);
            TensorFlowTrainingDebugLog.Write("split-end", $"op={op} train={xTrain.shape[0]} test={xTest.shape[0]} durationMs={splitTimer.ElapsedMilliseconds}");
            Log($"  訓練データ: {xTrain.shape[0]}件");
            Log($"  テストデータ: {xTest.shape[0]}件\n");

            var buildTimer = Stopwatch.StartNew();
            var model = CreateRegressionModel(ComponentIndex.Count);
            Log($"  入力: {SpectrumLength}次元スペクトル");
            Log($"  隠れ層: Dense(64, relu) → Dense(64, relu)");

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.MeanSquaredError(),
                metrics: new[] { "mae" }
            );
            TensorFlowTrainingDebugLog.Write("build-compile-end", $"op={op} durationMs={buildTimer.ElapsedMilliseconds}");

            var fitTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("fit-start", $"op={op}");
            var fitResult = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, patience, op, _logAction, reportEpoch, cancellationToken);
            TensorFlowTrainingDebugLog.Write("fit-end", $"op={op} durationMs={fitTimer.ElapsedMilliseconds} requestedEpochs={fitResult.RequestedEpochs} trainedEpochs={fitResult.CompletedEpochs} lastEpoch={fitResult.LastEpoch} earlyStoppingMonitor={EarlyStoppingMonitor} patience={patience} finalEpochMetrics={TensorFlowTrainingDebugLog.Clean(fitResult.LastEpochMetrics)}");
            // 260606Codex: Show the actual epoch count beside the EarlyStopping settings used for this fit.
            Log($"  学習エポック数: {fitResult.CompletedEpochs}/{fitResult.RequestedEpochs} (monitor={EarlyStoppingMonitor}, patience={patience})");

            Log("モデル評価中");
            cancellationToken.ThrowIfCancellationRequested();
            var evalTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("evaluate-start", $"op={op}");
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testMae = GetMetricValue(score, 1, "mae", "mean_absolute_error");
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} durationMs={evalTimer.ElapsedMilliseconds} testLoss={FormatMetric(testLoss)} testMae={FormatMetric(testMae)} trainedEpochs={fitResult.CompletedEpochs}");

            Log($" 評価完了");
            Log($"  Test Loss (MSE): {testLoss:F6}");

            if (testMae > 0)
            {
                Log($"  Test MAE: {testMae:F6}\n");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);

            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("save-start", $"op={op}");
            model.save(outputPath);
            TensorFlowTrainingDebugLog.Write("save-end", $"op={op} durationMs={saveTimer.ElapsedMilliseconds}");
            cancellationToken.ThrowIfCancellationRequested();

            string componentPath = Path.Combine(outputPath, "componentIndex.json");
            File.WriteAllText(componentPath, System.Text.Json.JsonSerializer.Serialize(componentIdx));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "regression");

            string mineralNamePath = Path.Combine(outputPath, "mineralName.txt");
            File.WriteAllText(mineralNamePath, trainingPool.MineralName);

            Log($" モデル保存完了: {outputPath}\n");
            TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=ok totalMs={modelTimer.ElapsedMilliseconds} trainedEpochs={fitResult.CompletedEpochs} testLoss={FormatMetric(testLoss)} testMae={FormatMetric(testMae)}");
        }

        // 260430Codex: 分類学習側も tuple 展開と共通評価 helper で回帰学習と読み方をそろえます。
        // 260507Codex: 新方式の分類学習は manifest 由来の pool だけを読み込みます。
        // 260514Codex: 分類学習でもデータ読み込み、fit、評価、保存の境目でキャンセルを確認します。
        // 260605Claude: 回帰側と同様にステージ計測ログを追加し、データ読み込み/fit/evaluate/save のどこが支配的か特定できるようにする。
        private void TrainClassificationModel(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            SpectrumDataLoader.NormalizedSpectrumCache? spectrumCache,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken)
        {
            const string op = "classification:AllMinerals";
            var modelTimer = Stopwatch.StartNew();
            int totalSamples = trainingPools.Sum(p => p.Samples.Count);
            TensorFlowTrainingDebugLog.Write("model-train-start", $"op={op} epochs={epochs} batchSize={batchSize} patience={patience} pools={trainingPools.Count} samples={totalSamples}");

            TensorFlowTrainingDebugLog.Write("clear-session-before", $"op={op}");
            keras.backend.clear_session();
            TensorFlowTrainingDebugLog.Write("clear-session-after", $"op={op}");
            tf.set_random_seed(42);
            Log($"\n 訓練データ読み込み中");

            var dataTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("data-load-start", $"op={op}");
            var (allSpectra, allLabelsList, loadStats) = SpectrumDataLoader.LoadClassificationData(trainingPools, cancellationToken, spectrumCache);
            // 260607Codex: Log bounded parallel classification load stats so data-load speedups can be compared safely.
            TensorFlowTrainingDebugLog.Write("data-load-end", $"op={op} spectra={allSpectra.shape[0]} durationMs={dataTimer.ElapsedMilliseconds} inputSamples={loadStats.InputSamples} loadedSamples={loadStats.LoadedSamples} skippedSamples={loadStats.SkippedSamples} parallelDegree={loadStats.ParallelDegree}{FormatSpectrumCacheStats(spectrumCache)}");
            Log($"\n 読み込み完了 (合計 {allSpectra.shape[0]} 件)\n");
            var (labelsEncoded, encoder) = DeepLearningDataSplitter.EncodeLabels(allLabelsList);
            if (encoder.Count > 0)
            {
                foreach (var kvp in encoder.OrderBy(x => x.Value))
                {
                    int count = allLabelsList.Count(l => l == kvp.Key);
                    Log($"  [{kvp.Value}] {kvp.Key} ({count}件)");
                }
            }

            var splitTimer = Stopwatch.StartNew();
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(allSpectra, labelsEncoded, testSize: testSplit, randomState: 42);
            TensorFlowTrainingDebugLog.Write("split-end", $"op={op} train={xTrain.shape[0]} test={xTest.shape[0]} durationMs={splitTimer.ElapsedMilliseconds}");
            Log($"訓練データ: {xTrain.shape[0]}");
            Log($"テストデータ: {xTest.shape[0]}\n");

            var buildTimer = Stopwatch.StartNew();
            var model = CreateClassificationModel(encoder.Count);

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );
            TensorFlowTrainingDebugLog.Write("build-compile-end", $"op={op} durationMs={buildTimer.ElapsedMilliseconds}");

            Log("訓練中...");
            var fitTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("fit-start", $"op={op}");
            var fitResult = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, patience, op, _logAction, reportEpoch, cancellationToken);
            TensorFlowTrainingDebugLog.Write("fit-end", $"op={op} durationMs={fitTimer.ElapsedMilliseconds} requestedEpochs={fitResult.RequestedEpochs} trainedEpochs={fitResult.CompletedEpochs} lastEpoch={fitResult.LastEpoch} earlyStoppingMonitor={EarlyStoppingMonitor} patience={patience} finalEpochMetrics={TensorFlowTrainingDebugLog.Clean(fitResult.LastEpochMetrics)}");
            // 260606Codex: Show the actual epoch count beside the EarlyStopping settings used for this fit.
            Log($"  学習エポック数: {fitResult.CompletedEpochs}/{fitResult.RequestedEpochs} (monitor={EarlyStoppingMonitor}, patience={patience})");
            Log("モデル評価中");

            cancellationToken.ThrowIfCancellationRequested();
            var evalTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("evaluate-start", $"op={op}");
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testAccuracy = GetMetricValue(score, 1, "accuracy");
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} durationMs={evalTimer.ElapsedMilliseconds} testLoss={FormatMetric(testLoss)} testAccuracy={FormatMetric(testAccuracy)} trainedEpochs={fitResult.CompletedEpochs}");

            Log($"Test loss: {testLoss:F4}");
            Log($"Test accuracy: {testAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, "labelEncoder.json");
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "classification");
            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("save-start", $"op={op}");
            model.save(outputPath);
            TensorFlowTrainingDebugLog.Write("save-end", $"op={op} durationMs={saveTimer.ElapsedMilliseconds}");
            cancellationToken.ThrowIfCancellationRequested();
            TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=ok totalMs={modelTimer.ElapsedMilliseconds} trainedEpochs={fitResult.CompletedEpochs} testLoss={FormatMetric(testLoss)} testAccuracy={FormatMetric(testAccuracy)}");
        }

        #endregion
        #region 学習済みモデルを利用して予測
        // 260522Codex: 予測はファイル走査とログ整形だけを担い、推論は分類/回帰サービスへ委譲します。
        public void RunPrediction(string modelPath, List<string> files, string assemblyPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                {
                    Log("モデルの保存先フォルダが指定されていないか、存在しません。");
                    return;
                }

                string classificationPath = Path.Combine(modelPath, "AllMinerals_Classification");
                if (!Directory.Exists(classificationPath))
                {
                    Log("分類モデルが見つかりません。");
                    return;
                }

                if (!File.Exists(Path.Combine(classificationPath, "labelEncoder.json")))
                {
                    Log("labelEncoder.json が見つかりません。");
                    return;
                }

                if (files.Count == 0)
                {
                    Log("予測対象のファイルがありません。");
                    return;
                }

                var classificationService = new MineralClassificationPredictionService();
                var regressionService = new MineralRegressionPredictionService();

                foreach (var filePath in files)
                {
                    var spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath);
                    if (spectrum == null)
                        continue;

                    var classification = classificationService.Predict(modelPath, spectrum);

                    Log($"\r\nファイル: {Path.GetFileName(filePath)}");
                    Log("【分類結果】");
                    Log($"  予測鉱物: {classification.PredictedMineral} ({classification.Confidence * 100:F2}%)");
                    foreach (var probability in classification.Probabilities)
                        Log($"    {probability.MineralName}: {probability.Confidence * 100:F2}%");

                    // 260522Codex: 予測鉱物名に対応する回帰モデルだけを成分比率予測の候補にします。
                    string[] candidates = Directory.GetDirectories(modelPath, $"{classification.PredictedMineral}*_Regression");
                    if (candidates.Length == 0)
                        continue;

                    Log("【成分比率予測】");
                    foreach (var regressionPath in candidates)
                        LogRegressionResult(regressionService, regressionPath, spectrum, classification.PredictedMineral, assemblyPath);
                }
            }
            catch (Exception ex)
            {
                Log($"\nエラー: {ex.Message}");
                Log($"スタックトレース: {ex.StackTrace}");
            }
        }

        // 260522Codex: 1 つの回帰候補を推論し、端成分比率と化学組成式をログへ出力します。
        private void LogRegressionResult(
            MineralRegressionPredictionService regressionService,
            string regressionPath,
            float[] normalizedSpectrum,
            string predictedMineral,
            string assemblyPath)
        {
            Log($"\n  モデル: {Path.GetFileName(regressionPath)}");

            MineralRegressionResult regression;
            try
            {
                regression = regressionService.Predict(regressionPath, normalizedSpectrum);
            }
            catch (Exception ex)
            {
                Log($"  回帰予測をスキップしました: {ex.Message}");
                return;
            }

            var resultRatios = new Dictionary<string, float>();
            Log("  予測端成分比率:");
            foreach (var component in regression.Components)
            {
                resultRatios[component.ComponentName] = component.Ratio;
                Log($"    {component.ComponentName}: {component.Ratio * 100:F0}");
            }

            Log($" 化学組成式: {GenerateFormula(resultRatios, predictedMineral, assemblyPath)}");
        }
        #endregion
        //分類モデルで予測された固溶体の化学組成を生成
        // 260430Codex: 既存公開メソッドは残し、化学式生成の実処理は専用 helper へ委譲します。
        public string GenerateFormula(Dictionary<string, float> predictedRatios, string targetMineralName, string assemblyPath) =>
            MineralFormulaGenerator.Generate(predictedRatios, targetMineralName, assemblyPath);
    }
}

