using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Tensorflow;
using Tensorflow.Keras;
using Tensorflow.Keras.Engine;
using Tensorflow.Keras.Losses;
using Tensorflow.NumPy;
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

    // 260902Codex: 想定内の学習中止理由を、予期しない例外と分けて workflow へ返します。
    internal enum DeepLearningTrainingFailureKind
    {
        TrainingDataLoadFailed
    }

    // 260902Codex: 学習本体が正常完了したか、想定内の理由で開始前に止まったかを表します。
    internal enum DeepLearningTrainingStatus
    {
        Completed,
        NotCompleted
    }

    // 260902Codex: 分類で打ち切った場合に回帰と正式昇格へ進ませないための内部結果です。
    internal sealed record DeepLearningTrainingResult
    {
        private DeepLearningTrainingResult(
            DeepLearningTrainingStatus status,
            DeepLearningTrainingFailureKind? failureKind,
            string? failureReason,
            IReadOnlyList<TrainingModelMetrics> modelMetrics)
        {
            Status = status;
            FailureKind = failureKind;
            FailureReason = failureReason;
            ModelMetrics = modelMetrics;
        }

        public DeepLearningTrainingStatus Status { get; }

        public DeepLearningTrainingFailureKind? FailureKind { get; }

        public string? FailureReason { get; }

        public IReadOnlyList<TrainingModelMetrics> ModelMetrics { get; }

        public static DeepLearningTrainingResult CreateCompleted(IReadOnlyList<TrainingModelMetrics> modelMetrics)
        {
            ArgumentNullException.ThrowIfNull(modelMetrics);
            if (modelMetrics.Count == 0)
                throw new ArgumentException("At least one model metric is required for a completed training run.", nameof(modelMetrics));
            return new(DeepLearningTrainingStatus.Completed, failureKind: null, failureReason: null, modelMetrics);
        }

        public static DeepLearningTrainingResult CreateNotCompleted(
            DeepLearningTrainingFailureKind failureKind,
            string failureReason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            return new(DeepLearningTrainingStatus.NotCompleted, failureKind, failureReason, Array.Empty<TrainingModelMetrics>());
        }
    }

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
        // 260803Codex: ローダーの実行時値化に合わせ、モデル入力長も static readonly で追従します。
        private static readonly int SpectrumLength = SpectrumDataLoader.SpectrumLength;

        // 260514Codex: 分類モデルの層構成を 1 箇所に集め、入力長とクラス数だけを呼び出し側から渡します。
        // 260622Claude: 分類学習は graph 経路(RunGraphClassificationLoop)が既定で、loss は内部で sparse_softmax_cross_entropy_with_logits
        //              (from_logits=True 相当=数値安定・Keras推奨)を使う。モデルは評価・保存用に従来どおり softmax 出力にする。
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

        // 260605Codex: Keep debug metric formatting culture-stable for log parsing.
        private static string FormatMetric(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

        // 260606Codex: Cache counters are cumulative for the training run, so log them with each stage for before/after comparison.
        private static string FormatSpectrumCacheStats(SpectrumDataLoader.NormalizedSpectrumCache? cache) =>
            cache is null
                ? string.Empty
                : $" cacheableSpectra={cache.CacheablePathCount} cachedSpectra={cache.CachedCount} cacheHits={cache.HitCount} cacheMisses={cache.MissCount} cacheStores={cache.StoreCount}";

        // 260605Codex: Patience is intended to watch validation/test loss rather than training loss.
        // 260906Codex: Keep the persisted training result tied to the exact early-stopping monitor used by graph training.
        internal const string EarlyStoppingMonitor = "val_loss";

        // 260906Codex: Describe the actually selected splitter rather than inferring it later from the pool type.
        private const string LegacySplitMethod = "legacy-random-v1";

        // 260622Claude: 評価・保存モデルは softmax 出力なので loss は from_logits=false。学習自体は graph 経路が logits で安定計算する。
        private static ILossFunc CreateSparseCategoricalCrossentropy() =>
            keras.losses.SparseCategoricalCrossentropy(from_logits: false);

        // 260605Codex: Return the observed fit result because TensorFlow.Keras History is not reliable enough here.
        // 260906Codex: Metrics are from the best validation epoch, not an independent test set.
        private sealed record TrainingFitResult(
            int RequestedEpochs,
            int CompletedEpochs,
            int LastEpoch,
            string LastEpochMetrics,
            int? BestEpoch,
            double BestValidationLoss,
            double BestValidationMetric,
            // 260930Codex: Preserve strict source-label accuracy beside overlap-aware accuracy.
            double? LegacyValidationAccuracy = null);

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
        // 260612Claude: 分類・回帰とも自前 custom loop で学習する(fit の毎batch GC 経路を避ける)。op 名で回帰/分類を選ぶ。
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
            CancellationToken cancellationToken,
            // 260930Codex: Alternative labels are used only by classification; regression stays unchanged.
            int[]? trainingAlternatives = null, int[]? validationAlternatives = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 260622Claude: 分類・回帰とも v1 Graph/Session 経路で学習する(高速化の本命、eager と統計的同等を実測確認済み)。
            if (operationName.StartsWith("regression", StringComparison.Ordinal))
                return RunGraphRegressionLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken);
            return RunGraphClassificationLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken, trainingAlternatives, validationAlternatives);
        }

        // 260621Claude: 分類の v1-style Graph/Session 学習(高速化の本命)。eager custom loop の per-batch dispatch を畳んで大幅高速化する。
        //   - to_graph は外部変数を capture できないため、manual Dense(W/b を自前変数)で graph を組み Session.run で train op を回す。
        //   - 研究比較のため: 初期重みは渡された Keras model の get_weights() を注入し、Adam hyperparams を Keras Adam と一致(epsilon=1e-7)させる。
        //   - eager は global 無効化せず graph.as_default() スコープ内だけ graph モード。学習後 best weights を model に set_weights し、
        //     呼び出し側の evaluate/save(load_model 互換) をそのまま使う。★Dense-only 前提。
        //   - 全データは graph 内 constant に置き start だけ feed して gather(tf.slice は動的 begin 不可)。巨大データで GraphDef が問題化したら feed-once Variable へ。
        private static TrainingFitResult RunGraphClassificationLoop(
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
            CancellationToken cancellationToken,
            int[]? trainingAlternatives, int[]? validationAlternatives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string op = TensorFlowTrainingDebugLog.Clean(operationName);

            // 初期重みを eager で確定させる(eager 経路と同一初期化にする)。未 build なら build してから取り出す。
            int features = (int)xTrain.shape[1];
            var initWeights = model.get_weights();
            if (initWeights == null || initWeights.Count == 0)
            {
                model.build(new Shape(-1, features));
                initWeights = model.get_weights();
            }
            TensorFlowTrainingDebugLog.Write("graph-loop-start", $"operation={op} engine=graph weights={initWeights.Count}");

            int sampleCount = (int)xTrain.shape[0];
            int valCount = (int)xValidation.shape[0];
            List<NDArray>? bestWeights = null;
            double bestValLoss = double.PositiveInfinity;
            double bestValAccuracy = 0d;
            // 260930Codex: Track the original one-label metric at the adopted epoch.
            double bestLegacyAccuracy = 0d;
            int? bestEpoch = null;
            int wait = 0;
            int completedEpochs = 0;
            int lastEpoch = -1;
            string lastEpochMetrics = "";
            var epochStopwatch = new Stopwatch();

            var graph = tf.Graph();
            graph.as_default();
            try
            {
                var dataX = tf.constant(xTrain);
                var dataY = tf.constant(yTrain, dtype: TF_DataType.TF_INT32);
                var valX = tf.constant(xValidation);
                var valY = tf.constant(yValidation, dtype: TF_DataType.TF_INT32);
                // 260930Codex: Keep alternative targets aligned with the original classification split.
                var dataAlternative = trainingAlternatives is null ? dataY : tf.constant(trainingAlternatives);
                var valAlternative = validationAlternatives is null ? valY : tf.constant(validationAlternatives);
                int classCount = (int)initWeights[5].shape[0];

                var W1 = tf.Variable(tf.constant(initWeights[0]), name: "W1");
                var b1 = tf.Variable(tf.constant(initWeights[1]), name: "b1");
                var W2 = tf.Variable(tf.constant(initWeights[2]), name: "W2");
                var b2 = tf.Variable(tf.constant(initWeights[3]), name: "b2");
                var W3 = tf.Variable(tf.constant(initWeights[4]), name: "W3");
                var b3 = tf.Variable(tf.constant(initWeights[5]), name: "b3");
                var weightVars = new[] { W1, b1, W2, b2, W3, b3 };

                Tensor Forward(Tensor x)
                {
                    var h1 = tf.nn.relu(tf.matmul(x, W1.AsTensor()) + b1.AsTensor());
                    var h2 = tf.nn.relu(tf.matmul(h1, W2.AsTensor()) + b2.AsTensor());
                    return tf.matmul(h2, W3.AsTensor()) + b3.AsTensor();
                }

                var startPh = tf.placeholder(tf.int32, new Shape(Array.Empty<int>()), "start");
                var idx = tf.range(startPh, tf.add(startPh, tf.constant(batchSize)));
                // 260930Codex: Sum acceptable class probability for the four feldspar intersections.
                var trainLoss = tf.reduce_mean(PartialLabelLoss.PerSample(Forward(tf.gather(dataX, idx)), tf.gather(dataY, idx), tf.gather(dataAlternative, idx), classCount));
                // 260621Claude: Keras Adam と同一の hyperparams(epsilon=1e-7)で minimize し、研究比較の同等性を保つ。
                var optimizer = new Tensorflow.Train.AdamOptimizer(0.001f, 0.9f, 0.999f, 1e-7f, false, TF_DataType.TF_FLOAT, "Adam");
                var trainOp = optimizer.minimize(trainLoss);

                var valLogits = Forward(valX);
                var valLossOp = tf.reduce_mean(PartialLabelLoss.PerSample(valLogits, valY, valAlternative, classCount));
                var legacyCorrectOp = tf.reduce_sum(tf.cast(tf.equal(tf.arg_max(valLogits, 1), tf.cast(valY, tf.int64)), tf.int32));
                var valCorrectOp = tf.reduce_sum(tf.cast(PartialLabelLoss.Correct(valLogits, valY, valAlternative), tf.int32));

                // 260621Claude: minimize 後に init を作って Adam の slot variables まで初期化する。
                var init = tf.global_variables_initializer();
                using var sess = tf.Session(graph);
                sess.run(init);

                for (int epoch = 0; epoch < epochs; epoch++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    epochStopwatch.Restart();
                    TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={op} epoch={epoch} engine=graph");

                    long batchLoopStart = Stopwatch.GetTimestamp();
                    for (int start = 0; start + batchSize <= sampleCount; start += batchSize)
                        sess.run(trainOp, new FeedItem(startPh, start));
                    double batchLoopMs = Stopwatch.GetElapsedTime(batchLoopStart).TotalMilliseconds;

                    long valStart = Stopwatch.GetTimestamp();
                    double valLoss = sess.run(valLossOp).ToArray<float>()[0];
                    double valAccuracy = valCount > 0 ? sess.run(valCorrectOp).ToArray<int>()[0] / (double)valCount : 0d;
                    double legacyAccuracy = valCount > 0 ? sess.run(legacyCorrectOp).ToArray<int>()[0] / (double)valCount : 0d;
                    double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                    bool willStop = false;
                    if (valLoss < bestValLoss)
                    {
                        bestValLoss = valLoss;
                        bestValAccuracy = valAccuracy;
                        bestLegacyAccuracy = legacyAccuracy;
                        bestEpoch = epoch + 1;
                        bestWeights = weightVars.Select(v => sess.run(v.AsTensor())).ToList();
                        wait = 0;
                    }
                    else if (++wait >= patience)
                        willStop = true;

                    // 260622Claude: 学習データ全体の metric はログ専用かつ初回ウォームアップが重いので算出しない。val(=test) のみ毎epoch評価する。
                    string logs = $"val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},legacy_val_accuracy={FormatMetric(legacyAccuracy)}";

                    completedEpochs++;
                    lastEpoch = epoch;
                    lastEpochMetrics = logs;
                    TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} engine=graph durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
                    logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                    if (willStop)
                    {
                        reportEpoch?.Invoke(epoch);
                        break;
                    }

                    reportEpoch?.Invoke(epoch);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                graph.Exit();
            }

            // 260621Claude: best weights を eager の Keras model に書き戻し、呼び出し側の save をそのまま使う。
            if (bestWeights != null)
                model.set_weights(bestWeights);
            TensorFlowTrainingDebugLog.Write("graph-loop-end", $"operation={op} engine=graph trainedEpochs={completedEpochs} lastEpoch={lastEpoch} bestValLoss={FormatMetric(bestValLoss)}");
            return new TrainingFitResult(epochs, completedEpochs, lastEpoch, lastEpochMetrics, bestEpoch, bestValLoss, bestValAccuracy, bestLegacyAccuracy);
        }

        // 260622Claude: 回帰の v1-style Graph/Session 学習。分類 graph 経路と同形で、loss を MSE・出力を線形(端成分比率)にしたもの。
        //   初期重みは渡された Keras model から注入、Adam hyperparams を Keras Adam と一致(epsilon=1e-7)させ研究比較の同等性を保つ。
        //   best weights を model へ書き戻し、呼び出し側の evaluate/save をそのまま使う。★Dense-only 前提。
        private static TrainingFitResult RunGraphRegressionLoop(
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

            int features = (int)xTrain.shape[1];
            int outputCount = (int)yTrain.shape[1];
            var initWeights = model.get_weights();
            if (initWeights == null || initWeights.Count == 0)
            {
                model.build(new Shape(-1, features));
                initWeights = model.get_weights();
            }
            TensorFlowTrainingDebugLog.Write("graph-loop-start", $"operation={op} engine=graph kind=regression weights={initWeights.Count}");

            int sampleCount = (int)xTrain.shape[0];
            int valCount = (int)xValidation.shape[0];
            List<NDArray>? bestWeights = null;
            double bestValLoss = double.PositiveInfinity;
            double bestValMae = 0d;
            int? bestEpoch = null;
            int wait = 0;
            int completedEpochs = 0;
            int lastEpoch = -1;
            string lastEpochMetrics = "";
            var epochStopwatch = new Stopwatch();

            var graph = tf.Graph();
            graph.as_default();
            try
            {
                var dataX = tf.constant(xTrain);
                var dataY = tf.constant(yTrain);
                var valX = tf.constant(xValidation);
                var valY = tf.constant(yValidation);

                var W1 = tf.Variable(tf.constant(initWeights[0]), name: "W1");
                var b1 = tf.Variable(tf.constant(initWeights[1]), name: "b1");
                var W2 = tf.Variable(tf.constant(initWeights[2]), name: "W2");
                var b2 = tf.Variable(tf.constant(initWeights[3]), name: "b2");
                var W3 = tf.Variable(tf.constant(initWeights[4]), name: "W3");
                var b3 = tf.Variable(tf.constant(initWeights[5]), name: "b3");
                var weightVars = new[] { W1, b1, W2, b2, W3, b3 };

                Tensor Forward(Tensor x)
                {
                    var h1 = tf.nn.relu(tf.matmul(x, W1.AsTensor()) + b1.AsTensor());
                    var h2 = tf.nn.relu(tf.matmul(h1, W2.AsTensor()) + b2.AsTensor());
                    return tf.matmul(h2, W3.AsTensor()) + b3.AsTensor();
                }

                Tensor MseLoss(Tensor pred, Tensor y) => tf.reduce_mean(tf.square(pred - y));

                var startPh = tf.placeholder(tf.int32, new Shape(Array.Empty<int>()), "start");
                var idx = tf.range(startPh, tf.add(startPh, tf.constant(batchSize)));
                var trainLoss = MseLoss(Forward(tf.gather(dataX, idx)), tf.gather(dataY, idx));
                // 260622Claude: Keras Adam と同一の hyperparams(epsilon=1e-7)で minimize し、研究比較の同等性を保つ。
                var optimizer = new Tensorflow.Train.AdamOptimizer(0.001f, 0.9f, 0.999f, 1e-7f, false, TF_DataType.TF_FLOAT, "Adam");
                var trainOp = optimizer.minimize(trainLoss);

                var valPred = Forward(valX);
                var valLossOp = MseLoss(valPred, valY);
                var valAbsSumOp = tf.reduce_sum(tf.abs(valPred - valY));

                var init = tf.global_variables_initializer();
                using var sess = tf.Session(graph);
                sess.run(init);

                for (int epoch = 0; epoch < epochs; epoch++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    epochStopwatch.Restart();
                    TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={op} epoch={epoch} engine=graph kind=regression");

                    long batchLoopStart = Stopwatch.GetTimestamp();
                    for (int start = 0; start + batchSize <= sampleCount; start += batchSize)
                        sess.run(trainOp, new FeedItem(startPh, start));
                    double batchLoopMs = Stopwatch.GetElapsedTime(batchLoopStart).TotalMilliseconds;

                    long valStart = Stopwatch.GetTimestamp();
                    double valLoss = sess.run(valLossOp).ToArray<float>()[0];
                    double valMae = valCount > 0 && outputCount > 0 ? sess.run(valAbsSumOp).ToArray<float>()[0] / valCount / outputCount : 0d;
                    double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                    bool willStop = false;
                    if (valLoss < bestValLoss)
                    {
                        bestValLoss = valLoss;
                        bestValMae = valMae;
                        bestEpoch = epoch + 1;
                        bestWeights = weightVars.Select(v => sess.run(v.AsTensor())).ToList();
                        wait = 0;
                    }
                    else if (++wait >= patience)
                        willStop = true;

                    // 260622Claude: 学習データ全体の metric はログ専用かつ初回ウォームアップが重いので算出しない。val(=test) のみ毎epoch評価する。
                    string logs = $"val_loss={FormatMetric(valLoss)},val_mean_absolute_error={FormatMetric(valMae)}";

                    completedEpochs++;
                    lastEpoch = epoch;
                    lastEpochMetrics = logs;
                    TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} engine=graph kind=regression durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
                    logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                    if (willStop)
                    {
                        reportEpoch?.Invoke(epoch);
                        break;
                    }

                    reportEpoch?.Invoke(epoch);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                graph.Exit();
            }

            if (bestWeights != null)
                model.set_weights(bestWeights);
            TensorFlowTrainingDebugLog.Write("graph-loop-end", $"operation={op} engine=graph kind=regression trainedEpochs={completedEpochs} lastEpoch={lastEpoch} bestValLoss={FormatMetric(bestValLoss)}");
            return new TrainingFitResult(epochs, completedEpochs, lastEpoch, lastEpochMetrics, bestEpoch, bestValLoss, bestValMae);
        }

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
                optimizer: keras.optimizers.Adam(),
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

        #region モデル訓練
        // 260507Codex: 新方式では manifest の Completed から選ばれた spectrum だけを学習に使います。
        // 260514Codex: workflow から渡された token を分類と各回帰モデルの学習へ伝播します。
        // 260902Codex: 分類データ不足を正常な未完了結果として返し、回帰へ入る前に打ち切ります。
        internal DeepLearningTrainingResult RunTraining(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            double unknownDistanceScale,
            string outputPath,
            IProgress<TrainingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputPath))
            {
                Log("モデルの保存先を指定してください。");
                throw new DirectoryNotFoundException($"モデルの一時保存先が見つかりません: {outputPath}");
            }

            var orderedPools = trainingPools
                .Where(pool => pool.Samples.Count > 0)
                .OrderBy(pool => pool.MineralName)
                .ToArray();

            // 260605Claude: 全モデルを通した累計時間を測り、分類1個+回帰N個のうちどこに時間が偏るかを比較できるようにする。
            var runTimer = Stopwatch.StartNew();
            // 260621Codex: run 全体の CPU 使用率を、論理 CPU 総量に対する割合で後から比較できるようにします。
            using var runProcess = Process.GetCurrentProcess();
            TimeSpan runCpuStart = runProcess.TotalProcessorTime;
            int regressionCount = orderedPools.Count(pool => pool.EndmemberNames.Count >= 2);
            TensorFlowTrainingDebugLog.Write("training-run-start", $"pools={orderedPools.Length} regressionModels={regressionCount} epochs={epochs} batchSize={batchSize} patience={patience}");

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
            // 260727Claude: 分類フォルダ名は ModelArtifactPaths を単一の基準にする (予測側と同じ値)。
            string classificationOutputPath = ModelArtifactPaths.GetClassificationFolder(outputPath);
            Log("分類モデル学習開始");
            reporter.BeginModel(ModelArtifactPaths.ClassificationFolderName);
            var classificationResult = TrainClassificationModel(
                orderedPools,
                epochs,
                batchSize,
                patience,
                testSplit,
                unknownDistanceScale,
                classificationOutputPath,
                spectrumCache,
                reporter.ReportEpoch,
                cancellationToken);
            if (classificationResult.Status == DeepLearningTrainingStatus.NotCompleted)
            {
                TensorFlowTrainingDebugLog.Write(
                    "training-run-end",
                    $"status=not-promoted reason={classificationResult.FailureKind} totalMs={runTimer.ElapsedMilliseconds}");
                return classificationResult;
            }
            if (classificationResult.Status != DeepLearningTrainingStatus.Completed)
                throw new InvalidOperationException($"未対応の学習結果です: {classificationResult.Status}");

            reporter.CompleteModel();
            var modelMetrics = new List<TrainingModelMetrics>(classificationResult.ModelMetrics);

            foreach (var pool in orderedPools.Where(pool => pool.EndmemberNames.Count >= 2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string regressionModelName = pool.MineralName + ModelArtifactPaths.RegressionFolderSuffix;
                string regressionOutputPath = Path.Combine(outputPath, regressionModelName);
                Log($"回帰モデル学習開始: {pool.MineralName}");
                reporter.BeginModel(regressionModelName);
                modelMetrics.Add(TrainRegressionModel(pool, epochs, batchSize, patience, testSplit, regressionOutputPath, spectrumCache, reporter.ReportEpoch, cancellationToken));
                reporter.CompleteModel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            Log(" 指定された全鉱物の処理が完了しました");
            runProcess.Refresh();
            long totalMs = runTimer.ElapsedMilliseconds;
            long runCpuMs = (long)(runProcess.TotalProcessorTime - runCpuStart).TotalMilliseconds;
            double averageLogicalCpuPercent = totalMs > 0 && Environment.ProcessorCount > 0
                ? runCpuMs * 100d / totalMs / Environment.ProcessorCount
                : 0d;
            TensorFlowTrainingDebugLog.Write("training-run-end", $"status=completed totalMs={totalMs} runCpuMs={runCpuMs} avgLogicalCpuPercent={FormatMetric(averageLogicalCpuPercent)} logicalProcessors={Environment.ProcessorCount}");
            return DeepLearningTrainingResult.CreateCompleted(modelMetrics);
        }

        // 260507Codex: manifest の endmemberFractions から回帰ラベルを作り、ファイル名パースを通らずに学習します。
        // 260514Codex: manifest 由来の回帰学習でも読み込み、fit、評価、保存の境目でキャンセルを確認します。
        // 260605Claude: 各ステージで Stopwatch を取り、データ読み込み/split/build/fit/evaluate/save の所要時間を tf-train-debug.log に残す。
        private TrainingModelMetrics TrainRegressionModel(
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
            // 260626Claude: 低エネルギー(C コンタミ)マスクを学習データに適用し、同じ前処理を preprocessing.json に保存する。
            var preprocessing = SpectrumPreprocessing.ForTraining();
            TensorFlowTrainingDebugLog.Write("preprocessing", $"op={op} {preprocessing.Describe()}");
            var dataTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("data-load-start", $"op={op}");
            var (allSpectra, allLabels, componentIdx) = SpectrumDataLoader.LoadRegressionData(trainingPool, cancellationToken, spectrumCache, preprocessing);
            TensorFlowTrainingDebugLog.Write("data-load-end", $"op={op} spectra={allSpectra.shape[0]} durationMs={dataTimer.ElapsedMilliseconds}{FormatSpectrumCacheStats(spectrumCache)}");
            ComponentIndex = componentIdx;

            // 260901Codex: Multi-time training must fail instead of silently changing the requested condition balance.
            bool useSourceMetadata = ResolveSourceMetadataMode(trainingPool.Samples, op);
            if (useSourceMetadata && (int)allSpectra.shape[0] != trainingPool.Samples.Count)
                throw new InvalidDataException(
                    $"Training spectrum load failed for {op}: expected={trainingPool.Samples.Count}, loaded={(int)allSpectra.shape[0]}.");

            if (allSpectra.shape[0] == 0 || ComponentIndex == null)
            {
                Log("エラー: 端成分情報が見つかりません。");
                TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=skipped totalMs={modelTimer.ElapsedMilliseconds}");
                return new TrainingModelMetrics(
                    Target: "regression",
                    ModelFolderName: trainingPool.MineralName + ModelArtifactPaths.RegressionFolderSuffix,
                    Status: "skipped",
                    MineralName: trainingPool.MineralName,
                    SkipReason: "No regression labels were loaded.",
                    SampleCount: trainingPool.Samples.Count,
                    TrainingSampleCount: null,
                    ValidationSampleCount: null,
                    ClassCount: null,
                    ComponentCount: componentIdx?.Count,
                    RequestedEpochs: null,
                    CompletedEpochs: null,
                    BestEpoch: null,
                    ValidationLossName: null,
                    ValidationLoss: null,
                    ValidationAccuracy: null,
                    ValidationMae: null,
                    SplitMethod: "not-applicable");
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
            // 260907Codex: Composition/time planning is independent from train/validation splitting; retain the established seed-42 non-stratified split for every data source.
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitRegression(
                allSpectra,
                allLabels,
                testSize: testSplit,
                randomState: DeepLearningDataSplitter.DefaultRandomState);
            string splitMethod = LegacySplitMethod;
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

            // 260622Claude: 検証に test を渡しているので graph が best epoch で測った test loss/MAE をそのまま使い、冗長な model.evaluate を省く。
            double validationLoss = fitResult.BestValidationLoss;
            double validationMae = fitResult.BestValidationMetric;
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} testLoss={FormatMetric(validationLoss)} testMae={FormatMetric(validationMae)} trainedEpochs={fitResult.CompletedEpochs} source=graph-bestval");

            Log($" 評価完了");
            Log($"  検証 Loss (MSE): {validationLoss:F6}");

            if (validationMae > 0)
            {
                Log($"  検証 MAE: {validationMae:F6}\n");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);

            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("save-start", $"op={op}");
            model.save(outputPath);
            TensorFlowTrainingDebugLog.Write("save-end", $"op={op} durationMs={saveTimer.ElapsedMilliseconds}");
            cancellationToken.ThrowIfCancellationRequested();

            string componentPath = Path.Combine(outputPath, ModelArtifactPaths.ComponentIndexFileName);
            File.WriteAllText(componentPath, System.Text.Json.JsonSerializer.Serialize(componentIdx));

            string modelTypePath = Path.Combine(outputPath, ModelArtifactPaths.ModelTypeFileName);
            File.WriteAllText(modelTypePath, "regression");

            string mineralNamePath = Path.Combine(outputPath, ModelArtifactPaths.MineralNameFileName);
            File.WriteAllText(mineralNamePath, trainingPool.MineralName);

            // 260626Claude: モデルが自分の前処理を持ち歩くよう、保存物の隣に preprocessing.json を残す。予測時に自動適用される。
            preprocessing.WriteToModelFolder(outputPath);

            Log($" モデル保存完了: {outputPath}\n");
            TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=ok totalMs={modelTimer.ElapsedMilliseconds} trainedEpochs={fitResult.CompletedEpochs} testLoss={FormatMetric(validationLoss)} testMae={FormatMetric(validationMae)}");
            return new TrainingModelMetrics(
                Target: "regression",
                ModelFolderName: trainingPool.MineralName + ModelArtifactPaths.RegressionFolderSuffix,
                Status: "trained",
                MineralName: trainingPool.MineralName,
                SkipReason: null,
                SampleCount: (int)allSpectra.shape[0],
                TrainingSampleCount: (int)xTrain.shape[0],
                ValidationSampleCount: (int)xTest.shape[0],
                ClassCount: null,
                ComponentCount: ComponentIndex.Count,
                RequestedEpochs: fitResult.RequestedEpochs,
                CompletedEpochs: fitResult.CompletedEpochs,
                BestEpoch: fitResult.BestEpoch,
                ValidationLossName: "meanSquaredError",
                ValidationLoss: validationLoss,
                ValidationAccuracy: null,
                ValidationMae: validationMae,
                SplitMethod: splitMethod);
        }

        // 260430Codex: 分類学習側も tuple 展開と共通評価 helper で回帰学習と読み方をそろえます。
        // 260507Codex: 新方式の分類学習は manifest 由来の pool だけを読み込みます。
        // 260514Codex: 分類学習でもデータ読み込み、fit、評価、保存の境目でキャンセルを確認します。
        // 260605Claude: 回帰側と同様にステージ計測ログを追加し、データ読み込み/fit/evaluate/save のどこが支配的か特定できるようにする。
        private DeepLearningTrainingResult TrainClassificationModel(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            double unknownDistanceScale,
            string outputPath,
            SpectrumDataLoader.NormalizedSpectrumCache? spectrumCache,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken)
        {
            const string op = "classification:AllMinerals";
            var modelTimer = Stopwatch.StartNew();
            int totalSamples = trainingPools.Sum(p => p.Samples.Count);
            TensorFlowTrainingDebugLog.Write("model-train-start", $"op={op} epochs={epochs} batchSize={batchSize} patience={patience} pools={trainingPools.Count} samples={totalSamples}");

            Log($"\n 訓練データ読み込み中");

            // 260626Claude: 分類学習データにも同じ低エネルギーマスクを適用し、preprocessing.json に保存する。
            var preprocessing = SpectrumPreprocessing.ForTraining();
            TensorFlowTrainingDebugLog.Write("preprocessing", $"op={op} {preprocessing.Describe()}");
            var dataTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("data-load-start", $"op={op}");
            var (allSpectra, allLabelsList, loadStats, loadFailures) = SpectrumDataLoader.LoadClassificationData(trainingPools, cancellationToken, spectrumCache, preprocessing);
            // 260607Codex: Log bounded parallel classification load stats so data-load speedups can be compared safely.
            TensorFlowTrainingDebugLog.Write("data-load-end", $"op={op} spectra={allSpectra.shape[0]} durationMs={dataTimer.ElapsedMilliseconds} inputSamples={loadStats.InputSamples} loadedSamples={loadStats.LoadedSamples} skippedSamples={loadStats.SkippedSamples} parallelDegree={loadStats.ParallelDegree}{FormatSpectrumCacheStats(spectrumCache)}");
            if (loadFailures.Count > 0)
                return ReportClassificationLoadFailures(
                    loadFailures,
                    loadStats,
                    op,
                    modelTimer,
                    cancellationToken);

            // 260901Codex: Source rows stay aligned with loaded spectra because a multi-time run accepts no skipped input.
            var classificationSamples = trainingPools
                .SelectMany(pool => pool.Samples.Select(sample => (pool.MineralName, Sample: sample)))
                .ToArray();
            bool useSourceMetadata = ResolveSourceMetadataMode(
                classificationSamples.Select(item => item.Sample).ToArray(),
                op);
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
            // 260907Codex: Keep the split rule separate from the planner; condition coverage is guaranteed by the completed plan, not by a new splitter policy.
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(
                allSpectra,
                labelsEncoded,
                testSize: testSplit,
                randomState: DeepLearningDataSplitter.DefaultRandomState);
            string splitMethod = LegacySplitMethod;
            TensorFlowTrainingDebugLog.Write("split-end", $"op={op} train={xTrain.shape[0]} test={xTest.shape[0]} durationMs={splitTimer.ElapsedMilliseconds}");
            Log($"訓練データ: {xTrain.shape[0]}");
            Log($"テストデータ: {xTest.shape[0]}\n");

            // 260902Codex: 全ファイル成功後にだけ TensorFlow の状態を初期化し、モデル構築へ進みます。
            TensorFlowTrainingDebugLog.Write("clear-session-before", $"op={op}");
            keras.backend.clear_session();
            TensorFlowTrainingDebugLog.Write("clear-session-after", $"op={op}");
            tf.set_random_seed(42);

            var buildTimer = Stopwatch.StartNew();
            var model = CreateClassificationModel(encoder.Count);

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: CreateSparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );
            TensorFlowTrainingDebugLog.Write("build-compile-end", $"op={op} durationMs={buildTimer.ElapsedMilliseconds}");

            Log("訓練中...");
            var fitTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("fit-start", $"op={op}");
            // 260930Codex: Derive alternatives from manifest composition and reuse the exact original split indices.
            var policyClasses = encoder.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            var alternatives = classificationSamples.Select(row => MineralLabelPolicy.AlternativeIndex(row.MineralName, row.Sample.EndmemberFractions, policyClasses)).ToArray();
            var (trainIndices, testIndices) = DeepLearningDataSplitter.CreateLegacySplitIndices(alternatives.Length, testSplit, DeepLearningDataSplitter.DefaultRandomState);
            var fitResult = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, patience, op, _logAction, reportEpoch, cancellationToken,
                trainIndices.Select(index => alternatives[index]).ToArray(), testIndices.Select(index => alternatives[index]).ToArray());
            TensorFlowTrainingDebugLog.Write("fit-end", $"op={op} durationMs={fitTimer.ElapsedMilliseconds} requestedEpochs={fitResult.RequestedEpochs} trainedEpochs={fitResult.CompletedEpochs} lastEpoch={fitResult.LastEpoch} earlyStoppingMonitor={EarlyStoppingMonitor} patience={patience} finalEpochMetrics={TensorFlowTrainingDebugLog.Clean(fitResult.LastEpochMetrics)}");
            // 260606Codex: Show the actual epoch count beside the EarlyStopping settings used for this fit.
            Log($"  学習エポック数: {fitResult.CompletedEpochs}/{fitResult.RequestedEpochs} (monitor={EarlyStoppingMonitor}, patience={patience})");
            // 260622Claude: 検証に test を渡しているので graph が best epoch で測った test loss/accuracy をそのまま使い、冗長な model.evaluate を省く。
            double validationLoss = fitResult.BestValidationLoss;
            double validationAccuracy = fitResult.BestValidationMetric;
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} testLoss={FormatMetric(validationLoss)} testAccuracy={FormatMetric(validationAccuracy)} trainedEpochs={fitResult.CompletedEpochs} source=graph-bestval");

            Log($"検証 Loss: {validationLoss:F4}");
            Log($"検証 Accuracy: {validationAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, ModelArtifactPaths.LabelEncoderFileName);
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, ModelArtifactPaths.ModelTypeFileName);
            File.WriteAllText(modelTypePath, "classification");
            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("save-start", $"op={op}");
            model.save(outputPath);
            TensorFlowTrainingDebugLog.Write("save-end", $"op={op} durationMs={saveTimer.ElapsedMilliseconds}");
            cancellationToken.ThrowIfCancellationRequested();
            // 260626Claude: 予測時に自動適用するため、分類モデルフォルダにも前処理を記録する。
            preprocessing.WriteToModelFolder(outputPath);
            // 260930Codex: Mark models trained with the dual-label rule; missing metadata means legacy training.
            MineralLabelPolicy.WriteTrainingMetadata(outputPath);
            // 260622Codex: Persist optional open-set statistics after the classifier itself is safely saved.
            TrySaveUnknownDetector(model, xTrain, yTrain, xTest, yTest, unknownDistanceScale, outputPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=ok totalMs={modelTimer.ElapsedMilliseconds} trainedEpochs={fitResult.CompletedEpochs} testLoss={FormatMetric(validationLoss)} testAccuracy={FormatMetric(validationAccuracy)}");
            return DeepLearningTrainingResult.CreateCompleted(new[]
            {
                new TrainingModelMetrics(
                    Target: "classification",
                    ModelFolderName: ModelArtifactPaths.ClassificationFolderName,
                    Status: "trained",
                    MineralName: null,
                    SkipReason: null,
                    SampleCount: (int)allSpectra.shape[0],
                    TrainingSampleCount: (int)xTrain.shape[0],
                    ValidationSampleCount: (int)xTest.shape[0],
                    ClassCount: encoder.Count,
                    ComponentCount: null,
                    RequestedEpochs: fitResult.RequestedEpochs,
                    CompletedEpochs: fitResult.CompletedEpochs,
                    BestEpoch: fitResult.BestEpoch,
                    ValidationLossName: "partialLabelCrossentropy",
                    ValidationLoss: validationLoss,
                    ValidationAccuracy: validationAccuracy,
                    ValidationMae: null,
                    SplitMethod: splitMethod,
                    LegacyValidationAccuracy: fitResult.LegacyValidationAccuracy)
            });
        }

        // 260902Codex: 先頭10件を画面ログへ、修復に必要な全件を診断ログへ入力順で残します。
        private DeepLearningTrainingResult ReportClassificationLoadFailures(
            IReadOnlyList<SpectrumDataLoader.ClassificationLoadFailure> failures,
            SpectrumDataLoader.ClassificationLoadStats stats,
            string operationName,
            Stopwatch modelTimer,
            CancellationToken cancellationToken)
        {
            if (failures.Count != stats.SkippedSamples)
                throw new InvalidOperationException(
                    $"分類データの失敗件数が一致しません: skipped={stats.SkippedSamples}, failures={failures.Count}");

            bool allFailuresLogged = true;
            foreach (var failure in failures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TensorFlowTrainingDebugLog.TryWrite(
                        "classification-data-load-failure",
                        $"op={operationName} kind={failure.Kind} path={TensorFlowTrainingDebugLog.Clean(failure.FilePath)} "
                        + $"actualPoints={failure.ActualPointCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty} "
                        + $"exceptionType={failure.ExceptionType ?? string.Empty} "
                        + $"exception={TensorFlowTrainingDebugLog.Clean(failure.ExceptionMessage ?? string.Empty)}"))
                    allFailuresLogged = false;
            }

            Log($"\n学習データ読込失敗: {failures.Count}件");
            int displayCount = Math.Min(failures.Count, 10);
            for (int i = 0; i < displayCount; i++)
            {
                var failure = failures[i];
                Log($"{i + 1}. {DescribeClassificationLoadFailure(failure)}");
                Log($"   {failure.FilePath}");
            }

            if (failures.Count > displayCount)
                Log($"ほか{failures.Count - displayCount}件");
            Log(allFailuresLogged
                ? "全件は tf-train-debug.log に記録しました。"
                : "tf-train-debug.log への全件記録に失敗しました。");

            TensorFlowTrainingDebugLog.Write(
                "model-train-end",
                $"op={operationName} status=not-completed reason=training-data-load-failed "
                + $"inputSamples={stats.InputSamples} loadedSamples={stats.LoadedSamples} failedSamples={failures.Count} "
                + $"totalMs={modelTimer.ElapsedMilliseconds}");

            // 260903Codex: 画面上の「モデル作成開始」と矛盾しないよう、実際に止めた段階を示します。
            string failureReason = $"学習データを{failures.Count}件読み込めなかったため、モデルの学習へ進みませんでした。";
            if (!allFailuresLogged)
                failureReason += " 診断ログへの全件記録にも失敗しました。";
            cancellationToken.ThrowIfCancellationRequested();
            return DeepLearningTrainingResult.CreateNotCompleted(
                DeepLearningTrainingFailureKind.TrainingDataLoadFailed,
                failureReason);
        }

        private static string DescribeClassificationLoadFailure(
            SpectrumDataLoader.ClassificationLoadFailure failure) =>
            failure.Kind switch
            {
                SpectrumDataLoader.ClassificationLoadFailureKind.PointCountMismatch =>
                    $"点数不一致（{failure.ActualPointCount} / {SpectrumLength}点）",
                SpectrumDataLoader.ClassificationLoadFailureKind.EdsLayoutMismatch =>
                    "EDSレイアウト不一致",
                SpectrumDataLoader.ClassificationLoadFailureKind.ReadFailure =>
                    $"読み取り失敗（{failure.ExceptionType ?? "原因不明"}）",
                _ => throw new InvalidOperationException($"未対応の分類データ読込失敗です: {failure.Kind}")
            };

        // 260901Codex: Mixed legacy and manifest-aware rows are rejected because their split semantics would be ambiguous.
        private static bool ResolveSourceMetadataMode(
            IReadOnlyList<SpectrumTrainingSample> samples,
            string operationName)
        {
            int sourceCount = samples.Count(sample => sample.Source is not null);
            if (sourceCount == 0)
                return false;
            if (sourceCount != samples.Count)
                throw new InvalidDataException(
                    $"Training source metadata is incomplete for {operationName}: expected={samples.Count}, actual={sourceCount}.");

            foreach (var sample in samples)
            {
                var source = sample.Source!;
                if (!double.IsFinite(source.LiveTime) || source.LiveTime <= 0
                    || string.IsNullOrWhiteSpace(source.ConditionKey)
                    || source.SimulationId < 0
                    || string.IsNullOrWhiteSpace(source.ManifestPath))
                    throw new InvalidDataException($"Training source metadata is invalid for {operationName}: {sample.FilePath}");
            }

            return true;
        }

        // 260622Codex: Build the optional open-set detector beside the classifier without making classifier training depend on it.
        private void TrySaveUnknownDetector(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            NDArray xTest,
            NDArray yTest,
            double unknownDistanceScale,
            string outputPath,
            CancellationToken cancellationToken)
        {
            try
            {
                TensorFlowTrainingDebugLog.Write("unknown-detector-start", $"path={TensorFlowTrainingDebugLog.Clean(outputPath)}");
                var detector = MineralUnknownDetector.Build(model, xTrain, yTrain, xTest, yTest, unknownDistanceScale, cancellationToken);
                detector.Save(outputPath);
                TensorFlowTrainingDebugLog.Write(
                    "unknown-detector-end",
                    $"path={TensorFlowTrainingDebugLog.Clean(outputPath)} labels={detector.LabelCount} dim={detector.EmbeddingDim} threshold={FormatMetric(detector.Threshold)} q={FormatMetric(detector.ThresholdQuantile)} radiusExpansion={FormatMetric(detector.ThresholdRadiusExpansion)} ridge={FormatMetric(detector.Ridge)}");
                Log($"Unknown detector: threshold={detector.Threshold:F4} (known max + radius x{detector.ThresholdRadiusExpansion:F2})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TensorFlowTrainingDebugLog.Write(
                    "unknown-detector-skip",
                    $"path={TensorFlowTrainingDebugLog.Clean(outputPath)} error={TensorFlowTrainingDebugLog.Clean(ex.Message)}");
                Log($"Unknown detector skipped: {ex.Message}");
            }
        }

        #endregion
        // 260727Claude: 旧予測経路 (#region 学習済みモデルを利用して予測 = RunPrediction / LogRegressionResult /
        //   GenerateFormula) を削除。6ba8adc "Add batch spectrum predictions" (260621) で FormMain の呼び出しが
        //   SpectrumBatchPredictionWorkflow へ差し替わって以降、唯一の呼び出し元だった MineralPredictionWorkflow.RunAsync
        //   ごと到達不能になっていた。判定の正本は SpectrumBatchPredictionWorkflow + SpectrumPredictionBlockFormatter。
        //   このクラスは学習専用になった。
    }
}

