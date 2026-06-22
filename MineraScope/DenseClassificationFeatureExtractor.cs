using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;

namespace MineraScope
{
    // 260622Codex: Reuses the saved dense classifier weights as a lightweight CPU feature extractor for open-set scoring.
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
                throw new InvalidDataException("Classification dense weight shapes do not match the expected 2048 -> hidden -> embedding layout.");

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

            var hidden = new float[HiddenDim];
            var embedding = new float[EmbeddingDim];
            TransformHidden(input, 0, hidden);
            TransformEmbedding(hidden, embedding);
            return embedding;
        }

        public float[,] Transform(float[,] inputs, int rows)
        {
            if (inputs.GetLength(1) != InputDim)
                throw new ArgumentException($"{InputDim} columns are required.", nameof(inputs));
            if (rows < 0 || rows > inputs.GetLength(0))
                throw new ArgumentOutOfRangeException(nameof(rows));

            var result = new float[rows, EmbeddingDim];
            var hidden = new float[HiddenDim];
            for (int row = 0; row < rows; row++)
            {
                TransformHidden(inputs, row, hidden);
                TransformEmbedding(hidden, result, row);
            }
            return result;
        }

        public float[,] TransformFlat(float[] inputs, int rows)
        {
            if (inputs.Length != (long)rows * InputDim)
                throw new ArgumentException("Input length does not match row count.", nameof(inputs));

            var result = new float[rows, EmbeddingDim];
            var hidden = new float[HiddenDim];
            for (int row = 0; row < rows; row++)
            {
                TransformHidden(inputs, row * InputDim, hidden);
                TransformEmbedding(hidden, result, row);
            }
            return result;
        }

        // 260622Codex: Keep the dense-layer math in one place so training and prediction use identical CPU embeddings.
        private void TransformHidden(float[] input, int offset, float[] hidden)
        {
            for (int h = 0; h < HiddenDim; h++)
            {
                double sum = _b1[h];
                for (int i = 0; i < InputDim; i++)
                    sum += input[offset + i] * _w1[i * HiddenDim + h];
                hidden[h] = sum > 0d ? (float)sum : 0f;
            }
        }

        private void TransformHidden(float[,] inputs, int row, float[] hidden)
        {
            for (int h = 0; h < HiddenDim; h++)
            {
                double sum = _b1[h];
                for (int i = 0; i < InputDim; i++)
                    sum += inputs[row, i] * _w1[i * HiddenDim + h];
                hidden[h] = sum > 0d ? (float)sum : 0f;
            }
        }

        private void TransformEmbedding(float[] hidden, float[] embedding)
        {
            for (int e = 0; e < EmbeddingDim; e++)
            {
                double sum = _b2[e];
                for (int h = 0; h < HiddenDim; h++)
                    sum += hidden[h] * _w2[h * EmbeddingDim + e];
                embedding[e] = sum > 0d ? (float)sum : 0f;
            }
        }

        private void TransformEmbedding(float[] hidden, float[,] result, int row)
        {
            for (int e = 0; e < EmbeddingDim; e++)
            {
                double sum = _b2[e];
                for (int h = 0; h < HiddenDim; h++)
                    sum += hidden[h] * _w2[h * EmbeddingDim + e];
                result[row, e] = sum > 0d ? (float)sum : 0f;
            }
        }
    }
}
