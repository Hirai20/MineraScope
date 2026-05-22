using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Tensorflow;
using Tensorflow.Keras;
using Tensorflow.Keras.Callbacks;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.Binding;
using static Tensorflow.KerasApi;

namespace MineraScope
{
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

        // 260507Codex: TensorFlow.Keras 0.15.0 では val_loss が履歴に出ない場合があるため、学習を止めない loss 監視の EarlyStopping を共通生成します。
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
                monitor: "loss",
                patience: patience,
                verbose: 1,
                mode: "auto",
                baseline: float.NaN,
                restore_best_weights: true,
                start_from_epoch: 0
            );
        }

        // 260430Codex: 端成分解析はスペクトルデータ loader に委譲し、DeepLearning は学習/予測入口だけを持ちます。
        // 260514Codex: Keras の epoch 境界で token を確認し、batch の途中では止めないキャンセル callback です。
        private sealed class CancellationTrainingCallback : ICallback
        {
            private readonly CancellationToken _cancellationToken;
            private Dictionary<string, List<float>> _history = [];

            public CancellationTrainingCallback(CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
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

            public void on_epoch_begin(int epoch) => _cancellationToken.ThrowIfCancellationRequested();

            public void on_epoch_end(int epoch, Dictionary<string, float> epoch_logs) =>
                _cancellationToken.ThrowIfCancellationRequested();

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
        private static void FitModelWithCancellation(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            int batchSize,
            int epochs,
            float validationSplit,
            int patience,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.fit(
                xTrain,
                yTrain,
                batch_size: batchSize,
                epochs: epochs,
                validation_split: validationSplit,
                callbacks: new List<ICallback>
                {
                    CreateEarlyStopping(model, epochs, patience),
                    new CancellationTrainingCallback(cancellationToken)
                }
            );
            cancellationToken.ThrowIfCancellationRequested();
        }

        public MineralFolder? AnalyzeMineralFolder(string folderPath) =>
            SpectrumDataLoader.AnalyzeMineralFolder(folderPath);

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

            cancellationToken.ThrowIfCancellationRequested();
            string classificationOutputPath = Path.Combine(outputPath, "AllMinerals_Classification");
            Log("分類モデル学習開始");
            TrainClassificationModel(orderedPools, epochs, batchSize, patience, testSplit, classificationOutputPath, cancellationToken);

            foreach (var pool in orderedPools.Where(pool => pool.EndmemberNames.Count >= 2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string regressionOutputPath = Path.Combine(outputPath, $"{pool.MineralName}_Regression");
                Log($"回帰モデル学習開始: {pool.MineralName}");
                TrainRegressionModel(pool, epochs, batchSize, patience, testSplit, regressionOutputPath, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Log(" 指定された全鉱物の処理が完了しました");
        }

        // 260514Codex: 旧フォルダ入力の学習経路にも同じ協調キャンセルを通します。
        public void RunTraining(
            List<string> mineralNames,
            string trainingDataFolder,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputPath))
            {
                Log("モデルの保存先を指定してください。");
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            string classificationOutputPath = Path.Combine(outputPath, "AllMinerals_Classification");

            Log("分類モデル学習開始");
            TrainClassificationModel(mineralNames, trainingDataFolder, epochs, batchSize, patience, testSplit, classificationOutputPath, cancellationToken);
            foreach (var mineralName in mineralNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 260430Codex: 回帰モデル対象フォルダの解決は共通 helper へ戻し、スペクトル判定を統一します。
                if (!SpectrumDataLoader.TryResolveMineralFolderPath(trainingDataFolder, mineralName, out var mineralFolderPath))
                {
                    Log($"警告: {mineralName} のフォルダが見つかりません。スキップします。\n");
                    continue;
                }

                //  固溶体の場合は回帰モデルも訓練
                // 260430Codex: 解析できないフォルダは固溶体ではないものとして安全にスキップします。
                if (AnalyzeMineralFolder(mineralFolderPath)?.IsSolidSolution == true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string regressionOutputPath = Path.Combine(outputPath, $"{mineralName}_Regression");
                    Log($"回帰モデル学習開始: {mineralName}");
                    TrainRegressionModel(mineralName, mineralFolderPath, epochs, batchSize, patience, testSplit, regressionOutputPath, cancellationToken);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Log(" 指定された全鉱物の処理が完了しました");
        }

        // 260507Codex: manifest の endmemberFractions から回帰ラベルを作り、ファイル名パースを通らずに学習します。
        // 260514Codex: manifest 由来の回帰学習でも読み込み、fit、評価、保存の境目でキャンセルを確認します。
        private void TrainRegressionModel(
            SpectrumTrainingPool trainingPool,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            CancellationToken cancellationToken)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log("端成分割合予測モデル \n");
            Log($"対象鉱物: {trainingPool.MineralName}");

            Log("manifest からスペクトルデータを読み込み中...");
            var (allSpectra, allLabels, componentIdx) = SpectrumDataLoader.LoadRegressionData(trainingPool, cancellationToken);
            ComponentIndex = componentIdx;

            if (allSpectra.shape[0] == 0 || ComponentIndex == null)
            {
                Log("エラー: 端成分情報が見つかりません。");
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

            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitRegression(
                allSpectra, allLabels, testSize: testSplit, randomState: 42);
            Log($"  訓練データ: {xTrain.shape[0]}件");
            Log($"  テストデータ: {xTest.shape[0]}件\n");

            var model = CreateRegressionModel(ComponentIndex.Count);

            Log($"  入力: {SpectrumLength}次元スペクトル");
            Log($"  隠れ層: Dense(64, relu) → Dense(64, relu)");

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.MeanSquaredError(),
                metrics: new[] { "mae" }
            );

            FitModelWithCancellation(model, xTrain, yTrain, batchSize, epochs, testSplit, patience, cancellationToken);

            Log("モデル評価中");
            cancellationToken.ThrowIfCancellationRequested();
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testMae = GetMetricValue(score, 1, "mae", "mean_absolute_error");

            Log($" 評価完了");
            Log($"  Test Loss (MSE): {testLoss:F6}");

            if (testMae > 0)
            {
                Log($"  Test MAE: {testMae:F6}\n");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);

            cancellationToken.ThrowIfCancellationRequested();
            model.save(outputPath);
            cancellationToken.ThrowIfCancellationRequested();

            string componentPath = Path.Combine(outputPath, "componentIndex.json");
            File.WriteAllText(componentPath, System.Text.Json.JsonSerializer.Serialize(componentIdx));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "regression");

            string mineralNamePath = Path.Combine(outputPath, "mineralName.txt");
            File.WriteAllText(mineralNamePath, trainingPool.MineralName);

            Log($" モデル保存完了: {outputPath}\n");
        }

        // 260430Codex: 回帰学習は余分な入れ子を外し、スペクトル長と評価値取得を共通 helper でそろえます。
        // 260514Codex: フォルダ由来の回帰学習でも読み込み、fit、評価、保存の境目でキャンセルを確認します。
        private void TrainRegressionModel(
            string mineralName,
            string trainingDataFolder,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            CancellationToken cancellationToken)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log("端成分割合予測モデル \n");
            Log($"訓練データフォルダ: {trainingDataFolder}");

            // データ読み込み
            Log("フォルダからスペクトルデータを読み込み中...");
            var (allSpectra, allLabels, componentIdx) = SpectrumDataLoader.LoadRegressionData(trainingDataFolder, cancellationToken);
            ComponentIndex = componentIdx;

            if (allSpectra.shape[0] == 0 || ComponentIndex == null)
            {
                Log("エラー: 端成分情報が見つかりません。");
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

            // データ分割
            // 260507Codex: UI のテスト分割率を回帰学習にも反映します。
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitRegression(
                allSpectra, allLabels, testSize: testSplit, randomState: 42);
            Log($"  訓練データ: {xTrain.shape[0]}件");
            Log($"  テストデータ: {xTest.shape[0]}件\n");

            var model = CreateRegressionModel(ComponentIndex.Count);

            Log($"  入力: {SpectrumLength}次元スペクトル");
            Log($"  隠れ層: Dense(64, relu) → Dense(64, relu)");

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.MeanSquaredError(),
                metrics: new[] { "mae" }
            );

            FitModelWithCancellation(model, xTrain, yTrain, batchSize, epochs, 0.1f, patience, cancellationToken);
            // 評価
            Log("モデル評価中");
            cancellationToken.ThrowIfCancellationRequested();
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testMae = GetMetricValue(score, 1, "mae", "mean_absolute_error");

            Log($" 評価完了");
            Log($"  Test Loss (MSE): {testLoss:F6}");

            if (testMae > 0)
            {
                Log($"  Test MAE: {testMae:F6}\n");
            }

            // モデル保存
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);

            cancellationToken.ThrowIfCancellationRequested();
            model.save(outputPath);
            cancellationToken.ThrowIfCancellationRequested();

            string componentPath = Path.Combine(outputPath, "componentIndex.json");
            File.WriteAllText(componentPath, System.Text.Json.JsonSerializer.Serialize(componentIdx));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "regression");

            string mineralNamePath = Path.Combine(outputPath, "mineralName.txt");
            File.WriteAllText(mineralNamePath, mineralName);

            Log($" モデル保存完了: {outputPath}\n");
        }
        // 260430Codex: 分類学習側も tuple 展開と共通評価 helper で回帰学習と読み方をそろえます。
        // 260507Codex: 新方式の分類学習は manifest 由来の pool だけを読み込みます。
        // 260514Codex: 分類学習でもデータ読み込み、fit、評価、保存の境目でキャンセルを確認します。
        private void TrainClassificationModel(
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            CancellationToken cancellationToken)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log($"\n 訓練データ読み込み中");
            var (allSpectra, allLabelsList) = SpectrumDataLoader.LoadClassificationData(trainingPools, cancellationToken);
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

            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(allSpectra, labelsEncoded, testSize: testSplit, randomState: 42);
            Log($"訓練データ: {xTrain.shape[0]}");
            Log($"テストデータ: {xTest.shape[0]}\n");

            var model = CreateClassificationModel(encoder.Count);

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );

            Log("訓練中...");
            FitModelWithCancellation(model, xTrain, yTrain, batchSize, epochs, testSplit, patience, cancellationToken);
            Log("モデル評価中");

            cancellationToken.ThrowIfCancellationRequested();
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testAccuracy = GetMetricValue(score, 1, "accuracy");

            Log($"Test loss: {testLoss:F4}");
            Log($"Test accuracy: {testAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, "labelEncoder.json");
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "classification");
            cancellationToken.ThrowIfCancellationRequested();
            model.save(outputPath);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // 260514Codex: 旧フォルダ入力の分類学習でもデータ読み込み、fit、評価、保存の境目でキャンセルを確認します。
        private void TrainClassificationModel(
            List<string> mineralNames,
            string trainingDataFolder,
            int epochs,
            int batchSize,
            int patience,
            float testSplit,
            string outputPath,
            CancellationToken cancellationToken)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log($"\n 訓練データ読み込み中");
            var (allSpectra, allLabelsList) = SpectrumDataLoader.LoadClassificationData(trainingDataFolder, mineralNames, cancellationToken);
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
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(allSpectra, labelsEncoded, testSize: testSplit, randomState: 42);
            Log($"訓練データ: {xTrain.shape[0]}");
            Log($"テストデータ: {xTest.shape[0]}\n");

            var model = CreateClassificationModel(encoder.Count);

            //Compile
            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );

            Log("訓練中...");
            FitModelWithCancellation(model, xTrain, yTrain, batchSize, epochs, 0.2f, patience, cancellationToken);
            Log("モデル評価中");

            cancellationToken.ThrowIfCancellationRequested();
            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testAccuracy = GetMetricValue(score, 1, "accuracy");

            Log($"Test loss: {testLoss:F4}");
            Log($"Test accuracy: {testAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, "labelEncoder.json");
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "classification");
            cancellationToken.ThrowIfCancellationRequested();
            model.save(outputPath);
            cancellationToken.ThrowIfCancellationRequested();
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

