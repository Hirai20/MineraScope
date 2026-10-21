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
    // 260908Codex: 任意のSeed実験の入口を、通常の分類・定量学習から分離する。
    // 260924Codex: このファイルはデータ読込・モデル学習・保存を調停し、グラフループと診断は別のpartialに分ける。
    public partial class DeepLearning
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

        // 260605Codex: ログ解析のため、デバッグ指標の書式をカルチャに依存させない。
        private static string FormatMetric(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

        // 260606Codex: キャッシュ件数は学習実行中の累積値なので、前後比較のため各工程で記録する。
        private static string FormatSpectrumCacheStats(SpectrumDataLoader.NormalizedSpectrumCache? cache) =>
            cache is null
                ? string.Empty
                : $" cacheableSpectra={cache.CacheablePathCount} cachedSpectra={cache.CachedCount} cacheHits={cache.HitCount} cacheMisses={cache.MissCount} cacheStores={cache.StoreCount}";

        // 260605Codex: Patienceは学習損失ではなく、検証・テスト損失を監視する意図で使う。
        // 260906Codex: 保存する学習結果を、グラフ学習で実際に使った早期終了の監視項目に結び付ける。
        internal const string EarlyStoppingMonitor = "val_loss";

        // 260906Codex: poolの種類から後で推測せず、実際に選んだ分割処理を記述する。
        private const string LegacySplitMethod = "legacy-random-v1";

        // 260622Claude: 評価・保存モデルは softmax 出力なので loss は from_logits=false。学習自体は graph 経路が logits で安定計算する。
        private static ILossFunc CreateSparseCategoricalCrossentropy() =>
            keras.losses.SparseCategoricalCrossentropy(from_logits: false);

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

        // 260917Claude: 通常学習とSeed試行の両方で、分類保存時にラベルエンコーダーとモデル種別の付随ファイルを書き込む。
        // 261014Codex: 通常学習とSeed試行の同じ保存処理を1か所へ集約する。
        private static void WriteClassificationSidecars(string classificationFolder, Dictionary<string, int> encoder)
        {
            Directory.CreateDirectory(classificationFolder);
            File.WriteAllText(Path.Combine(classificationFolder, ModelArtifactPaths.LabelEncoderFileName), System.Text.Json.JsonSerializer.Serialize(encoder));
            File.WriteAllText(Path.Combine(classificationFolder, ModelArtifactPaths.ModelTypeFileName), "classification");
        }

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
            CancellationToken cancellationToken = default,
            float classificationLearningRate = ModelTrainingSettings.DefaultClassificationLearningRate,
            // 261001Codex: 通常機能のOCN headを要求した場合だけ、特徴量をコピーする。
            bool captureClassifierFeatures = false)
        {
            // 260930Codex: スペクトル読込やTensorFlow起動の前に、不正な学習率を拒否する。
            if (!float.IsFinite(classificationLearningRate) || classificationLearningRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(classificationLearningRate));
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
            // 261021Claude: 定量モデルの対象判定は SpectrumTrainingPool の共通定義を使う。
            int regressionCount = orderedPools.Count(pool => pool.HasRegressionModel);
            // 261014Codex: 選択された分類学習率を機械可読な形式で学習開始ログへ記録する。
            TensorFlowTrainingDebugLog.Write("training-run-start", $"pools={orderedPools.Length} regressionModels={regressionCount} epochs={epochs} batchSize={batchSize} patience={patience} classificationLearningRate={classificationLearningRate.ToString(CultureInfo.InvariantCulture)}");

            // 260606Claude: 分類1個+回帰N個を1本のバーで表す。各モデルを BeginModel/CompleteModel で挟み、epoch 進捗は reporter.ReportEpoch で報告する。
            var reporter = new TrainingProgressReporter(progress, 1 + regressionCount, epochs);

            // 260606Codex: 回帰モデルで再利用する spectrum だけを分類ロード時に保持し、重複ファイル読込を避けます。
            var spectrumCache = new SpectrumDataLoader.NormalizedSpectrumCache(
                orderedPools
                    .Where(pool => pool.HasRegressionModel)
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
                classificationLearningRate,
                classificationOutputPath,
                spectrumCache,
                reporter.ReportEpoch,
                cancellationToken, captureClassifierFeatures);
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

            // 260930Codex: 共通する鉱物別件数はバッチ検証1回で確認し、UI外の呼び出しで件数が異なる場合も引き続き扱う。
            var regressionPools = orderedPools.Where(pool => pool.HasRegressionModel).ToArray();
            var regressionSkipReasons = regressionPools.Select(pool => pool.Samples.Count).Distinct().ToDictionary(
                count => count,
                count =>
                {
                    var (training, validation) = TrainingSampleRequirements.GetSplitCounts(count, testSplit);
                    return TrainingSampleRequirements.GetFailureReason(training, validation, batchSize);
                });
            foreach (var pool in regressionPools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 261021Claude: 回帰フォルダ名は保存先の事前検査と同じ定義から作る。
                string regressionModelName = ModelArtifactPaths.GetRegressionFolderName(pool.MineralName);
                string regressionOutputPath = Path.Combine(outputPath, regressionModelName);
                // 260930Codex: 選択した件数判定を、ログと学習で再利用する。
                string? skipReason = regressionSkipReasons[pool.Samples.Count];
                // 260930Codex: 省略した定量処理を、学習開始として通知しない。
                if (skipReason is null)
                    Log($"回帰モデル学習開始: {pool.MineralName}");
                reporter.BeginModel(regressionModelName);
                // 260930Codex: 共通の件数判定を再利用しつつ、すべての入力poolを検証する。
                modelMetrics.Add(TrainRegressionModel(pool, epochs, batchSize, patience, testSplit, regressionOutputPath, spectrumCache, reporter.ReportEpoch, cancellationToken,
                    skipReason));
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
            return DeepLearningTrainingResult.CreateCompleted(modelMetrics) with { ClassifierFeatures = classificationResult.ClassifierFeatures };
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
            CancellationToken cancellationToken,
            // 260930Codex: 共通の設定検証から、任意の定量処理を省略する理由を取得する。
            string? skipReason)
        {
            string op = $"regression:{trainingPool.MineralName}";
            var modelTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("model-train-start", $"op={op} epochs={epochs} batchSize={batchSize} patience={patience} samples={trainingPool.Samples.Count}");

            // 260930Codex: 分類で全スペクトルを検査済みなので、省略した定量処理では個別読込やUI通知を行わない。
            if (skipReason is not null)
            {
                var (trainingCount, validationCount) = TrainingSampleRequirements.GetSplitCounts(trainingPool.Samples.Count, testSplit);
                TensorFlowTrainingDebugLog.Write("model-train-end", $"op={op} status=skipped reason={skipReason} totalMs={modelTimer.ElapsedMilliseconds}");
                return new TrainingModelMetrics(
                    Target: "regression",
                    ModelFolderName: ModelArtifactPaths.GetRegressionFolderName(trainingPool.MineralName),
                    Status: "skipped",
                    MineralName: trainingPool.MineralName,
                    SkipReason: skipReason,
                    SampleCount: trainingPool.Samples.Count,
                    TrainingSampleCount: trainingCount,
                    ValidationSampleCount: validationCount,
                    ClassCount: null,
                    ComponentCount: trainingPool.EndmemberNames.Count,
                    RequestedEpochs: epochs,
                    CompletedEpochs: 0,
                    BestEpoch: null,
                    ValidationLossName: null,
                    ValidationLoss: null,
                    ValidationAccuracy: null,
                    ValidationMae: null,
                    SplitMethod: LegacySplitMethod);
            }


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

            // 260901Codex: 複数時間の学習では、要求した条件比率を暗黙に変えず失敗させる。
            // 260930Codex: 未使用のモードフラグを保持せず、メタデータの検証を維持する。
            ResolveSourceMetadataMode(trainingPool.Samples, op);
            // 260930Codex: 読み取り失敗によって、共通の事前検査で使った件数を暗黙に変えない。
            if ((int)allSpectra.shape[0] != trainingPool.Samples.Count)
                throw new InvalidDataException(
                    $"Training spectrum load failed for {op}: expected={trainingPool.Samples.Count}, loaded={(int)allSpectra.shape[0]}.");

            if (allSpectra.shape[0] == 0 || ComponentIndex == null)
            {
                // 260930Codex: ラベル欠損は読み込み失敗とし、件数に基づく任意の定量省略として扱わない。
                throw new InvalidDataException($"{trainingPool.MineralName}の定量用スペクトルまたは端成分情報を読み込めませんでした。");
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
            // 260907Codex: 組成・時間の計画と学習・検証分割は独立させ、全入力元で従来のseed 42の非層化分割を保持する。
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
            // 260606Codex: 実際のエポック数を、この学習で使ったEarlyStopping設定と並べて表示する。
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
                ModelFolderName: ModelArtifactPaths.GetRegressionFolderName(trainingPool.MineralName),
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
            // 261014Codex: 分類の学習条件を保存先とcallbackより前にまとめる。
            float classificationLearningRate,
            string outputPath,
            SpectrumDataLoader.NormalizedSpectrumCache? spectrumCache,
            Action<int>? reportEpoch,
            CancellationToken cancellationToken, bool captureClassifierFeatures = false)
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
            // 260607Codex: 並列分類読込の統計を記録し、規模を限定して読込の高速化を安全に比較できるようにする。
            TensorFlowTrainingDebugLog.Write("data-load-end", $"op={op} spectra={allSpectra.shape[0]} durationMs={dataTimer.ElapsedMilliseconds} inputSamples={loadStats.InputSamples} loadedSamples={loadStats.LoadedSamples} skippedSamples={loadStats.SkippedSamples} parallelDegree={loadStats.ParallelDegree}{FormatSpectrumCacheStats(spectrumCache)}");
            if (loadFailures.Count > 0)
                return ReportClassificationLoadFailures(
                    loadFailures,
                    loadStats,
                    op,
                    modelTimer,
                    cancellationToken);

            // 260901Codex: 複数時間の実行では入力の省略を認めないため、元の行と読込スペクトルの対応を保持する。
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
            // 260907Codex: 分割規則を計画処理から分離し、条件の網羅は新しい分割方針ではなく完了した計画で保証する。
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(
                allSpectra,
                labelsEncoded,
                testSize: testSplit,
                randomState: DeepLearningDataSplitter.DefaultRandomState);
            string splitMethod = LegacySplitMethod;
            TensorFlowTrainingDebugLog.Write("split-end", $"op={op} train={xTrain.shape[0]} test={xTest.shape[0]} durationMs={splitTimer.ElapsedMilliseconds}");
            Log($"訓練データ: {xTrain.shape[0]}");
            Log($"テストデータ: {xTest.shape[0]}\n");

            // 260930Codex: 学習処理を直接呼ぶ場合も、学習不能な分類器の構築前に停止する。
            string? insufficientReason = TrainingSampleRequirements.GetFailureReason((int)xTrain.shape[0], (int)xTest.shape[0], batchSize);
            if (insufficientReason is not null)
            {
                Log($"分類モデルを作成できません: {insufficientReason}");
                return DeepLearningTrainingResult.CreateNotCompleted(
                    DeepLearningTrainingFailureKind.InsufficientTrainingSamples, insufficientReason);
            }

            // 260902Codex: 全ファイル成功後にだけ TensorFlow の状態を初期化し、モデル構築へ進みます。
            TensorFlowTrainingDebugLog.Write("clear-session-before", $"op={op}");
            keras.backend.clear_session();
            TensorFlowTrainingDebugLog.Write("clear-session-after", $"op={op}");
            tf.set_random_seed(42);

            var buildTimer = Stopwatch.StartNew();
            var model = CreateClassificationModel(encoder.Count);

            model.compile(
                optimizer: keras.optimizers.Adam(learning_rate: classificationLearningRate),
                loss: CreateSparseCategoricalCrossentropy(),
                metrics: new[] { "accuracy" }
            );
            TensorFlowTrainingDebugLog.Write("build-compile-end", $"op={op} durationMs={buildTimer.ElapsedMilliseconds}");

            Log("訓練中...");
            var fitTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("fit-start", $"op={op}");
            // 260917Codex: 分類の分割を変えず、manifestの組成から許容する目標ラベルを求める。
            var policyClasses = encoder.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            var alternatives = classificationSamples.Select(row => MineralLabelPolicy.AlternativeIndex(row.MineralName, row.Sample.EndmemberFractions, policyClasses)).ToArray();
            // 260917Claude: TrainTestSplitClassificationと同じSeed付き分割で、代替ラベルとxTrain・xTestの行対応を保持する。
            var (trainIndices, testIndices) = DeepLearningDataSplitter.CreateLegacySplitIndices(alternatives.Length, testSplit, DeepLearningDataSplitter.DefaultRandomState);
            var fitResult = FitModelWithCancellation(model, xTrain, yTrain, xTest, yTest, batchSize, epochs, patience, op, _logAction, reportEpoch, cancellationToken,
                classificationLearningRate: classificationLearningRate,
                // 261014Codex: 任意引数の並び替え後も、各入力を名前付きで対応させる。
                trainingAlternatives: trainIndices.Select(index => alternatives[index]).ToArray(),
                validationAlternatives: testIndices.Select(index => alternatives[index]).ToArray());
            TensorFlowTrainingDebugLog.Write("fit-end", $"op={op} durationMs={fitTimer.ElapsedMilliseconds} requestedEpochs={fitResult.RequestedEpochs} trainedEpochs={fitResult.CompletedEpochs} lastEpoch={fitResult.LastEpoch} earlyStoppingMonitor={EarlyStoppingMonitor} patience={patience} finalEpochMetrics={TensorFlowTrainingDebugLog.Clean(fitResult.LastEpochMetrics)}");
            // 260606Codex: 実際のエポック数を、この学習で使ったEarlyStopping設定と並べて表示する。
            Log($"  学習エポック数: {fitResult.CompletedEpochs}/{fitResult.RequestedEpochs} (monitor={EarlyStoppingMonitor}, patience={patience})");
            // 260622Claude: 検証に test を渡しているので graph が best epoch で測った test loss/accuracy をそのまま使い、冗長な model.evaluate を省く。
            double validationLoss = fitResult.BestValidationLoss;
            double validationAccuracy = fitResult.BestValidationMetric;
            TensorFlowTrainingDebugLog.Write("evaluate-end", $"op={op} testLoss={FormatMetric(validationLoss)} testAccuracy={FormatMetric(validationAccuracy)} trainedEpochs={fitResult.CompletedEpochs} source=graph-bestval");

            Log($"検証 Loss: {validationLoss:F4}");
            Log($"検証 Accuracy: {validationAccuracy * 100:F2}%");
            cancellationToken.ThrowIfCancellationRequested();
            WriteClassificationSidecars(outputPath, encoder);
            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = Stopwatch.StartNew();
            TensorFlowTrainingDebugLog.Write("save-start", $"op={op}");
            model.save(outputPath);
            TensorFlowTrainingDebugLog.Write("save-end", $"op={op} durationMs={saveTimer.ElapsedMilliseconds}");
            cancellationToken.ThrowIfCancellationRequested();
            // 260626Claude: 予測時に自動適用するため、分類モデルフォルダにも前処理を記録する。
            preprocessing.WriteToModelFolder(outputPath);
            // 260917Codex: この付随ファイルは後の評価とは独立して、学習内容を記述する。
            MineralLabelPolicy.WriteTrainingMetadata(outputPath);
            // 260622Codex: 分類器自体を安全に保存してから、任意のオープンセット統計を保存する。
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
            }) with
            {
                // 261001Codex: このマネージド重みは直前に保存した分類器と一致し、ファイルシステムのハンドルを保持しない。
                ClassifierFeatures = captureClassifierFeatures ? DenseClassificationFeatureExtractor.FromModel(model) : null
            };
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
                // 260930Claude: 読めてはいるので、読み取り失敗と区別して原因を示します。
                // 260930Codex: モデルの学習や昇格の前に、空の学習入力を通知する。
                SpectrumDataLoader.ClassificationLoadFailureKind.NoSignal =>
                    "信号なし（全ゼロ、または低エネルギーマスク後に信号が残らない）",
                _ => throw new InvalidOperationException($"未対応の分類データ読込失敗です: {failure.Kind}")
            };

        // 260901Codex: 旧形式とmanifest対応の行が混在すると分割の意味が曖昧になるため、拒否する。
        // 260908Codex: スナップショット作成でも、通常学習と同じメタデータ完全性の検証を使う。
        internal static bool ResolveSourceMetadataMode(
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

        // 260622Codex: 分類学習の必須条件にせず、分類器とともに任意のオープンセット検知器を構築する。
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

