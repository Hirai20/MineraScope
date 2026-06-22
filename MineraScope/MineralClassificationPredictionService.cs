using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: Runs the saved AllMinerals classification model against an already normalized spectrum.
    // 260608Claude: predict()→Apply 解決後の per-call 詳細トレース(predict-single/batch-*, labels-*, model-cache-hit)を撤去し、tf-lock/model-load 等の診断要所だけ残す。
    internal sealed class MineralClassificationPredictionService
    {
        private string? _loadedClassificationPath;
        private IModel? _classificationModel;
        private string[]? _labelNames;
        // 260622Codex: Optional open-set detector state is cached with the loaded classifier model.
        private DenseClassificationFeatureExtractor? _featureExtractor;
        private MineralUnknownDetector? _unknownDetector;
        // 260604Codex: Track the thread that loaded the cached model so TF.NET ThreadLocal context moves are visible.
        private int _modelLoadManagedThreadId;
        private uint _modelLoadNativeThreadId;

        // 260606Claude: 同期版(RunPrediction バッチ用)。検証と dispatch は PredictAsync に集約し、呼び出し元 BG スレッドをブロックして結果を得る。
        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum)
            => PredictAsync(modelPath, normalizedSpectrum).GetAwaiter().GetResult();

        // 260606Claude: UI 単発分類用。Task.Run の代わりに専用スレッドへ投げ、UI を塞がず await できるようにする。
        public Task<MineralClassificationPredictionResult> PredictAsync(string modelPath, float[] normalizedSpectrum)
        {
            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = GetClassificationPath(modelPath);
            return TensorFlowExecutor.RunAsync(() => RunLockedWithCacheReset(() => PredictCore(classificationPath, normalizedSpectrum), "single"));
        }

        // 260606Claude: TF 本体(load/np.array/Apply/numpy/ToArray/dispose)。必ず専用スレッド上で実行し、戻すのは managed な結果のみ。
        private MineralClassificationPredictionResult PredictCore(string classificationPath, float[] normalizedSpectrum)
        {
            EnsureModelLoaded(classificationPath);

            var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
            try
            {
                // 260605Claude: predict() は DataAdapter/DataHandler/prefetch worker を毎回構築してスレッドと GC をリークさせる。素のフォワードパスである Apply() に切り替える。
                var prediction = _classificationModel!.Apply(spectrumReshaped, training: false);
                try
                {
                    var predictionArray = prediction.numpy();
                    try
                    {
                        var probabilities = predictionArray.ToArray<float>();
                        // 260622Codex: Keep the softmax top-1 while attaching the optional unknown-distance verdict.
                        MineralUnknownDetectionResult? unknown = _unknownDetector is not null && _featureExtractor is not null
                            ? _unknownDetector.Evaluate(_featureExtractor.Transform(normalizedSpectrum))
                            : null;
                        return BuildPredictionResult(probabilities, unknown);
                    }
                    finally
                    {
                        DisposeIfPossible(predictionArray);
                    }
                }
                finally
                {
                    DisposeIfPossible(prediction);
                }
            }
            finally
            {
                DisposeIfPossible(spectrumReshaped);
            }
        }

        public int[] PredictTop1Batch(string modelPath, float[,] batch, CancellationToken cancellationToken)
        {
            int rows = batch.GetLength(0);
            if (batch.GetLength(1) != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            if (rows == 0)
                return [];

            string classificationPath = GetClassificationPath(modelPath);
            // 260606Claude: マップの chunk 推論も専用スレッドへ集約する。workflow スレッドはここでブロックし、合間の PTS 読み取り/正規化は並列のまま。
            return TensorFlowExecutor.Run(() => RunLockedWithCacheReset(() => PredictTop1BatchCore(classificationPath, batch, rows, cancellationToken), "batch"));
        }

        // 260606Claude: バッチ TF 本体。必ず専用スレッド上で実行し、戻すのは top1 ラベル index の int[] のみ。
        private int[] PredictTop1BatchCore(string classificationPath, float[,] batch, int rows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelLoaded(classificationPath);

            var batchArray = np.array(batch);
            try
            {
                // 260605Claude: predict() の臨時設備リークを避けるため、マップ側も Apply() で素のフォワードパスにする。
                var prediction = _classificationModel!.Apply(batchArray, training: false);
                try
                {
                    var predictionArray = prediction.numpy();
                    try
                    {
                        var probabilities = predictionArray.ToArray<float>();
                        var results = BuildTop1BatchResult(probabilities, rows);
                        ApplyUnknownDetector(batch, rows, results);
                        return results;
                    }
                    finally
                    {
                        DisposeIfPossible(predictionArray);
                    }
                }
                finally
                {
                    DisposeIfPossible(prediction);
                }
            }
            finally
            {
                DisposeIfPossible(batchArray);
            }
        }

        public string[] GetLabelNames(string modelPath)
        {
            string classificationPath = GetClassificationPath(modelPath);
            // 260606Claude: load_model がここで走り得るので、ラベル取得も専用スレッドに乗せてモデル初期化スレッドを 1 本へ固定する。
            return TensorFlowExecutor.Run(() => RunLockedWithCacheReset(() => GetLabelNamesCore(classificationPath), "labels"));
        }

        private string[] GetLabelNamesCore(string classificationPath)
        {
            EnsureModelLoaded(classificationPath);
            return (string[])_labelNames!.Clone();
        }

        // 260622Codex: Preserve closed-set details and add nullable open-set metadata for callers that understand it.
        private MineralClassificationPredictionResult BuildPredictionResult(float[] probabilities, MineralUnknownDetectionResult? unknown)
        {
            if (probabilities.Length != _labelNames!.Length)
                throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

            var orderedResults = new List<MineralClassificationProbability>(probabilities.Length);
            for (int i = 0; i < probabilities.Length; i++)
                orderedResults.Add(new MineralClassificationProbability(_labelNames[i], probabilities[i]));
            orderedResults.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            var best = orderedResults[0];
            string? nearestKnown = unknown is { } detection
                && detection.NearestLabelId >= 0
                && detection.NearestLabelId < _labelNames.Length
                    ? _labelNames[detection.NearestLabelId]
                    : null;
            return new MineralClassificationPredictionResult(
                best.MineralName,
                best.Confidence,
                orderedResults,
                unknown?.IsUnknown ?? false,
                unknown?.Score,
                unknown?.Threshold,
                nearestKnown);
        }

        private int[] BuildTop1BatchResult(float[] probabilities, int rows)
        {
            int labelCount = _labelNames!.Length;
            if (probabilities.Length != (long)rows * labelCount)
                throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

            var results = new int[rows];
            for (int r = 0; r < rows; r++)
            {
                int offset = r * labelCount;
                int best = 0;
                float bestValue = probabilities[offset];
                for (int j = 1; j < labelCount; j++)
                {
                    float value = probabilities[offset + j];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = j;
                    }
                }
                results[r] = best;
            }
            return results;
        }

        // 260622Codex: PTS map batches keep the fast top-1 path and overwrite only rows outside the known embedding distribution.
        private void ApplyUnknownDetector(float[,] batch, int rows, int[] results)
        {
            if (_unknownDetector is null || _featureExtractor is null)
                return;

            var embeddings = _featureExtractor.Transform(batch, rows);
            var diff = new double[_unknownDetector.EmbeddingDim];
            for (int row = 0; row < rows; row++)
            {
                var detection = _unknownDetector.Evaluate(embeddings, row, diff);
                if (detection.IsUnknown)
                    results[row] = PtsClassificationMapResult.UnknownLabelId;
            }
        }

        private static string GetClassificationPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                throw new DirectoryNotFoundException("モデルフォルダが見つかりません。");

            return Path.Combine(modelPath, "AllMinerals_Classification");
        }

        // 260606Claude: 呼び出し元は必ず TensorFlowExecutor 経由なので action は専用スレッド上で動く。lock は単一スレッドで非競合となり、段階移行の tripwire として残す(第2パッチで撤去予定)。
        private T RunLockedWithCacheReset<T>(Func<T> action, string operation)
        {
            // 260604Codex: The lock proves serialization but not thread affinity, so log both wait and enter.
            TensorFlowPredictionDebugLog.Write("tf-lock-wait", $"operation={operation}");
            lock (TensorFlowRuntimeGate.SyncRoot)
            {
                TensorFlowPredictionDebugLog.Write("tf-lock-enter", $"operation={operation}");
                try
                {
                    return action();
                }
                catch
                {
                    TensorFlowPredictionDebugLog.Write("tf-managed-exception", $"operation={operation}");
                    ResetModelCache();
                    throw;
                }
                finally
                {
                    TensorFlowPredictionDebugLog.Write("tf-lock-exit", $"operation={operation}");
                }
            }
        }

        private void EnsureModelLoaded(string classificationPath)
        {
            if (_classificationModel is not null && _loadedClassificationPath == classificationPath)
                return;

            if (!Directory.Exists(classificationPath))
                throw new DirectoryNotFoundException("分類モデル AllMinerals_Classification が見つかりません。");

            string encoderPath = Path.Combine(classificationPath, "labelEncoder.json");
            if (!File.Exists(encoderPath))
                throw new FileNotFoundException("labelEncoder.json が見つかりません。", encoderPath);

            Dictionary<string, int>? encoder = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(encoderPath));
            if (encoder is not { Count: > 0 })
                throw new InvalidDataException("labelEncoder.json の内容を読み取れませんでした。");

            string[] labelNames = BuildLabelNames(encoder);

            // 260604Codex: clear_session/load_model are native-state boundaries and must be visible in the crash log.
            TensorFlowPredictionDebugLog.Write("model-load-start", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()}");
            ResetModelCache();
            TensorFlowPredictionDebugLog.Write("model-clear-session-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            keras.backend.clear_session();
            TensorFlowPredictionDebugLog.Write("model-clear-session-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            TensorFlowPredictionDebugLog.Write("model-load-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            _classificationModel = keras.models.load_model(classificationPath);
            _labelNames = labelNames;
            // 260622Codex: Load optional open-set metadata after the classifier weights are available.
            LoadUnknownDetector(classificationPath, labelNames.Length);
            _loadedClassificationPath = classificationPath;
            _modelLoadManagedThreadId = Environment.CurrentManagedThreadId;
            _modelLoadNativeThreadId = TensorFlowPredictionDebugLog.CurrentNativeThreadId;
            TensorFlowPredictionDebugLog.Write("model-load-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()} labels={labelNames.Length}");
        }

        // 260622Codex: Unknown detection is optional, so stale or missing artifacts disable only the open-set score.
        private void LoadUnknownDetector(string classificationPath, int labelCount)
        {
            _featureExtractor = null;
            _unknownDetector = null;
            try
            {
                _featureExtractor = DenseClassificationFeatureExtractor.FromModel(_classificationModel!);
                if (MineralUnknownDetector.TryLoad(classificationPath, labelCount, _featureExtractor.EmbeddingDim, out var detector, out string message))
                {
                    _unknownDetector = detector;
                    TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=loaded threshold={detector!.Threshold:G9}");
                    return;
                }

                TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=disabled reason={TensorFlowPredictionDebugLog.Clean(message)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _featureExtractor = null;
                _unknownDetector = null;
                TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=disabled reason={TensorFlowPredictionDebugLog.Clean(ex.Message)}");
            }
        }

        private static string[] BuildLabelNames(Dictionary<string, int> encoder)
        {
            int count = encoder.Count;
            var names = new string[count];
            var assigned = new bool[count];
            foreach (var (name, index) in encoder)
            {
                if (index < 0 || index >= count || assigned[index])
                    throw new InvalidDataException("labelEncoder.json のラベル index が不正です（負値・重複・欠番）。");

                names[index] = name;
                assigned[index] = true;
            }
            return names;
        }

        private void ResetModelCache()
        {
            // 260604Codex: Cache drops can invalidate the model/thread relationship, so log them even when no model exists.
            TensorFlowPredictionDebugLog.Write("model-cache-reset", $"{GetModelDebugInfo()}");
            DisposeIfPossible(_classificationModel);
            _loadedClassificationPath = null;
            _classificationModel = null;
            _labelNames = null;
            // 260622Codex: Keep optional open-set cache lifetime identical to the classifier cache.
            _featureExtractor = null;
            _unknownDetector = null;
            _modelLoadManagedThreadId = 0;
            _modelLoadNativeThreadId = 0;
        }

        // 260604Codex: Keep model identity compact in the flush-backed TensorFlow diagnostics.
        private string GetModelDebugInfo()
        {
            int modelHash = _classificationModel is null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_classificationModel);
            return $"modelHash={modelHash} loadedManagedThread={_modelLoadManagedThreadId} loadedNativeThread={_modelLoadNativeThreadId} loadedPath={TensorFlowPredictionDebugLog.Clean(_loadedClassificationPath ?? string.Empty)}";
        }

        // 260527Codex: TensorFlow.NET/NumSharp values are not all statically IDisposable here, so release them opportunistically.
        private static void DisposeIfPossible(object? value)
        {
            try
            {
                if (value is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
            }
        }
    }

    // 260521Codex: Holds the top classification result and all label probabilities for UI display.
    internal sealed record MineralClassificationPredictionResult(
        string PredictedMineral,
        float Confidence,
        IReadOnlyList<MineralClassificationProbability> Probabilities,
        bool IsUnknown = false,
        float? UnknownScore = null,
        float? UnknownThreshold = null,
        string? NearestKnownMineral = null)
    {
        // 260622Codex: UI and exports can show Unknown without losing the closed-set top-1 candidate.
        public string DisplayMineralName => IsUnknown ? MineralUnknownDetector.UnknownDisplayName : PredictedMineral;
    }

    // 260521Codex: Holds one mineral label probability returned by the classification model.
    internal sealed record MineralClassificationProbability(string MineralName, float Confidence);
}
