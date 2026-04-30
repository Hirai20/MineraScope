using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
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

        // 260416Codex: スペクトルファイル判定と列挙の条件を共通化し、同じ拡張子判定の重複を減らします。
        private const int SpectrumLength = 2048;

        private static bool IsSpectrumFile(string filePath) =>
            filePath.EndsWith(".msa", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".emsa", StringComparison.OrdinalIgnoreCase);

        private static string[] GetSpectrumFiles(string folderPath, SearchOption searchOption = SearchOption.AllDirectories) =>
            Directory.EnumerateFiles(folderPath, "*.*", searchOption)
                .Where(IsSpectrumFile)
                .OrderBy(path => path)
                .ToArray();

        private static bool HasSpectrumFiles(string folderPath) =>
            Directory.Exists(folderPath) && Directory.EnumerateFiles(folderPath, "*.*").Any(IsSpectrumFile);

        // 260416Codex: 鉱物名から実際の学習フォルダを解決する分岐を helper に寄せ、分類と回帰の重複を減らします。
        private static bool TryResolveMineralFolderPath(string trainingFolder, string mineralName, out string mineralFolderPath)
        {
            string combinedPath = Path.Combine(trainingFolder, mineralName);
            if (Directory.Exists(combinedPath))
            {
                mineralFolderPath = combinedPath;
                return true;
            }

            if (Path.GetFileName(trainingFolder) == mineralName || HasSpectrumFiles(trainingFolder))
            {
                mineralFolderPath = trainingFolder;
                return true;
            }

            mineralFolderPath = string.Empty;
            return false;
        }

        // 260416Codex: 2048 点スペクトルの読み込みと正規化を共通化し、学習と予測の重複処理を減らします。
        private static float[]? LoadNormalizedSpectrum(string filePath)
        {
            var data = LoadMsaFile(filePath);
            if (data.shape[0] != SpectrumLength)
            {
                return null;
            }

            var values = data.ToArray<float>();
            NormalizeSpectrum(values);
            return values;
        }

        private static void NormalizeSpectrum(float[] values)
        {
            float max = values.Max();
            if (max <= 0)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] /= max;
            }
        }

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

        // 260416Codex: 端成分解析側でも共通のスペクトル列挙 helper を使い、条件分岐を読みやすくします。
        public MineralFolder? AnalyzeMineralFolder(string folderPath)
        {
            var files = GetSpectrumFiles(folderPath);

            if (files.Length == 0)
            {
                return null;
            }

            var mineralFolder = new MineralFolder
            {
                Name = Path.GetFileName(folderPath),
                FolderPath = folderPath
            };

            // ファイル名から端成分を検出
            var allComponents = new HashSet<string>();

            foreach (var file in files)
            {
                var components = GetRegressionLabels(file);
                if (components is not { Count: > 0 })
                {
                    continue;
                }

                foreach (var (comp, _) in components)
                {
                    allComponents.Add(comp);
                }
            }

            // 2つ以上の端成分がある = 固溶体
            mineralFolder.IsSolidSolution = allComponents.Count >= 2;
            mineralFolder.EndMembers = mineralFolder.IsSolidSolution
                ? allComponents.OrderBy(x => x).ToList()
                : [];

            return mineralFolder;
        }

        // 260416Codex: MSA 読み込みは using var と早期 continue を使って少し浅い構造に整理します。
        static NDArray LoadMsaFile(string filePath)
        {
            var yValues = new List<float>();

            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    continue;
                }

                line = line.TrimEnd(',');

                if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    yValues.Add(value);
                }
            }

            return np.array(yValues.ToArray());
        }

        // 260416Codex: 回帰データ読み込みではスペクトル列挙と正規化 helper を使い、2 パス処理だけを目立たせます。
        static (NDArray, NDArray, Dictionary<string, int>?) LoadRegressionData(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 2)), null);
            }

            var files = GetSpectrumFiles(directory);
            var spectraList = new List<float[]>();
            var labelsList = new List<Dictionary<string, float>>();
            var allComponents = new HashSet<string>();

            // 第1パス: 端成分を収集
            foreach (var filePath in files)
            {
                var components = GetRegressionLabels(filePath);
                if (components is not { Count: > 0 })
                {
                    continue;
                }

                foreach (var (comp, _) in components)
                {
                    allComponents.Add(comp);
                }
            }

            if (allComponents.Count == 0)
            {
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, 2)), null);
            }

            var componentOrder = allComponents.OrderBy(x => x).ToList();
            var componentIndex = componentOrder.Select((c, i) => new { c, i }).ToDictionary(x => x.c, x => x.i);

            // 第2パス: データ読み込み
            foreach (var filePath in files)
            {
                var data = LoadNormalizedSpectrum(filePath);
                var components = GetRegressionLabels(filePath);

                if (data == null || components is not { Count: > 0 })
                {
                    continue;
                }

                spectraList.Add(data);
                labelsList.Add(components.ToDictionary(component => component.Component, component => component.Ratio));
            }

            if (spectraList.Count == 0)
            {
                return (np.zeros(new Shape(0, SpectrumLength)), np.zeros(new Shape(0, componentOrder.Count)), null);
            }

            // 配列化
            int numSamples = spectraList.Count;
            int numComponents = componentOrder.Count;
            float[,] spectraArray = new float[numSamples, SpectrumLength];

            for (int i = 0; i < numSamples; i++)
            {
                for (int j = 0; j < SpectrumLength; j++)
                {
                    spectraArray[i, j] = spectraList[i][j];
                }
            }

            float[,] labelsArray = new float[numSamples, numComponents];
            for (int i = 0; i < numSamples; i++)
            {
                var labelDict = labelsList[i];
                for (int j = 0; j < numComponents; j++)
                {
                    string compName = componentOrder[j];
                    labelsArray[i, j] = labelDict.ContainsKey(compName) ? labelDict[compName] : 0f;
                }
            }

            return (np.array(spectraArray), np.array(labelsArray), componentIndex);
        }

        // 260416Codex: 回帰ラベル抽出は null を返すケースを型にも表して呼び出し側の意図を明確にします。
        static List<(string Component, float Ratio)>? GetRegressionLabels(string filePath)
        {
            string filename = Path.GetFileNameWithoutExtension(filePath);

            var pattern = @"([a-zA-Z0-9\s]+?)(0\.\d+|1\.0+)";
            var matches = Regex.Matches(filename, pattern);

            var components = new List<(string, float)>();

            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string componentName = match.Groups[1].Value;
                    string ratioStr = match.Groups[2].Value;

                    if (float.TryParse(ratioStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
                    {
                        components.Add((componentName, ratio));
                    }
                }
            }

            return components.Count > 0 ? components : null;
        }

        // 260416Codex: 分類データ読み込みでもフォルダ解決とスペクトル正規化を helper に寄せて本筋を短くします。
        static (NDArray, List<string>) LoadClassificationData(string TrainingFolder, List<string> targetMinerals)
        {
            var spectraList = new List<float[]>();
            var labels = new List<string>();

            // 指定された鉱物リストのフォルダだけを処理する
            foreach (var mineralName in targetMinerals)
            {
                if (!TryResolveMineralFolderPath(TrainingFolder, mineralName, out var mineralFolderPath))
                {
                    continue; // 見つからない場合はスキップ
                }

                //  "Olivine_10mol%" でも "Olivine" としてラベル付けする
                string labelName = mineralName.Split('_')[0];

                // サブディレクトリも含めて .msa or.emsaファイルを検索
                var files = GetSpectrumFiles(mineralFolderPath);

                foreach (var filePath in files)
                {
                    var data = LoadNormalizedSpectrum(filePath);
                    if (data == null)
                    {
                        continue;
                    }

                    spectraList.Add(data);
                    labels.Add(labelName);
                }
            }

            if (spectraList.Count == 0)
            {
                return (np.zeros(new Shape(0, SpectrumLength)), labels);
            }

            // NDArrayへの変換
            int numSamples = spectraList.Count;
            float[,] spectraArray = new float[numSamples, SpectrumLength];

            for (int i = 0; i < numSamples; i++)
            {
                for (int j = 0; j < SpectrumLength; j++)
                {
                    spectraArray[i, j] = spectraList[i][j];
                }
            }

            return (np.array(spectraArray), labels);
        }

        #region ラベルエンコーディングとデータ分割

        static (NDArray, Dictionary<string, int>) EncodeLabels(List<string> labels)
        {
            var uniqueLabels = labels.Distinct().OrderBy(x => x).ToList();
            var encoder = uniqueLabels.Select((label, index) => new { label, index })
                                      .ToDictionary(x => x.label, x => x.index);

            int[] encoded = labels.Select(label => encoder[label]).ToArray();
            return (np.array(encoded), encoder);
        }

        static (NDArray xTrain, NDArray xTest, NDArray yTrain, NDArray yTest) TrainTestSplitClassification(NDArray X, NDArray y, float testSize = 0.2f, int randomState = 42)
        {
            int numSamples = (int)X.shape[0];
            int testCount = (int)(numSamples * testSize);
            int trainCount = numSamples - testCount;

            var indices = Enumerable.Range(0, numSamples).ToArray();
            var rng = new Random(randomState);

            // Fisher-Yates シャッフル
            for (int i = numSamples - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                int temp = indices[i];
                indices[i] = indices[j];
                indices[j] = temp;
            }

            var trainIndices = indices.Take(trainCount).ToArray();
            var testIndices = indices.Skip(trainCount).ToArray();

            var xArray = X.ToArray<float>();
            var yArray = y.ToArray<int>();

            float[,] xTrainArray = new float[trainCount, 2048];
            float[,] xTestArray = new float[testCount, 2048];
            int[] yTrainArray = new int[trainCount];
            int[] yTestArray = new int[testCount];

            for (int i = 0; i < trainCount; i++)
            {
                int idx = trainIndices[i];
                yTrainArray[i] = yArray[idx];
                for (int j = 0; j < 2048; j++)
                {
                    xTrainArray[i, j] = xArray[idx * 2048 + j];
                }
            }

            for (int i = 0; i < testCount; i++)
            {
                int idx = testIndices[i];
                yTestArray[i] = yArray[idx];
                for (int j = 0; j < 2048; j++)
                {
                    xTestArray[i, j] = xArray[idx * 2048 + j];
                }
            }

            return (np.array(xTrainArray), np.array(xTestArray),
                    np.array(yTrainArray), np.array(yTestArray));
        }
        static (NDArray xTrain, NDArray xTest, NDArray yTrain, NDArray yTest) TrainTestSplitRegression(
           NDArray X, NDArray y, float testSize = 0.2f, int randomState = 42)
        {
            int nSamples = (int)X.shape[0];
            int nFeatures = (int)X.shape[1];
            int nLabels = (int)y.shape[1];

            int nTest = (int)(nSamples * testSize);
            int nTrain = nSamples - nTest;

            var rnd = new Random(randomState);
            int[] indices = Enumerable.Range(0, nSamples)
                                      .OrderBy(_ => rnd.Next())
                                      .ToArray();

            float[,] xTrainArr = new float[nTrain, nFeatures];
            float[,] xTestArr = new float[nTest, nFeatures];
            float[,] yTrainArr = new float[nTrain, nLabels];
            float[,] yTestArr = new float[nTest, nLabels];

            var Xall = X.ToArray<float>();
            var yall = y.ToArray<float>();

            for (int i = 0; i < nTrain; i++)
            {
                int idx = indices[i];
                for (int j = 0; j < nFeatures; j++)
                    xTrainArr[i, j] = Xall[idx * nFeatures + j];

                for (int k = 0; k < nLabels; k++)
                    yTrainArr[i, k] = yall[idx * nLabels + k];
            }

            for (int i = 0; i < nTest; i++)
            {
                int idx = indices[nTrain + i];
                for (int j = 0; j < nFeatures; j++)
                    xTestArr[i, j] = Xall[idx * nFeatures + j];

                for (int k = 0; k < nLabels; k++)
                    yTestArr[i, k] = yall[idx * nLabels + k];
            }

            return (
                np.array(xTrainArr),
                np.array(xTestArr),
                np.array(yTrainArr),
                np.array(yTestArr)
            );
        }
        #endregion
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
                if (!TryResolveMineralFolderPath(TrainingDataFolder, mineralName, out var mineralFolderPath))
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
            var (allSpectra, allLabels, componentIdx) = LoadRegressionData(TrainingDataFolder);
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
            var (xTrain, xTest, yTrain, yTest) = TrainTestSplitRegression(
                allSpectra, allLabels, testSize: 0.2f, randomState: 42);
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

            // EarlyStopping
            var cbParams = new CallbackParams
            {
                Model = model,
                Epochs = epochs,
                Verbose = 1
            };

            var earlyStop = new EarlyStopping(
                parameters: cbParams,
                monitor: "val_loss",
                patience: patience,
                verbose: 1,
                mode: "auto",
                baseline: float.NaN,
                restore_best_weights: true,
                start_from_epoch: 0
            );
            model.fit(
                xTrain,
                yTrain,
                batch_size: 16,
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
            var (allSpectra, allLabelsList) = LoadClassificationData(TrainingDataFolder, mineralNames);
            Log($"\n 読み込み完了 (合計 {allSpectra.shape[0]} 件)\n");
            var (labelsEncoded, encoder) = EncodeLabels(allLabelsList);
            if (encoder.Count > 0)
            {
                foreach (var kvp in encoder.OrderBy(x => x.Value))
                {
                    int count = allLabelsList.Count(l => l == kvp.Key);
                    Log($"  [{kvp.Value}] {kvp.Key} ({count}件)");
                }
            }
            var (xTrain, xTest, yTrain, yTest) = TrainTestSplitClassification(allSpectra, labelsEncoded, testSize: testSplit, randomState: 42);
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

            //EarlyStopping
            var cbParams = new CallbackParams
            {
                Model = model,  // 監視対象のモデル
                Epochs = epochs, // 最大エポック数
                Verbose = 1      // ログ出力あり
            };

            var earlyStop = new EarlyStopping(
                parameters: cbParams,
                monitor: "val_loss",
                patience: patience,
                verbose: 1,
                mode: "auto",
                baseline: float.NaN,
                restore_best_weights: true,
                start_from_epoch: 0
            );
            Log("訓練中...");
            //Fit
            model.fit(
                xTrain,
                yTrain,
                batch_size: 32,
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
                    var spectrum = LoadNormalizedSpectrum(filePath);
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
        // 260430Codex: 化学式生成は null guard と dictionary lookup を整理して早期 return で読みやすくします。
        public string GenerateFormula(Dictionary<string, float> predictedRatios, string targetMineralName, string assemblyPath)
        {
            if (predictedRatios == null || predictedRatios.Count == 0)
            {
                return "";
            }

            string xmlPath = Path.Combine(assemblyPath, "MineralDatabase.xml");
            if (!File.Exists(xmlPath))
            {
                return "";
            }

            XmlSerializer xml = new(typeof(SolidSolution[]));
            using var fs = new FileStream(xmlPath, FileMode.Open);
            var solidSolutions = xml.Deserialize(fs) as SolidSolution[];
            if (solidSolutions == null)
            {
                return "";
            }

            // 260416Codex: ローカル変数の単純な綴りミスを直して読みやすくします。
            var targetGroup = solidSolutions.FirstOrDefault(ss => ss.Name == targetMineralName);
            if (targetGroup == null)
            {
                return "";
            }

            var targetElements = new List<string>();
            int maxElementCount = targetGroup.Members.Max(ss => ss.Elements.Length);

            for (int i = 0; i < maxElementCount; i++)
            {
                foreach (var member in targetGroup.Members)
                {
                    if (i < member.Elements.Length)
                    {
                        string symbol = member.Elements[i].Item1;
                        if (!targetElements.Contains(symbol))
                        {
                            targetElements.Add(symbol);
                        }
                    }
                }
            }

            //原子数を計算
            var mineralDefinitions = new Dictionary<string, (string Element, double Count)[]>();

            foreach (var member in targetGroup.Members)
            {
                string safeXmlName = member.Name.Trim();
                mineralDefinitions.TryAdd(safeXmlName, member.Elements.Select(el => (el.Item1, el.Item2)).ToArray());
            }

            var finalAtoms = new Dictionary<string, double>();
            foreach (var kvp in predictedRatios)
            {
                string endmemberName = kvp.Key.Trim();
                double ratio = kvp.Value;
                if (!mineralDefinitions.TryGetValue(endmemberName, out var atoms))
                {
                    continue;
                }

                foreach (var atom in atoms)
                {
                    finalAtoms.TryAdd(atom.Element, 0);
                    finalAtoms[atom.Element] += atom.Count * ratio;
                }
            }

            StringBuilder sb = new();
            foreach (var element in targetElements)
            {
                double val = finalAtoms.TryGetValue(element, out double foundVal) ? foundVal : 0;
                sb.Append($"{element}{val:F2}");
            }
            return sb.ToString();
        }
    }

    // 260430Codex: DTO の既定値はプロパティ初期化子へ戻し、空コンストラクタを不要にします。
    public class MineralFolder
    {
        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public bool IsSolidSolution { get; set; }  // 固溶体かどうか
        public List<string> EndMembers { get; set; } = [];  // 端成分リスト
    }
}

