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
        // 260622Codex: eager logits 検証時だけ最終層を生 logits にし、通常保存モデルは従来どおり softmax 出力に保つ。
        private static Model CreateClassificationModel(int numClasses, bool outputLogits = false)
        {
            var layers = new List<ILayer>
            {
                keras.layers.Dense(128, activation: "relu", input_shape: new Shape(SpectrumLength)),
                keras.layers.Dense(64, activation: "relu")
            };
            layers.Add(outputLogits
                ? keras.layers.Dense(numClasses)
                : keras.layers.Dense(numClasses, activation: "softmax"));
            return keras.Sequential(layers);
        }

        // 260622Codex: logits 実験で保存モデルの推論互換性を壊さないよう、学習後の重みを softmax モデルへ移す。
        private static Model ConvertClassificationLogitsModelToSoftmax(Model logitsModel, int numClasses)
        {
            var softmaxModel = CreateClassificationModel(numClasses);
            softmaxModel.build(new Shape(-1, SpectrumLength));
            softmaxModel.set_weights(logitsModel.get_weights());
            softmaxModel.compile(
                optimizer: keras.optimizers.Adam(),
                loss: CreateSparseCategoricalCrossentropy(fromLogits: false),
                metrics: new[] { "accuracy" });
            return softmaxModel;
        }

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

        // 260605Codex: Patience is intended to watch validation/test loss rather than training loss.
        private const string EarlyStoppingMonitor = "val_loss";

        // 260622Codex: Read A/B env and marker toggles uniformly; explicit OFF wins over marker ON.
        private static bool IsExperimentFlagEnabled(string environmentVariable, string markerFileName)
        {
            string? value = Environment.GetEnvironmentVariable(environmentVariable);
            if (IsEnabledFlag(value))
                return true;

            if (IsDisabledFlag(value))
                return false;

            string offMarkerFileName = markerFileName.EndsWith(".on", StringComparison.OrdinalIgnoreCase)
                ? markerFileName[..^3] + ".off"
                : markerFileName + ".off";
            if (File.Exists(GetExperimentMarkerPath(offMarkerFileName)))
                return false;

            return File.Exists(GetExperimentMarkerPath(markerFileName));
        }

        private static bool IsEnabledFlag(string? value) =>
            string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

        private static bool IsDisabledFlag(string? value) =>
            string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);

        private static string GetExperimentMarkerPath(string fileName) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MineraScope",
                fileName);

        private static ILossFunc CreateSparseCategoricalCrossentropy(bool fromLogits) =>
            keras.losses.SparseCategoricalCrossentropy(from_logits: fromLogits);

        // 260612Claude: 分類・回帰とも自前 GradientTape ループで学習する(fit の毎batch強制 GC.Collect を撤去するのが目的)。
        //              fit/custom の等価性(bit-identical)と連続run leak を検証済みのため、旧 fit へ戻す安全弁トグルは撤去した。
        //              ★Dense-only 前提: Dropout/BatchNorm/regularizer/non-trainable が無いので Apply(training:true/false) は同一。層を足したらこの前提を見直す。
        //              native handle を bound するため毎batchではなく N batch ごとにだけ GC する(fit の毎batch GC の代替)。
        private const int CustomLoopGcInterval = 32;

        // 260605Codex: Return the observed fit result because TensorFlow.Keras History is not reliable enough here.
        private sealed record TrainingFitResult(int RequestedEpochs, int CompletedEpochs, int LastEpoch, string LastEpochMetrics);

        // 260621Codex: TensorFlow.NET の native memory を見ながら custom loop の GC 頻度を安全側/高速側へ寄せます。
        private sealed class AdaptiveTrainingGcController : IDisposable
        {
            private const int ProbeInterval = 16;
            private const int MinInterval = 16;
            private const int MaxInterval = 128;
            private const double PressureMemoryLoadRatio = 0.85;
            private const double StableMemoryLoadRatio = 0.65;
            private const long PressurePrivateGrowthMb = 512;
            private const long StablePrivateGrowthMb = 128;

            private readonly string _operation;
            private readonly Process _process;
            private readonly bool _adaptive;
            private int _interval;
            private long _lastGcPrivateMb;
            private long _nextGcStep;

            public AdaptiveTrainingGcController(string operationName)
            {
                _operation = TensorFlowTrainingDebugLog.Clean(operationName);
                _process = Process.GetCurrentProcess();

                int fixedInterval = ReadIntEnv("MINERASCOPE_CUSTOMLOOP_GC_INTERVAL", 0);
                bool adaptiveDisabled = IsDisabled(Environment.GetEnvironmentVariable("MINERASCOPE_ADAPTIVE_GC"));
                _adaptive = fixedInterval <= 0 && !adaptiveDisabled;
                _interval = fixedInterval > 0
                    ? fixedInterval
                    : _adaptive ? ResolveInitialInterval() : CustomLoopGcInterval;
                _nextGcStep = Math.Max(1, _interval);

                var snapshot = CaptureMemorySnapshot();
                _lastGcPrivateMb = snapshot.PrivateMb;
                TensorFlowTrainingDebugLog.Write(
                    "adaptive-gc-start",
                    $"operation={_operation} mode={Mode} interval={_interval} probeInterval={ProbeInterval} minInterval={MinInterval} maxInterval={MaxInterval} privateMB={snapshot.PrivateMb} memoryLoad={FormatMetric(snapshot.MemoryLoadRatio)} totalAvailableMB={snapshot.TotalAvailableMb}");
            }

            public string Mode => _adaptive ? "adaptive" : "fixed";

            public int CurrentInterval => _interval;

            public void AfterBatch(long globalStep)
            {
                if (_interval <= 0)
                    return;

                if (!_adaptive)
                {
                    if (globalStep % _interval == 0)
                        Collect(globalStep, "fixed", CaptureMemorySnapshot());
                    return;
                }

                bool scheduled = globalStep >= _nextGcStep;
                if (!scheduled && globalStep % ProbeInterval != 0)
                    return;

                var snapshot = CaptureMemorySnapshot();
                long privateGrowthMb = Math.Max(0, snapshot.PrivateMb - _lastGcPrivateMb);
                bool pressure = snapshot.MemoryLoadRatio >= PressureMemoryLoadRatio || privateGrowthMb >= PressurePrivateGrowthMb;
                if (!scheduled && !pressure)
                    return;

                string reason = "scheduled";
                if (pressure)
                {
                    _interval = Math.Max(MinInterval, Math.Max(1, _interval / 2));
                    reason = "pressure";
                }
                else if (snapshot.MemoryLoadRatio <= StableMemoryLoadRatio && privateGrowthMb <= StablePrivateGrowthMb)
                {
                    int widenedInterval = Math.Min(MaxInterval, _interval * 2);
                    if (widenedInterval != _interval)
                    {
                        _interval = widenedInterval;
                        reason = "stable";
                    }
                }

                Collect(globalStep, reason, snapshot);
            }

            public void Dispose() => _process.Dispose();

            private void Collect(long globalStep, string reason, MemorySnapshot before)
            {
                var pauseBefore = GC.GetTotalPauseDuration();
                GC.Collect();
                long pauseDeltaMs = (long)(GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
                var after = CaptureMemorySnapshot();
                long privateGrowthMb = Math.Max(0, before.PrivateMb - _lastGcPrivateMb);
                _lastGcPrivateMb = after.PrivateMb;
                _nextGcStep = globalStep + Math.Max(1, _interval);

                TensorFlowTrainingDebugLog.Write(
                    "adaptive-gc",
                    $"operation={_operation} mode={Mode} step={globalStep} reason={reason} interval={_interval} nextStep={_nextGcStep} privateBeforeMB={before.PrivateMb} privateAfterMB={after.PrivateMb} privateGrowthMB={privateGrowthMb} memoryLoad={FormatMetric(before.MemoryLoadRatio)} gcPauseDeltaMs={pauseDeltaMs}");
            }

            private MemorySnapshot CaptureMemorySnapshot()
            {
                _process.Refresh();
                var info = GC.GetGCMemoryInfo();
                long highThresholdBytes = info.HighMemoryLoadThresholdBytes;
                double memoryLoadRatio = highThresholdBytes > 0
                    ? info.MemoryLoadBytes / (double)highThresholdBytes
                    : 0d;

                return new MemorySnapshot(
                    _process.PrivateMemorySize64 / (1024 * 1024),
                    memoryLoadRatio,
                    info.TotalAvailableMemoryBytes / (1024 * 1024));
            }

            private static int ResolveInitialInterval()
            {
                var info = GC.GetGCMemoryInfo();
                double memoryLoadRatio = info.HighMemoryLoadThresholdBytes > 0
                    ? info.MemoryLoadBytes / (double)info.HighMemoryLoadThresholdBytes
                    : 0d;
                long totalAvailableMb = info.TotalAvailableMemoryBytes / (1024 * 1024);

                if (memoryLoadRatio >= 0.80)
                    return MinInterval;

                if (totalAvailableMb >= 24 * 1024)
                    return MaxInterval;

                if (totalAvailableMb >= 12 * 1024)
                    return 64;

                return CustomLoopGcInterval;
            }

            private static bool IsDisabled(string? value) =>
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || value == "0";

            private readonly record struct MemorySnapshot(long PrivateMb, double MemoryLoadRatio, long TotalAvailableMb);
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
            bool classificationFromLogits = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationName.StartsWith("regression", StringComparison.Ordinal))
                return RunCustomRegressionLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken);
            // 260621Claude: 分類は env で v1 Graph/Session 経路(高速化の本命)へ切替可能。既定は従来 eager custom loop(検証付き同等ゲートを通すまで本番不変)。
            if (IsGraphClassificationEnabled())
                return RunGraphClassificationLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken);
            return RunCustomClassificationLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken, classificationFromLogits);
        }

        // 260621Claude: 分類の graph 学習経路を有効化する(既定OFF)。実データで eager と A/B して同等性ゲートを通すまで本番挙動を変えないため。
        // 260622Claude: env が起動方法(VS デバッグ/Release 直起動)で渡らないため、マーカーファイル `Documents\MineraScope\graph_classification.on` でも有効化できるようにする。A/B 計測用で、合格後に既定化する際は撤去する。
        private static bool IsGraphClassificationEnabled()
            => IsExperimentFlagEnabled("MINERASCOPE_GRAPH_CLASSIFICATION", "graph_classification.on");

        // 260622Codex: eager の数値経路だけを stable logits に替えて、graph との精度差の原因を切り分ける実験用。
        // 260622Codex: Experimentally switch only eager classification to the stable logits loss path.
        private static bool IsEagerLogitsClassificationEnabled()
            => IsExperimentFlagEnabled("MINERASCOPE_EAGER_LOGITS_CLASSIFICATION", "eager_logits_classification.on");

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
            CancellationToken cancellationToken,
            bool fromLogits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string op = TensorFlowTrainingDebugLog.Clean(operationName);
            using var gcController = new AdaptiveTrainingGcController(operationName);
            TensorFlowTrainingDebugLog.Write("custom-loop-start", $"operation={op} fromLogits={fromLogits} gcMode={gcController.Mode} gcInterval={gcController.CurrentInterval}");

            var lossFn = CreateSparseCategoricalCrossentropy(fromLogits);
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
            // 260621Claude: train metric (loss/accuracy) はログ用なので毎epochではなく N epoch ごと + 最終/early-stop epoch だけ評価する。
            int trainMetricInterval = ReadIntEnv("MINERASCOPE_TRAIN_METRIC_INTERVAL", 10);

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                epochStopwatch.Restart();
                TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={op} epoch={epoch} customLoop=1");

                // 260621Claude: batch loop は学習だけに絞り、train metric の scalar 同期(numpy)を毎batchで起こさない。
                long batchLoopStart = Stopwatch.GetTimestamp();
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

                    DisposeTensors(xb, yb, predT, lossT);
                    foreach (var g in grads)
                        g?.Dispose();

                    globalStep++;
                    // 260609Claude: fit の毎batch GC.Collect の代わりに N batch ごとにだけ回収して native handle を bound する。
                    gcController.AfterBatch(globalStep);
                }
                double batchLoopMs = Stopwatch.GetElapsedTime(batchLoopStart).TotalMilliseconds;

                // 260609Claude: EarlyStopping(monitor=val_loss) のため val_loss は毎epoch評価する。
                long valStart = Stopwatch.GetTimestamp();
                var (valLoss, valAccuracy) = EvaluateClassification(model, xVal, yVal, valCount, lossFn);
                double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                // 260609Claude: EarlyStopping(monitor=val_loss, min_delta=0, restore_best_weights) を自前で再現。
                bool willStop = false;
                if (valLoss < bestValLoss)
                {
                    bestValLoss = valLoss;
                    bestWeights = model.get_weights();
                    wait = 0;
                }
                else if (++wait >= patience)
                    willStop = true;

                // 260621Claude: train metric は N epoch ごと + 最終 epoch + early-stop epoch だけ full train forward で評価する。
                bool evalTrainMetric = epoch % trainMetricInterval == 0 || epoch == epochs - 1 || willStop;
                double trainEvalMs = 0d;
                string logs;
                if (evalTrainMetric)
                {
                    long trainEvalStart = Stopwatch.GetTimestamp();
                    var (trainLoss, trainAccuracy) = EvaluateClassification(model, xAll, yAll, sampleCount, lossFn);
                    trainEvalMs = Stopwatch.GetElapsedTime(trainEvalStart).TotalMilliseconds;
                    logs = $"loss={FormatMetric(trainLoss)},accuracy={FormatMetric(trainAccuracy)},val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},trainMetricMode=epoch_end_interval";
                }
                else
                    logs = $"val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},trainMetricSkipped=1,trainMetricMode=skipped";

                completedEpochs++;
                lastEpoch = epoch;
                lastEpochMetrics = logs;
                TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} trainEvalMs={FormatMetric(trainEvalMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
                logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                if (willStop)
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
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string op = TensorFlowTrainingDebugLog.Clean(operationName);
            int trainMetricInterval = ReadIntEnv("MINERASCOPE_TRAIN_METRIC_INTERVAL", 10);

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
                var trainLoss = tf.reduce_mean(tf.nn.sparse_softmax_cross_entropy_with_logits(tf.gather(dataY, idx), Forward(tf.gather(dataX, idx))));
                // 260621Claude: Keras Adam と同一の hyperparams(epsilon=1e-7)で minimize し、研究比較の同等性を保つ。
                var optimizer = new Tensorflow.Train.AdamOptimizer(0.001f, 0.9f, 0.999f, 1e-7f, false, TF_DataType.TF_FLOAT, "Adam");
                var trainOp = optimizer.minimize(trainLoss);

                var valLogits = Forward(valX);
                var valLossOp = tf.reduce_mean(tf.nn.sparse_softmax_cross_entropy_with_logits(valY, valLogits));
                var valCorrectOp = tf.reduce_sum(tf.cast(tf.equal(tf.arg_max(valLogits, 1), tf.cast(valY, tf.int64)), tf.int32));

                var trainLogitsFull = Forward(dataX);
                var trainLossFullOp = tf.reduce_mean(tf.nn.sparse_softmax_cross_entropy_with_logits(dataY, trainLogitsFull));
                var trainCorrectOp = tf.reduce_sum(tf.cast(tf.equal(tf.arg_max(trainLogitsFull, 1), tf.cast(dataY, tf.int64)), tf.int32));

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
                    double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                    bool willStop = false;
                    if (valLoss < bestValLoss)
                    {
                        bestValLoss = valLoss;
                        bestWeights = weightVars.Select(v => sess.run(v.AsTensor())).ToList();
                        wait = 0;
                    }
                    else if (++wait >= patience)
                        willStop = true;

                    bool evalTrainMetric = epoch % trainMetricInterval == 0 || epoch == epochs - 1 || willStop;
                    double trainEvalMs = 0d;
                    string logs;
                    if (evalTrainMetric)
                    {
                        long trainEvalStart = Stopwatch.GetTimestamp();
                        double trainLossFull = sess.run(trainLossFullOp).ToArray<float>()[0];
                        double trainAccuracy = sampleCount > 0 ? sess.run(trainCorrectOp).ToArray<int>()[0] / (double)sampleCount : 0d;
                        trainEvalMs = Stopwatch.GetElapsedTime(trainEvalStart).TotalMilliseconds;
                        logs = $"loss={FormatMetric(trainLossFull)},accuracy={FormatMetric(trainAccuracy)},val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},trainMetricMode=epoch_end_interval";
                    }
                    else
                        logs = $"val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},trainMetricSkipped=1,trainMetricMode=skipped";

                    completedEpochs++;
                    lastEpoch = epoch;
                    lastEpochMetrics = logs;
                    TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} engine=graph durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} trainEvalMs={FormatMetric(trainEvalMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
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

            // 260621Claude: best weights を eager の Keras model に書き戻し、呼び出し側の evaluate/save をそのまま使う。
            if (bestWeights != null)
                model.set_weights(bestWeights);
            TensorFlowTrainingDebugLog.Write("graph-loop-end", $"operation={op} engine=graph trainedEpochs={completedEpochs} lastEpoch={lastEpoch} bestValLoss={FormatMetric(bestValLoss)}");
            return new TrainingFitResult(epochs, completedEpochs, lastEpoch, lastEpochMetrics);
        }

        // 260611Codex: 回帰用 custom training loop。MSE の勾配更新を fit と同じ Adam で回し、MAE はログ用に batch prediction から集計します。
        private static TrainingFitResult RunCustomRegressionLoop(
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
            using var gcController = new AdaptiveTrainingGcController(operationName);
            TensorFlowTrainingDebugLog.Write("custom-loop-start", $"operation={op} kind=regression gcMode={gcController.Mode} gcInterval={gcController.CurrentInterval}");

            var lossFn = keras.losses.MeanSquaredError();
            var optimizer = keras.optimizers.Adam();

            var xAll = tf.constant(xTrain);
            var yAll = tf.constant(yTrain);
            var xVal = tf.constant(xValidation);
            var yVal = tf.constant(yValidation);
            int sampleCount = (int)xTrain.shape[0];
            int features = (int)xTrain.shape[1];
            int outputCount = (int)yTrain.shape[1];
            int valCount = (int)xValidation.shape[0];

            List<NDArray>? bestWeights = null;
            double bestValLoss = double.PositiveInfinity;
            int wait = 0;
            int completedEpochs = 0;
            int lastEpoch = -1;
            string lastEpochMetrics = "";
            long globalStep = 0;
            var epochStopwatch = new Stopwatch();
            // 260621Claude: train metric (loss/MAE) はログ用なので毎epochではなく N epoch ごと + 最終/early-stop epoch だけ評価する。
            int trainMetricInterval = ReadIntEnv("MINERASCOPE_TRAIN_METRIC_INTERVAL", 10);

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                epochStopwatch.Restart();
                TensorFlowTrainingDebugLog.Write("epoch-begin", $"operation={op} epoch={epoch} customLoop=1 kind=regression");

                // 260621Claude: batch loop は学習だけに絞り、train metric の scalar 同期(numpy)を毎batchで起こさない。
                long batchLoopStart = Stopwatch.GetTimestamp();
                for (int start = 0; start < sampleCount; start += batchSize)
                {
                    int count = Math.Min(batchSize, sampleCount - start);
                    var xb = tf.slice(xAll, new[] { start, 0 }, new[] { count, features });
                    var yb = tf.slice(yAll, new[] { start, 0 }, new[] { count, outputCount });

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

                    DisposeTensors(xb, yb, predT, lossT);
                    foreach (var g in grads)
                        g?.Dispose();

                    globalStep++;
                    gcController.AfterBatch(globalStep);
                }
                double batchLoopMs = Stopwatch.GetElapsedTime(batchLoopStart).TotalMilliseconds;

                // 260611Codex: EarlyStopping(monitor=val_loss) のため val_loss は毎epoch評価する。
                long valStart = Stopwatch.GetTimestamp();
                var (valLoss, valMae) = EvaluateRegression(model, xVal, yVal, valCount, outputCount, lossFn);
                double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                bool willStop = false;
                if (valLoss < bestValLoss)
                {
                    bestValLoss = valLoss;
                    bestWeights = model.get_weights();
                    wait = 0;
                }
                else if (++wait >= patience)
                    willStop = true;

                // 260621Claude: train metric は N epoch ごと + 最終 epoch + early-stop epoch だけ full train forward で評価する。
                bool evalTrainMetric = epoch % trainMetricInterval == 0 || epoch == epochs - 1 || willStop;
                double trainEvalMs = 0d;
                string logs;
                if (evalTrainMetric)
                {
                    long trainEvalStart = Stopwatch.GetTimestamp();
                    var (trainLoss, trainMae) = EvaluateRegression(model, xAll, yAll, sampleCount, outputCount, lossFn);
                    trainEvalMs = Stopwatch.GetElapsedTime(trainEvalStart).TotalMilliseconds;
                    logs = $"loss={FormatMetric(trainLoss)},mean_absolute_error={FormatMetric(trainMae)},val_loss={FormatMetric(valLoss)},val_mean_absolute_error={FormatMetric(valMae)},trainMetricMode=epoch_end_interval";
                }
                else
                    logs = $"val_loss={FormatMetric(valLoss)},val_mean_absolute_error={FormatMetric(valMae)},trainMetricSkipped=1,trainMetricMode=skipped";

                completedEpochs++;
                lastEpoch = epoch;
                lastEpochMetrics = logs;
                TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} trainEvalMs={FormatMetric(trainEvalMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
                logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                if (willStop)
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
            TensorFlowTrainingDebugLog.Write("custom-loop-end", $"operation={op} kind=regression trainedEpochs={completedEpochs} lastEpoch={lastEpoch} bestValLoss={FormatMetric(bestValLoss)}");
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

        // 260611Codex: 回帰 validation は Keras evaluate を通さず、同じ MSE loss と TensorFlow 側の絶対誤差合計で一括計算します。
        private static (double ValLoss, double ValMae) EvaluateRegression(
            Model model, Tensor xValidation, Tensor yValidation, int total, int outputCount, ILossFunc lossFn)
        {
            Tensor preds = model.Apply(xValidation, training: false);
            Tensor lossT = lossFn.Call(yValidation, preds);
            double valLoss = lossT.numpy().ToArray<float>()[0];

            var errorT = tf.subtract(preds, yValidation);
            var absErrorT = tf.abs(errorT);
            var absErrorSumT = tf.reduce_sum(absErrorT);
            double valMae = total > 0 && outputCount > 0
                ? absErrorSumT.numpy().ToArray<float>()[0] / total / outputCount
                : 0d;

            DisposeTensors(preds, lossT, errorT, absErrorT, absErrorSumT);
            return (valLoss, valMae);
        }

        // 260609Claude: batch-local tensor を明示破棄して native handle を即解放する(N batch GC と併用して leak を抑える)。
        private static void DisposeTensors(params Tensor[] tensors)
        {
            foreach (var t in tensors)
                t?.Dispose();
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
            bool graphClassification = IsGraphClassificationEnabled();
            bool classificationFromLogits = !graphClassification && IsEagerLogitsClassificationEnabled();

            // 260622Codex: Let the smoke path exercise the same eager logits experiment switch as real classification training.
            TensorFlowTrainingDebugLog.Write("smoke-start", $"samples={samples} classes={classes} epochs={epochs} batchSize={batchSize} graphClassification={graphClassification} classificationFromLogits={classificationFromLogits}");
            log($"headless smoke test: samples={samples} classes={classes} epochs={epochs} batch={batchSize}");

            tf.set_random_seed(42);
            var rng = new Random(42);
            float[,] centers = new float[classes, SpectrumLength];
            for (int k = 0; k < classes; k++)
                for (int j = 0; j < SpectrumLength; j++)
                    centers[k, j] = (float)rng.NextDouble();

            var (xTrain, yTrain) = MakeSyntheticClassificationData(samples, classes, centers, rng);
            var (xTest, yTest) = MakeSyntheticClassificationData(valSamples, classes, centers, rng);

            var model = CreateClassificationModel(classes, outputLogits: classificationFromLogits);
            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: CreateSparseCategoricalCrossentropy(classificationFromLogits),
                metrics: new[] { "accuracy" });

            var sw = Stopwatch.StartNew();
            var result = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, 10, "classification:Smoke", log, null, default, classificationFromLogits);
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
            // 260621Codex: run 全体の CPU 使用率を、論理 CPU 総量に対する割合で後から比較できるようにします。
            using var runProcess = Process.GetCurrentProcess();
            TimeSpan runCpuStart = runProcess.TotalProcessorTime;
            int regressionCount = orderedPools.Count(pool => pool.EndmemberNames.Count >= 2);
            bool graphClassification = IsGraphClassificationEnabled();
            bool eagerLogitsClassification = !graphClassification && IsEagerLogitsClassificationEnabled();
            // 260622Claude: graph 分類フラグが実際にプロセスへ届いたかを起動時に残し、A/B で経路を取り違えていないか即判別できるようにする。
            // 260622Codex: Log the effective eager logits flag; graph wins when both experiment toggles are present.
            TensorFlowTrainingDebugLog.Write("training-run-start", $"pools={orderedPools.Length} regressionModels={regressionCount} epochs={epochs} batchSize={batchSize} patience={patience} graphClassification={graphClassification} eagerLogitsClassification={eagerLogitsClassification}");

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
            runProcess.Refresh();
            long totalMs = runTimer.ElapsedMilliseconds;
            long runCpuMs = (long)(runProcess.TotalProcessorTime - runCpuStart).TotalMilliseconds;
            double averageLogicalCpuPercent = totalMs > 0 && Environment.ProcessorCount > 0
                ? runCpuMs * 100d / totalMs / Environment.ProcessorCount
                : 0d;
            TensorFlowTrainingDebugLog.Write("training-run-end", $"totalMs={totalMs} runCpuMs={runCpuMs} avgLogicalCpuPercent={FormatMetric(averageLogicalCpuPercent)} logicalProcessors={Environment.ProcessorCount}");
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
            bool graphClassification = IsGraphClassificationEnabled();
            bool classificationFromLogits = !graphClassification && IsEagerLogitsClassificationEnabled();
            TensorFlowTrainingDebugLog.Write("model-train-start", $"op={op} epochs={epochs} batchSize={batchSize} patience={patience} pools={trainingPools.Count} samples={totalSamples} graphClassification={graphClassification} classificationFromLogits={classificationFromLogits}");

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
            var model = CreateClassificationModel(encoder.Count, outputLogits: classificationFromLogits);

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: CreateSparseCategoricalCrossentropy(classificationFromLogits),
                metrics: new[] { "accuracy" }
            );
            TensorFlowTrainingDebugLog.Write("build-compile-end", $"op={op} durationMs={buildTimer.ElapsedMilliseconds} classificationOutputLogits={classificationFromLogits}");

            Log("訓練中...");
            var fitTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("fit-start", $"op={op}");
            var fitResult = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, patience, op, _logAction, reportEpoch, cancellationToken, classificationFromLogits);
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
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} durationMs={evalTimer.ElapsedMilliseconds} testLoss={FormatMetric(testLoss)} testAccuracy={FormatMetric(testAccuracy)} trainedEpochs={fitResult.CompletedEpochs} classificationFromLogits={classificationFromLogits}");

            Log($"Test loss: {testLoss:F4}");
            Log($"Test accuracy: {testAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, "labelEncoder.json");
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "classification");
            if (classificationFromLogits)
            {
                // 260622Codex: Preserve prediction-service compatibility by saving a softmax-output model with the learned logits weights.
                TensorFlowTrainingDebugLog.Write("classification-logits-save-convert", $"op={op}");
                model = ConvertClassificationLogitsModelToSoftmax(model, encoder.Count);
            }
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

                // 260611Claude: 複数ファイル判定時に各結果ブロックを空行で区切るための先頭判定。
                bool isFirstFile = true;
                foreach (var filePath in files)
                {
                    var spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath);
                    if (spectrum == null)
                        continue;

                    var classification = classificationService.Predict(modelPath, spectrum);

                    // 260611Claude: 予測鉱物名を見出しにし、詳細確率は 0.00% を隠して見やすく並べる (鉱物マップと同じ非表示ルール)。
                    if (!isFirstFile)
                        Log("");
                    isFirstFile = false;

                    string topPercent = (classification.Confidence * 100).ToString("F2", CultureInfo.InvariantCulture);
                    Log($"ファイル: {Path.GetFileName(filePath)}");
                    Log("");
                    Log($"{classification.PredictedMineral} ({topPercent}%)");
                    Log("");
                    Log("[詳細確率]");
                    // 260613Claude: 0.00% (F2 で丸めて 0) の候補は隠す。AnalyzerForm は別形式 (ランク表示) で自前整形のため helper 共有はやめインライン化。
                    foreach (var probability in classification.Probabilities)
                    {
                        string percentText = (probability.Confidence * 100).ToString("F2", CultureInfo.InvariantCulture);
                        if (percentText == "0.00")
                            continue;
                        Log($"  {probability.MineralName}: {percentText}%");
                    }

                    // 260522Codex: 予測鉱物名に対応する回帰モデルだけを成分比率予測の候補にします。
                    string[] candidates = Directory.GetDirectories(modelPath, $"{classification.PredictedMineral}*_Regression");
                    if (candidates.Length == 0)
                        continue;

                    Log("");
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

