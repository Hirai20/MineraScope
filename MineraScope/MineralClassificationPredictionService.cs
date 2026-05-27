using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: Runs the saved AllMinerals classification model against an already normalized spectrum.
    internal sealed class MineralClassificationPredictionService
    {
        private string? _loadedClassificationPath;
        private IModel? _classificationModel;
        private string[]? _labelNames;

        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum)
        {
            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = GetClassificationPath(modelPath);

            // 260527Codex: Serialize prediction and dispose transient TensorFlow/NumSharp objects after copying managed results.
            return RunLockedWithCacheReset(() =>
            {
                EnsureModelLoaded(classificationPath);

                var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
                try
                {
                    var prediction = _classificationModel!.predict(spectrumReshaped);
                    try
                    {
                        var predictionArray = prediction.numpy();
                        try
                        {
                            return BuildPredictionResult(predictionArray.ToArray<float>());
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
            });
        }

        public int[] PredictTop1Batch(string modelPath, float[,] batch, CancellationToken cancellationToken)
        {
            int rows = batch.GetLength(0);
            if (batch.GetLength(1) != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            if (rows == 0)
                return [];

            string classificationPath = GetClassificationPath(modelPath);

            // 260527Codex: Batch map prediction is the hot path, so release native arrays promptly between chunks.
            return RunLockedWithCacheReset(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureModelLoaded(classificationPath);

                var batchArray = np.array(batch);
                try
                {
                    var prediction = _classificationModel!.predict(batchArray);
                    try
                    {
                        var predictionArray = prediction.numpy();
                        try
                        {
                            return BuildTop1BatchResult(predictionArray.ToArray<float>(), rows);
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
            });
        }

        public string[] GetLabelNames(string modelPath)
        {
            string classificationPath = GetClassificationPath(modelPath);

            return RunLockedWithCacheReset(() =>
            {
                EnsureModelLoaded(classificationPath);
                return (string[])_labelNames!.Clone();
            });
        }

        // 260527Codex: Repeated full-map prediction can leave TensorFlow runtime state stale; reset it between map runs.
        public void ReleaseModel()
        {
            lock (TensorFlowRuntimeGate.SyncRoot)
            {
                ResetModelCache();
                try
                {
                    keras.backend.clear_session();
                }
                catch
                {
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
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

        private T RunLockedWithCacheReset<T>(Func<T> action)
        {
            lock (TensorFlowRuntimeGate.SyncRoot)
            {
                try
                {
                    return action();
                }
                catch
                {
                    ResetModelCache();
                    throw;
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

            ResetModelCache();
            keras.backend.clear_session();
            _classificationModel = keras.models.load_model(classificationPath);
            _labelNames = labelNames;
            _loadedClassificationPath = classificationPath;
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
            DisposeIfPossible(_classificationModel);
            _loadedClassificationPath = null;
            _classificationModel = null;
            _labelNames = null;
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
