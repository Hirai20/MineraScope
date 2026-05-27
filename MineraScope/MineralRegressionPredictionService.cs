using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: Runs a saved endmember-ratio regression model against an already normalized spectrum.
    internal sealed class MineralRegressionPredictionService
    {
        // 260522Codex: Cache the loaded model + component order per path so a batch reuses them across files.
        private string? _loadedModelPath;
        private IModel? _model;
        private string[]? _componentNames;

        public MineralRegressionResult Predict(string regressionModelPath, float[] normalizedSpectrum)
        {
            if (string.IsNullOrWhiteSpace(regressionModelPath) || !Directory.Exists(regressionModelPath))
                throw new DirectoryNotFoundException("回帰モデルフォルダが見つかりません。");

            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを回帰できます。");

            lock (TensorFlowRuntimeGate.SyncRoot)
            {
                try
                {
                    EnsureModelLoaded(regressionModelPath);
                    string[] componentNames = _componentNames!;

                    var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
                    var prediction = _model!.predict(spectrumReshaped);
                    float[] rawValues = prediction.numpy().ToArray<float>();

                    float[] clipped = new float[componentNames.Length];
                    float sum = 0f;
                    for (int i = 0; i < componentNames.Length; i++)
                    {
                        clipped[i] = Math.Max(rawValues[i], 0f);
                        sum += clipped[i];
                    }

                    var components = new List<MineralComponentRatio>(componentNames.Length);
                    for (int i = 0; i < componentNames.Length; i++)
                    {
                        float ratio = sum > 0f ? clipped[i] / sum : 0f;
                        components.Add(new MineralComponentRatio(componentNames[i], ratio));
                    }

                    return new MineralRegressionResult(components);
                }
                catch
                {
                    // 260522Codex: Drop the cache so a session cleared elsewhere reloads on the next call.
                    _loadedModelPath = null;
                    _model = null;
                    _componentNames = null;
                    throw;
                }
            }
        }

        private void EnsureModelLoaded(string regressionModelPath)
        {
            if (_model is not null && _loadedModelPath == regressionModelPath)
                return;

            string componentPath = Path.Combine(regressionModelPath, "componentIndex.json");
            if (!File.Exists(componentPath))
                throw new FileNotFoundException("componentIndex.json が見つかりません。", componentPath);

            Dictionary<string, int>? componentIndex = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(componentPath));
            if (componentIndex is not { Count: > 0 })
                throw new InvalidDataException("componentIndex.json の内容を読み取れませんでした。");

            _componentNames = componentIndex.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            _model = keras.models.load_model(regressionModelPath);
            _loadedModelPath = regressionModelPath;
        }
    }

    // 260522Codex: Holds the endmember ratios (already normalized to sum 1) in component-index order.
    internal sealed record MineralRegressionResult(IReadOnlyList<MineralComponentRatio> Components);

    // 260522Codex: One endmember component name with its predicted ratio in the 0-1 range.
    internal sealed record MineralComponentRatio(string ComponentName, float Ratio);
}
