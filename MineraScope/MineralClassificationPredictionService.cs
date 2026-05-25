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
        // 260522Codex: load_model rebuilds the whole graph, so cache it per model path instead of reloading every click.
        private readonly object _gate = new();
        private string? _loadedClassificationPath;
        private IModel? _classificationModel;

        // 260526Claude: labelEncoder の value を出力 index として並べた鉱物名。クリックとマップで唯一の名前解決にする。
        private string[]? _labelNames;

        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum)
        {
            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = GetClassificationPath(modelPath);

            // 260522Codex: Serialize predictions so overlapping clicks never race the shared model/graph.
            return RunLockedWithCacheReset(() =>
            {
                EnsureModelLoaded(classificationPath);

                var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
                var prediction = _classificationModel!.predict(spectrumReshaped);
                float[] probabilities = prediction.numpy().ToArray<float>();

                // 260526Claude: 名前解決を _labelNames に統一し、全ラベルを信頼度降順で返す（クリック詳細用）。
                if (probabilities.Length != _labelNames!.Length)
                    throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

                var orderedResults = new List<MineralClassificationProbability>(probabilities.Length);
                for (int i = 0; i < probabilities.Length; i++)
                    orderedResults.Add(new MineralClassificationProbability(_labelNames[i], probabilities[i]));
                orderedResults.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

                var best = orderedResults[0];
                return new MineralClassificationPredictionResult(best.MineralName, best.Confidence, orderedResults);
            });
        }

        // 260526Claude: 全ブロックをチャンク batch で分類し、各行の Top-1 を返す。全確率行列は保持しない。
        // 260526Codex: 現在のマップ表示で使う Top-1 labelId だけを返し、未使用の信頼度保持をやめます。
        public int[] PredictTop1Batch(string modelPath, float[,] batch, CancellationToken cancellationToken)
        {
            int rows = batch.GetLength(0);
            if (batch.GetLength(1) != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            if (rows == 0)
                return [];

            string classificationPath = GetClassificationPath(modelPath);

            // 260526Claude: predict はスレッドセーフでないため、クリックと同じ _gate で直列化する。
            return RunLockedWithCacheReset(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureModelLoaded(classificationPath);

                var prediction = _classificationModel!.predict(np.array(batch));
                float[] probabilities = prediction.numpy().ToArray<float>();
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
            });
        }

        // 260526Claude: 出力 index → 鉱物名の配列を返す（result のラベル表・凡例用）。
        public string[] GetLabelNames(string modelPath)
        {
            string classificationPath = GetClassificationPath(modelPath);

            return RunLockedWithCacheReset(() =>
            {
                EnsureModelLoaded(classificationPath);
                return (string[])_labelNames!.Clone();
            });
        }

        // 260526Codex: モデルフォルダ検証と分類モデルパス作成の重複を 1 か所へ寄せます。
        private static string GetClassificationPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                throw new DirectoryNotFoundException("モデルフォルダが見つかりません。");

            return Path.Combine(modelPath, "AllMinerals_Classification");
        }

        // 260526Codex: TensorFlow モデルへのアクセス直列化と例外時のキャッシュ破棄を共通化します。
        private T RunLockedWithCacheReset<T>(Func<T> action)
        {
            lock (_gate)
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

            // 260526Claude: 出力 index → 名前を一度だけ構築・検証し、推論経路で共有する。
            string[] labelNames = BuildLabelNames(encoder);

            keras.backend.clear_session();
            _classificationModel = keras.models.load_model(classificationPath);
            _labelNames = labelNames;
            _loadedClassificationPath = classificationPath;
        }

        // 260526Claude: encoder(name→index) を index 順の名前配列へ。負値・重複・欠番(0..count-1 の連続でない)を検出してエラーにする。
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

        // 260526Claude: Drop the cache so a session cleared elsewhere (training/RunPrediction) reloads on the next call.
        private void ResetModelCache()
        {
            _loadedClassificationPath = null;
            _classificationModel = null;
            _labelNames = null;
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
