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
        // 260930Codex: Detect same-path replacements of regressor weights or component metadata.
        private string? _loadedFingerprint;
        private IModel? _model;
        private string[]? _componentNames;

        // 260930Codex: Standalone predictions verify; fresh batch services can hold their loaded revision.
        public MineralRegressionResult Predict(string regressionModelPath, float[] normalizedSpectrum, bool verifyModelFiles = true)
        {
            if (string.IsNullOrWhiteSpace(regressionModelPath) || !Directory.Exists(regressionModelPath))
                throw new DirectoryNotFoundException("回帰モデルフォルダが見つかりません。");

            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを回帰できます。");

            // 260606Claude: 回帰も分類と同じ専用スレッドへ集約し、TF を 1 native thread 起点に固定する。lock は worker 内の非競合 tripwire として残す。
            return TensorFlowExecutor.Run(() =>
            {
                lock (TensorFlowRuntimeGate.SyncRoot)
                {
                    return PredictCore(regressionModelPath, normalizedSpectrum, verifyModelFiles);
                }
            });
        }

        // 260606Claude: TF 本体(load/np.array/Apply/numpy/ToArray/dispose)。必ず専用スレッド上で実行し、戻すのは managed な結果のみ。異常時はキャッシュを破棄して再throw。
        private MineralRegressionResult PredictCore(string regressionModelPath, float[] normalizedSpectrum, bool verifyModelFiles)
        {
            try
            {
                EnsureModelLoaded(regressionModelPath, verifyModelFiles);
                string[] componentNames = _componentNames!;

                var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
                try
                {
                    // 260727Claude: 分類側 (260605Claude) と同じ理由で predict() をやめ Apply() にする。predict() は
                    //   呼び出しごとに DataAdapter/DataHandler/prefetch worker を構築・破棄するため、1スペクトルごとに
                    //   論理CPU数ぶんのスレッドチャーンと GC 圧が乗る。分類・回帰とも Dense のみ (Dropout/BatchNorm なし) なので
                    //   Apply(training: false) は predict() と数値的に同一。
                    var prediction = _model!.Apply(spectrumReshaped, training: false);
                    try
                    {
                        var predictionArray = prediction.numpy();
                        try
                        {
                            float[] rawValues = predictionArray.ToArray<float>();
                            return BuildResult(componentNames, rawValues);
                        }
                        finally
                        {
                            TensorFlowRuntimeGate.DisposeIfPossible(predictionArray);
                        }
                    }
                    finally
                    {
                        TensorFlowRuntimeGate.DisposeIfPossible(prediction);
                    }
                }
                finally
                {
                    // 260727Claude: 一時 Tensor の解放も分類側と同じ形に揃える (従来は未解放だった)。
                    TensorFlowRuntimeGate.DisposeIfPossible(spectrumReshaped);
                }
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

        // 260727Claude: 負値クリップ→合計1へ正規化。計算は従来と同一で、TF 本体から切り離して読みやすくした。
        private static MineralRegressionResult BuildResult(string[] componentNames, float[] rawValues)
        {
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

        private void EnsureModelLoaded(string regressionModelPath, bool verifyModelFiles)
        {
            // 260930Codex: Verify only the requested regressor; fresh batch services keep one revision for the batch.
            string? fingerprint = verifyModelFiles || _model is null || _loadedModelPath != regressionModelPath
                ? ModelArtifactFingerprint.Compute(regressionModelPath) : _loadedFingerprint;
            if (_model is not null && _loadedModelPath == regressionModelPath && _loadedFingerprint == fingerprint)
                return;

            string componentPath = Path.Combine(regressionModelPath, ModelArtifactPaths.ComponentIndexFileName);
            if (!File.Exists(componentPath))
                throw new FileNotFoundException("componentIndex.json が見つかりません。", componentPath);

            Dictionary<string, int>? componentIndex = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(componentPath));
            if (componentIndex is not { Count: > 0 })
                throw new InvalidDataException("componentIndex.json の内容を読み取れませんでした。");

            _componentNames = componentIndex.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
            _model = keras.models.load_model(regressionModelPath);
            _loadedModelPath = regressionModelPath;
            // 260930Codex: Record weights and component metadata as one cached revision.
            _loadedFingerprint = fingerprint;
        }
    }

    // 260522Codex: Holds the endmember ratios (already normalized to sum 1) in component-index order.
    internal sealed record MineralRegressionResult(IReadOnlyList<MineralComponentRatio> Components);

    // 260522Codex: One endmember component name with its predicted ratio in the 0-1 range.
    internal sealed record MineralComponentRatio(string ComponentName, float Ratio);
}
