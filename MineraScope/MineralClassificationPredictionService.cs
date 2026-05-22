using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.Binding;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: Runs the saved AllMinerals classification model against an already normalized spectrum.
    internal sealed class MineralClassificationPredictionService
    {
        // 260522Codex: load_model rebuilds the whole graph, so cache it per model path instead of reloading every click.
        private readonly object _gate = new();
        private string? _loadedClassificationPath;
        private IModel? _classificationModel;
        private Dictionary<string, int>? _encoder;

        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                throw new DirectoryNotFoundException("モデルフォルダが見つかりません。");

            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = Path.Combine(modelPath, "AllMinerals_Classification");

            // 260522Codex: Serialize predictions so overlapping clicks never race the shared model/graph.
            lock (_gate)
            {
                try
                {
                    EnsureModelLoaded(classificationPath);

                    var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
                    var prediction = _classificationModel!.predict(spectrumReshaped);
                    float[] probabilities = prediction.numpy().ToArray<float>();

                    var orderedResults = _encoder!
                        .Where(item => item.Value >= 0 && item.Value < probabilities.Length)
                        .Select(item => new MineralClassificationProbability(item.Key, probabilities[item.Value]))
                        .OrderByDescending(item => item.Confidence)
                        .ToList();

                    if (orderedResults.Count == 0)
                        throw new InvalidDataException("分類モデルの出力と labelEncoder.json が対応していません。");

                    var best = orderedResults[0];
                    return new MineralClassificationPredictionResult(best.MineralName, best.Confidence, orderedResults);
                }
                catch
                {
                    // 260522Codex: Drop the cache so a session cleared elsewhere (training/RunPrediction) reloads on the next click.
                    _loadedClassificationPath = null;
                    _classificationModel = null;
                    _encoder = null;
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

            keras.backend.clear_session();
            _classificationModel = keras.models.load_model(classificationPath);
            _encoder = encoder;
            _loadedClassificationPath = classificationPath;
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
