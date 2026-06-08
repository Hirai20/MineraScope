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
                        return BuildPredictionResult(probabilities);
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
                        return BuildTop1BatchResult(probabilities, rows);
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

        private MineralClassificationPredictionResult BuildPredictionResult(float[] probabilities)
        {
            if (probabilities.Length != _labelNames!.Length)
                throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

            var orderedResults = new List<MineralClassificationProbability>(probabilities.Length);
            for (int i = 0; i < probabilities.Length; i++)
                orderedResults.Add(new MineralClassificationProbability(_labelNames[i], probabilities[i]));
            orderedResults.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            var best = orderedResults[0];
            return new MineralClassificationPredictionResult(best.MineralName, best.Confidence, orderedResults);
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
            _loadedClassificationPath = classificationPath;
            _modelLoadManagedThreadId = Environment.CurrentManagedThreadId;
            _modelLoadNativeThreadId = TensorFlowPredictionDebugLog.CurrentNativeThreadId;
            TensorFlowPredictionDebugLog.Write("model-load-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()} labels={labelNames.Length}");
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
        IReadOnlyList<MineralClassificationProbability> Probabilities);

    // 260521Codex: Holds one mineral label probability returned by the classification model.
    internal sealed record MineralClassificationProbability(string MineralName, float Confidence);
}
