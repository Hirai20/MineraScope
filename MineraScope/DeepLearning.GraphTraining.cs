using System.Diagnostics;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.Binding;

namespace MineraScope
{
    // 260924Codex: 通常学習とSeed学習で、共通グラフの実行とネイティブ配列の所有権をまとめて管理する。
    public partial class DeepLearning
    {
        // 260605Codex: ここではTensorFlow.KerasのHistoryの信頼性が十分でないため、観測した学習結果を返す。
        // 260906Codex: 指標は独立したテスト集合ではなく、最良の検証エポックから取得する。
        private sealed record TrainingFitResult(
            int RequestedEpochs,
            int CompletedEpochs,
            int LastEpoch,
            string LastEpochMetrics,
            int? BestEpoch,
            double BestValidationLoss,
            double BestValidationMetric,
            // 260917Codex: 同じ採用重みによる、元ラベルとの厳密一致指標。
            double? LegacyValidationAccuracy = null);

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
            // 260918Codex: 通常のSoftmax呼び出しは共通の既定値1e-4を使い、定量は別の最適化処理を維持する。
            CancellationToken cancellationToken,
            // 261014Codex: 分類専用の任意引数を、学習率・代替ラベルの順に揃える。
            float classificationLearningRate = ModelTrainingSettings.DefaultClassificationLearningRate,
            // 260917Codex: 任意の代替ラベルを追加しても、定量の呼び出し側と元ラベルを変更しない。
            int[]? trainingAlternatives = null, int[]? validationAlternatives = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 260622Claude: 分類・回帰とも v1 Graph/Session 経路で学習する(高速化の本命、eager と統計的同等を実測確認済み)。
            if (operationName.StartsWith("regression", StringComparison.Ordinal))
                return RunGraphRegressionLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken);
            // 260909Codex: Seed実験は、実際に重みを更新するグラフへ記録済みの学習率を渡す。
            return RunGraphClassificationLoop(model, xTrain, yTrain, xValidation, yValidation, batchSize, epochs, patience, operationName, logAction, reportEpoch, cancellationToken, classificationLearningRate, trainingAlternatives, validationAlternatives);
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
            // 260909Codex: Denseグラフ構造を変えず、共通の学習振り分け処理から選択した学習率を受け取る。
            CancellationToken cancellationToken,
            // 261014Codex: dispatcherと同じ順序で学習率と代替ラベルを受け取る。
            float learningRate,
            // 260917Codex: 代替ラベルも、元ラベルと同じ固定行順に従う。
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
            // 260908Codex: 完了・中止時に、更新前の最良エポックのコピーも含めてネイティブ重みのコピーを解放する。
            // 261014Codex: 初期重みと採用重みの配列を完了・中止時に解放する。
            using var initialWeightLifetime = new TrainingWeightArrays(initWeights);
            using var bestWeightLifetime = new TrainingWeightArrays(null);
            TensorFlowTrainingDebugLog.Write("graph-loop-start", $"operation={op} engine=graph weights={initWeights.Count}");

            int sampleCount = (int)xTrain.shape[0];
            int valCount = (int)xValidation.shape[0];
            List<NDArray>? bestWeights = null;
            double bestValLoss = double.PositiveInfinity;
            double bestValAccuracy = 0d;
            // 260917Codex: 選択したエポックの厳密一致指標を保持する。
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
                // 260917Codex: 検知器用に元ラベルを保ち、検証の両定義を報告する。
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
                // 260917Codex: 共通の部分ラベル目的関数でも、単一ラベルの各行は通常のCEを維持する。
                var trainLoss = tf.reduce_mean(PartialLabelLoss.PerSample(Forward(tf.gather(dataX, idx)), tf.gather(dataY, idx), tf.gather(dataAlternative, idx), classCount));
                // 260621Claude: Keras Adam と同一の hyperparams(epsilon=1e-7)で minimize し、研究比較の同等性を保つ。
                // 260909Codex: 選択した学習率だけを変え、Adamの他のパラメーターは既存値を保持する。
                var optimizer = new Tensorflow.Train.AdamOptimizer(learningRate, 0.9f, 0.999f, 1e-7f, false, TF_DataType.TF_FLOAT, "Adam");
                var trainOp = optimizer.minimize(trainLoss);

                var valLogits = Forward(valX);
                // 260917Codex: 早期終了でも、学習と同じ許容ラベルの確率総和を監視する。
                var valLossOp = tf.reduce_mean(PartialLabelLoss.PerSample(valLogits, valY, valAlternative, classCount));
                // 260917Codex: 許容ラベル正解率とともに、厳密正解率も利用可能にする。
                var legacyCorrectOp = tf.reduce_sum(tf.cast(tf.equal(tf.arg_max(valLogits, 1), tf.cast(valY, tf.int64)), tf.int32));
                var valCorrectOp = tf.reduce_sum(tf.cast(PartialLabelLoss.Correct(valLogits, valY, valAlternative), tf.int32));
                // 260917Codex: 1回の取得で、3指標すべての検証順伝播とグラフ最適化を共用する。
                // 261014Codex: 検証指標と採用重みをそれぞれ一括取得する。
                ITensorOrOperation[] validationFetches = [valLossOp, legacyCorrectOp, valCorrectOp];
                ITensorOrOperation[] weightFetches = weightVars.Select(variable => (ITensorOrOperation)variable.AsTensor()).ToArray();

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
                    // 260908Codex: 全バッチを使う学習方針を変えず、バッチ間でユーザーの中止に応じる。
                    // 260930Codex: 分類・定量学習とも、完全なバッチの間で中止に応じる。
                    for (int start = 0; start + batchSize <= sampleCount; start += batchSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sess.run(trainOp, new FeedItem(startPh, start));
                    }
                    double batchLoopMs = Stopwatch.GetElapsedTime(batchLoopStart).TotalMilliseconds;

                    long valStart = Stopwatch.GetTimestamp();
                    // 260908Codex: 検証の取得結果は、スカラー1個しか使わなくてもネイティブ配列を所有する。
                    // 260917Codex: 全指標で同じlogitsを使い、許容ラベルと厳密一致の定義は分ける。
                    // 261014Codex: 同じ検証forwardで得た配列を、このepochの集計後に解放する。
                    var validationValues = sess.run(validationFetches).ToList();
                    using var validationLifetime = new TrainingWeightArrays(validationValues);
                    double valLoss = validationValues[0].ToArray<float>()[0];
                    double legacyAccuracy = valCount > 0 ? validationValues[1].ToArray<int>()[0] / (double)valCount : 0d;
                    double valAccuracy = valCount > 0 ? validationValues[2].ToArray<int>()[0] / (double)valCount : 0d;
                    double validationMs = Stopwatch.GetElapsedTime(valStart).TotalMilliseconds;

                    bool willStop = false;
                    if (valLoss < bestValLoss)
                    {
                        bestValLoss = valLoss;
                        bestValAccuracy = valAccuracy;
                        // 260917Codex: 方針比較を最良チェックポイントに合わせる。
                        bestLegacyAccuracy = legacyAccuracy;
                        bestEpoch = epoch + 1;
                        // 260908Codex: 現在の採用候補だけをメモリに保持する。
                        // 260917Codex: 独立したグラフ実行を6回始めず、採用する6テンソルをまとめて取得する。
                        // 261014Codex: 新しい採用重みを取得してから、前の候補を解放する。
                        bestWeights = sess.run(weightFetches).ToList();
                        bestWeightLifetime.Replace(bestWeights);
                        wait = 0;
                    }
                    else if (++wait >= patience)
                        willStop = true;

                    // 260622Claude: 学習データ全体の metric はログ専用かつ初回ウォームアップが重いので算出しない。val(=test) のみ毎epoch評価する。
                    // 260917Codex: 全エポックの報告で、従来の元ラベル指標を明示する。
                    string logs = $"val_loss={FormatMetric(valLoss)},val_accuracy={FormatMetric(valAccuracy)},legacy_val_accuracy={FormatMetric(legacyAccuracy)}";

                    completedEpochs++;
                    lastEpoch = epoch;
                    lastEpochMetrics = logs;
                    TensorFlowTrainingDebugLog.Write("epoch-end", $"operation={op} epoch={epoch} engine=graph durationMs={epochStopwatch.ElapsedMilliseconds} batchLoopMs={FormatMetric(batchLoopMs)} validationMs={FormatMetric(validationMs)} {TensorFlowTrainingDebugLog.Clean(logs)}");
                    logAction($"  Epoch {epoch + 1}/{epochs} [{operationName}]: {logs}");

                    // 260924Codex: 早期終了するエポックも含め、完了エポックを各1回報告する。
                    // 260930Codex: コールバック順序を変えず、両方の早期終了経路で進捗報告を共用する。
                    reportEpoch?.Invoke(epoch);
                    if (willStop)
                        break;
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
                    // 260930Codex: 分類・定量学習とも、完全なバッチの間で中止に応じる。
                    for (int start = 0; start + batchSize <= sampleCount; start += batchSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sess.run(trainOp, new FeedItem(startPh, start));
                    }
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

                    // 260924Codex: 早期終了するエポックも含め、完了エポックを各1回報告する。
                    // 260930Codex: コールバック順序を変えず、両方の早期終了経路で進捗報告を共用する。
                    reportEpoch?.Invoke(epoch);
                    if (willStop)
                        break;
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

        // 260908Codex: ネイティブ配列の所有権は学習スコープに従い、より良いエポックが前の候補を置き換える場合だけ移譲する。
        // 261014Codex: fit内で所有する配列を、候補の更新時とscope終了時に解放する。
        private sealed class TrainingWeightArrays(List<NDArray>? values) : IDisposable
        {
            private List<NDArray>? _values = values;

            public void Replace(List<NDArray> replacement)
            {
                Dispose();
                _values = replacement;
            }

            public void Dispose()
            {
                if (_values is null)
                    return;
                foreach (var value in _values)
                    value.Dispose();
                _values = null;
            }
        }
    }
}
