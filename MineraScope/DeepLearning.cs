using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        // 260416Codex: 評価辞書から優先キー順で値を取る helper を追加し、回帰・分類で同じ流れを再利用します。
        private static double GetMetricValue<TMetric>(IReadOnlyDictionary<string, TMetric> metrics, int fallbackIndex, params string[] keys)
            where TMetric : struct, IConvertible
        {
            foreach (var key in keys)
            {
                if (metrics.TryGetValue(key, out var value))
                {
                    return value.ToDouble(CultureInfo.InvariantCulture);
                }
            }

            if (metrics.Count > fallbackIndex)
            {
                return metrics.ElementAt(fallbackIndex).Value.ToDouble(CultureInfo.InvariantCulture);
            }

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
        public MineralFolder? AnalyzeMineralFolder(string folderPath) =>
            SpectrumDataLoader.AnalyzeMineralFolder(folderPath);

        #region モデル訓練
        public void RunTraining(List<string> mineralNames, string TrainingDataFolder, int epochs, int batchSize, int patience, float testSplit, string outputPath)
        {
            if (!Directory.Exists(outputPath))
            {
                Log("モデルの保存先を指定してください。");
                return;
            }
            string classificationOutputPath = Path.Combine(outputPath, "AllMinerals_Classification");

            TrainClassificationModel(mineralNames, TrainingDataFolder, epochs, batchSize, patience, testSplit, classificationOutputPath);
            foreach (var mineralName in mineralNames)
            {
                // 260430Codex: 回帰モデル対象フォルダの解決は共通 helper へ戻し、スペクトル判定を統一します。
                if (!SpectrumDataLoader.TryResolveMineralFolderPath(TrainingDataFolder, mineralName, out var mineralFolderPath))
                {
                    Log($"警告: {mineralName} のフォルダが見つかりません。スキップします。\n");
                    continue;
                }

                //  固溶体の場合は回帰モデルも訓練
                // 260430Codex: 解析できないフォルダは固溶体ではないものとして安全にスキップします。
                if (AnalyzeMineralFolder(mineralFolderPath)?.IsSolidSolution == true)
                {
                    string regressionOutputPath = Path.Combine(outputPath, $"{mineralName}_Regression");
                    TrainRegressionModel(mineralName, mineralFolderPath, epochs, batchSize, patience, testSplit, regressionOutputPath);
                }
            }
            Log(" 指定された全鉱物の処理が完了しました");
        }

        // 260430Codex: 回帰学習は余分な入れ子を外し、スペクトル長と評価値取得を共通 helper でそろえます。
        private void TrainRegressionModel(string mineralName, string TrainingDataFolder, int epochs, int batchSize, int patience, float testSplit, string outputPath)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log("端成分割合予測モデル \n");
            Log($"訓練データフォルダ: {TrainingDataFolder}");

            // データ読み込み
            Log("フォルダからスペクトルデータを読み込み中...");
            var (allSpectra, allLabels, componentIdx) = SpectrumDataLoader.LoadRegressionData(TrainingDataFolder);
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

            // モデル構築
            int numComponents = ComponentIndex.Count;

            var model = keras.Sequential(new List<ILayer>
            {
                keras.layers.Dense(64, activation: "relu", input_shape: new Shape(SpectrumLength)),
                keras.layers.Dense(64, activation: "relu"),
                keras.layers.Dense(numComponents)  // 活性化関数なし（線形出力）
            });

            Log($"  入力: {SpectrumLength}次元スペクトル");
            Log($"  隠れ層: Dense(64, relu) → Dense(64, relu)");

            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.MeanSquaredError(),
                metrics: new[] { "mae" }
            );

            // 260507Codex: EarlyStopping は共通 helper で作成し、val_loss 欠落時の KeyNotFoundException を避けます。
            var earlyStop = CreateEarlyStopping(model, epochs, patience);
            model.fit(
                xTrain,
                yTrain,
                // 260507Codex: UI から渡されたバッチサイズを回帰学習にも反映します。
                batch_size: batchSize,
                epochs: epochs,
                validation_split: 0.1f,
                callbacks: new List<ICallback> { earlyStop }
            );
            // 評価
            Log("モデル評価中");
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
            Directory.CreateDirectory(outputPath);

            model.save(outputPath);

            string componentPath = Path.Combine(outputPath, "componentIndex.json");
            File.WriteAllText(componentPath, System.Text.Json.JsonSerializer.Serialize(componentIdx));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "regression");

            string mineralNamePath = Path.Combine(outputPath, "mineralName.txt");
            File.WriteAllText(mineralNamePath, mineralName);

            Log($" モデル保存完了: {outputPath}\n");
        }
        // 260430Codex: 分類学習側も tuple 展開と共通評価 helper で回帰学習と読み方をそろえます。
        private void TrainClassificationModel(List<string> mineralNames, string TrainingDataFolder, int epochs, int batchSize, int patience, float testSplit, string outputPath)
        {
            keras.backend.clear_session();
            tf.set_random_seed(42);
            Log($"\n 訓練データ読み込み中");
            var (allSpectra, allLabelsList) = SpectrumDataLoader.LoadClassificationData(TrainingDataFolder, mineralNames);
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

            int numClasses = encoder.Count;

            var model = keras.Sequential(new List<ILayer>
                {
                    keras.layers.Dense(128, activation: "relu", input_shape: new Shape(SpectrumLength)),
                    keras.layers.Dense(64, activation: "relu"),
                    keras.layers.Dense(numClasses, activation: "softmax")
                });

            //Compile
            model.compile(
                optimizer: keras.optimizers.Adam(),
                loss: keras.losses.SparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );

            // 260507Codex: EarlyStopping は共通 helper で作成し、val_loss 欠落時の KeyNotFoundException を避けます。
            var earlyStop = CreateEarlyStopping(model, epochs, patience);
            Log("訓練中...");
            //Fit
            model.fit(
                xTrain,
                yTrain,
                // 260507Codex: UI から渡されたバッチサイズを分類学習にも反映します。
                batch_size: batchSize,
                epochs: epochs,
                validation_split: 0.2f,
                callbacks: new List<ICallback> { earlyStop }
             );
            Log("モデル評価中");

            var score = model.evaluate(xTest, yTest);

            double testLoss = GetMetricValue(score, 0, "loss");
            double testAccuracy = GetMetricValue(score, 1, "accuracy");

            Log($"Test loss: {testLoss:F4}");
            Log($"Test accuracy: {testAccuracy * 100:F2}%");
            Directory.CreateDirectory(outputPath);
            string encoderPath = Path.Combine(outputPath, "labelEncoder.json");
            File.WriteAllText(encoderPath, System.Text.Json.JsonSerializer.Serialize(encoder));

            string modelTypePath = Path.Combine(outputPath, "modelType.txt");
            File.WriteAllText(modelTypePath, "classification");
            model.save(outputPath);
        }

        #endregion
        #region 学習済みモデルを利用して予測
        // 260430Codex: 予測処理は共通の正規化 helper と null guard を使い、無効データを早期にスキップします。
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
                keras.backend.clear_session();
                var classificationModel = keras.models.load_model(classificationPath);
                string encoderPath = Path.Combine(classificationPath, "labelEncoder.json");
                if (!File.Exists(encoderPath))
                {
                    Log("labelEncoder.json が見つかりません。");
                    return;
                }
                var encoder = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, int>>(File.ReadAllText(encoderPath));
                if (encoder == null || encoder.Count == 0)
                {
                    Log("labelEncoder.json の内容を読み取れませんでした。");
                    return;
                }

                if (files.Count == 0)
                {
                    Log("予測対象のファイルがありません。");
                    return;
                }
                foreach (var filePath in files)
                {
                    var spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath);
                    if (spectrum == null)
                    {
                        continue;
                    }

                    var spectrumReshaped = np.array(spectrum).reshape(new Shape(1, SpectrumLength));

                    // 分類予測
                    var classificationPrediction = classificationModel.predict(spectrumReshaped);
                    var classPredArray = classificationPrediction.numpy().ToArray<float>();

                    // 最も確率が高いクラスを取得
                    int predictedClassIndex = Array.IndexOf(classPredArray, classPredArray.Max());
                    string predictedMineral = encoder.FirstOrDefault(x => x.Value == predictedClassIndex).Key;
                    float confidence = classPredArray[predictedClassIndex] * 100;
                    Log($"\r\nファイル: {Path.GetFileName(filePath)}");
                    Log($"【分類結果】");
                    Log($"  予測鉱物: {predictedMineral} ({confidence:F2}%)");
                    var sortedResults = encoder.OrderByDescending(x => classPredArray[x.Value]);
                    foreach (var kvp in sortedResults)
                    {
                        Log($"    {kvp.Key}: {classPredArray[kvp.Value] * 100:F2}%");
                    }

                    // 260430Codex: 処理時間表示を外し、成分比率予測結果だけをログに残します。
                    string[] candidates = Directory.GetDirectories(modelPath, $"{predictedMineral}*_Regression");
                    if (candidates.Length == 0)
                    {
                        continue;
                    }

                    Log($"【成分比率予測】");

                    keras.backend.clear_session();
                    foreach (var regressionPath in candidates)
                    {
                        var regressionModel = keras.models.load_model(regressionPath);
                        string modelName = Path.GetFileName(regressionPath); // 例: Olivine_10mol%_Regression

                        Log($"\n  モデル: {modelName}"); // どのモデルかを表示

                        string componentPath = Path.Combine(regressionPath, "componentIndex.json");
                        var componentIndex = System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string, int>>(File.ReadAllText(componentPath));
                        if (componentIndex == null || componentIndex.Count == 0)
                        {
                            continue;
                        }

                        var componentNames = componentIndex.OrderBy(x => x.Value).Select(x => x.Key).ToArray();

                        var regressionPrediction = regressionModel.predict(spectrumReshaped);
                        var regPredArray = regressionPrediction.numpy().ToArray<float>();

                        var predValues = new float[componentNames.Length];
                        float sum = 0;

                        for (int j = 0; j < componentNames.Length; j++)
                        {
                            predValues[j] = Math.Max(regPredArray[j], 0.0f);
                            sum += predValues[j];
                        }

                        var resultRatios = new Dictionary<string, float>();
                        Log($"  予測端成分比率:");
                        for (int j = 0; j < componentNames.Length; j++)
                        {
                            float ratio = sum > 0 ? predValues[j] / sum : 0;
                            resultRatios.Add(componentNames[j], ratio);
                            Log($"    {componentNames[j]}: {ratio * 100:F0}");
                        }
                        Log($" 化学組成式: {GenerateFormula(resultRatios, predictedMineral, assemblyPath)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"\nエラー: {ex.Message}");
                Log($"スタックトレース: {ex.StackTrace}");
            }
        }
        #endregion
        //分類モデルで予測された固溶体の化学組成を生成
        // 260430Codex: 既存公開メソッドは残し、化学式生成の実処理は専用 helper へ委譲します。
        public string GenerateFormula(Dictionary<string, float> predictedRatios, string targetMineralName, string assemblyPath) =>
            MineralFormulaGenerator.Generate(predictedRatios, targetMineralName, assemblyPath);
    }
}

