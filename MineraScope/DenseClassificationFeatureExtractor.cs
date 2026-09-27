using System.Numerics;
using System.Runtime.InteropServices;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;

namespace MineraScope
{
    // 260622Codex: 保存済みの全結合分類器の重みを、開集合スコア用の軽量な CPU 特徴抽出器として再利用する。
    internal sealed class DenseClassificationFeatureExtractor
    {
        private readonly float[] _w1;
        private readonly float[] _b1;
        private readonly float[] _w2;
        private readonly float[] _b2;

        private DenseClassificationFeatureExtractor(
            int inputDim,
            int hiddenDim,
            int embeddingDim,
            float[] w1,
            float[] b1,
            float[] w2,
            float[] b2)
        {
            InputDim = inputDim;
            HiddenDim = hiddenDim;
            EmbeddingDim = embeddingDim;
            _w1 = w1;
            _b1 = b1;
            _w2 = w2;
            _b2 = b2;
        }

        public int InputDim { get; }

        public int HiddenDim { get; }

        public int EmbeddingDim { get; }

        public static DenseClassificationFeatureExtractor FromModel(IModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var method = model.GetType().GetMethod("get_weights", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Loaded classification model does not expose get_weights().");
            object? rawWeights = method.Invoke(model, null);
            if (rawWeights is IEnumerable<NDArray> weights)
                return FromWeights(weights.ToList());

            throw new InvalidOperationException("Loaded classification model returned unreadable weights.");
        }

        public static DenseClassificationFeatureExtractor FromWeights(IReadOnlyList<NDArray> weights)
        {
            ArgumentNullException.ThrowIfNull(weights);

            if (weights.Count < 4)
                throw new InvalidDataException("Classification model does not have enough dense weights for embedding extraction.");

            int inputDim = (int)weights[0].shape[0];
            int hiddenDim = (int)weights[0].shape[1];
            int hiddenBiasDim = (int)weights[1].shape[0];
            int secondInputDim = (int)weights[2].shape[0];
            int embeddingDim = (int)weights[2].shape[1];
            int embeddingBiasDim = (int)weights[3].shape[0];

            if (inputDim != SpectrumDataLoader.SpectrumLength || hiddenBiasDim != hiddenDim || secondInputDim != hiddenDim || embeddingBiasDim != embeddingDim)
                // 260803Codex: 診断文の入力長も共通軸由来にし、固定 2048 の重複を残しません。
                throw new InvalidDataException($"Classification dense weight shapes do not match the expected {SpectrumDataLoader.SpectrumLength} -> hidden -> embedding layout.");

            return new DenseClassificationFeatureExtractor(
                inputDim,
                hiddenDim,
                embeddingDim,
                weights[0].ToArray<float>(),
                weights[1].ToArray<float>(),
                weights[2].ToArray<float>(),
                weights[3].ToArray<float>());
        }

        public float[] Transform(float[] input)
        {
            if (input.Length != InputDim)
                throw new ArgumentException($"{InputDim} values are required.", nameof(input));

            var embedding = new float[EmbeddingDim];
            TransformRow(input, new double[HiddenDim], embedding);
            return embedding;
        }

        public float[,] Transform(float[,] inputs, int rows)
        {
            if (inputs.GetLength(1) != InputDim)
                throw new ArgumentException($"{InputDim} columns are required.", nameof(inputs));
            if (rows < 0 || rows > inputs.GetLength(0))
                throw new ArgumentOutOfRangeException(nameof(rows));

            var result = new float[rows, EmbeddingDim];
            // 260810Claude: 行同士は独立な純粋計算なので並列化する。TF 呼び出しを含まないため TensorFlowExecutor 専用スレッド上でも安全。
            Parallel.For(0, rows, () => new double[HiddenDim], (row, _, hidden) =>
            {
                TransformRow(
                    MemoryMarshal.CreateReadOnlySpan(ref inputs[row, 0], InputDim),
                    hidden,
                    MemoryMarshal.CreateSpan(ref result[row, 0], EmbeddingDim));
                return hidden;
            }, _ => { });
            return result;
        }

        public float[,] TransformFlat(float[] inputs, int rows)
        {
            if (inputs.Length != (long)rows * InputDim)
                throw new ArgumentException("Input length does not match row count.", nameof(inputs));

            var result = new float[rows, EmbeddingDim];
            Parallel.For(0, rows, () => new double[HiddenDim], (row, _, hidden) =>
            {
                TransformRow(
                    inputs.AsSpan(row * InputDim, InputDim),
                    hidden,
                    MemoryMarshal.CreateSpan(ref result[row, 0], EmbeddingDim));
                return hidden;
            }, _ => { });
            return result;
        }

        // 260622Codex: 全結合層の計算を 1 か所へ集約し、学習と推論が同じ CPU 埋め込みを使うようにする。
        // 260824Claude: 行列積を「入力 1 チャンネルぶんの重み行 (連続した HiddenDim 個) へ足し込む」形へ組み替えている。
        //   旧実装は出力 h が外側・入力 i が内側で _w1[i * HiddenDim + h] を 512 バイトおきに読んでいたため、
        //   (1) 連続していないので SIMD が使えず、(2) 値が 0 の入力を飛ばしても掛け算 1 個ぶんしか省けなかった。
        //   入力を外側に出すと重みが連続アクセスになって SIMD が効き、0 の入力は 1 回の判定で 128 個の積和ごと飛ばせる。
        //   転置コピーを持てば内積の形のままでも連続にできるが、足し込み順が変わって旧実装との一致を保証できない。
        //   この形なら足し込み順 (入力チャンネル順) が旧実装と同じなので、埋め込みの値はビット単位で一致する。
        //   実測 (Release, 1 画素あたり): 旧 396us → 順序入替 265us → 0 スキップ 107us → SIMD 77us → 行並列 2.9us。
        private void TransformRow(ReadOnlySpan<float> input, Span<double> hidden, Span<float> embedding)
        {
            for (int h = 0; h < HiddenDim; h++)
                hidden[h] = _b1[h];
            for (int i = 0; i < InputDim; i++)
            {
                float value = input[i];
                if (value == 0f) continue;
                AddScaled(_w1.AsSpan(i * HiddenDim, HiddenDim), value, hidden);
            }
            Span<double> accumulator = stackalloc double[EmbeddingDim];
            for (int e = 0; e < EmbeddingDim; e++)
                accumulator[e] = _b2[e];
            for (int h = 0; h < HiddenDim; h++)
            {
                // 260810Claude: 層の境界は従来どおり ReLU 後に float へ丸め、Keras の float32 層と同じ刻みを保つ。
                float value = hidden[h] > 0d ? (float)hidden[h] : 0f;
                if (value == 0f) continue;
                AddScaled(_w2.AsSpan(h * EmbeddingDim, EmbeddingDim), value, accumulator);
            }
            for (int e = 0; e < EmbeddingDim; e++)
                embedding[e] = accumulator[e] > 0d ? (float)accumulator[e] : 0f;
        }

        // 260810Claude: destination += weights * scale。旧実装と同じく「float どうしの積を double へ足す」順序を守るため、
        //   積は float のまま作り、広げてから加算する。累算順 (入力チャンネル順) も同じなので結果はビット一致する。
        private static void AddScaled(ReadOnlySpan<float> weights, float scale, Span<double> destination)
        {
            int floatWidth = Vector<float>.Count;
            int doubleWidth = Vector<double>.Count;
            var scaleVector = new Vector<float>(scale);
            int i = 0;
            for (; i <= weights.Length - floatWidth; i += floatWidth)
            {
                Vector.Widen(new Vector<float>(weights.Slice(i, floatWidth)) * scaleVector, out Vector<double> low, out Vector<double> high);
                (new Vector<double>(destination.Slice(i, doubleWidth)) + low).CopyTo(destination.Slice(i, doubleWidth));
                (new Vector<double>(destination.Slice(i + doubleWidth, doubleWidth)) + high).CopyTo(destination.Slice(i + doubleWidth, doubleWidth));
            }
            for (; i < weights.Length; i++)
                destination[i] += weights[i] * scale;
        }
    }
}
