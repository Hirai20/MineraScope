using System.Text.Json;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;

namespace MineraScope
{
    // 260622Codex: Stores and evaluates the open-set detector built from Dense(64) embeddings of the classifier.
    internal sealed class MineralUnknownDetector
    {
        public const string FileName = "unknownDetector.json";
        public const string UnknownDisplayName = "Unknown";

        private const int CurrentVersion = 1;
        private const double DefaultThresholdQuantile = 0.99d;
        private const double BaseRidgeScale = 1e-3d;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly double[][] _means;
        private readonly double[][] _precision;

        private MineralUnknownDetector(
            int embeddingDim,
            double threshold,
            double thresholdQuantile,
            double ridge,
            double[][] means,
            double[][] precision)
        {
            EmbeddingDim = embeddingDim;
            Threshold = threshold;
            ThresholdQuantile = thresholdQuantile;
            Ridge = ridge;
            _means = means;
            _precision = precision;
        }

        public int EmbeddingDim { get; }

        public double Threshold { get; private set; }

        public double ThresholdQuantile { get; }

        public double Ridge { get; }

        public int LabelCount => _means.Length;

        public static MineralUnknownDetector Build(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            NDArray xValidation,
            CancellationToken cancellationToken)
        {
            var extractor = DenseClassificationFeatureExtractor.FromWeights(model.get_weights());
            float[,] trainEmbeddings = extractor.TransformFlat(xTrain.ToArray<float>(), (int)xTrain.shape[0]);
            int[] trainLabels = yTrain.ToArray<int>();
            var detector = BuildFromEmbeddings(trainEmbeddings, trainLabels, DefaultThresholdQuantile, cancellationToken);

            float[,] calibrationEmbeddings = xValidation.shape[0] > 0
                ? extractor.TransformFlat(xValidation.ToArray<float>(), (int)xValidation.shape[0])
                : trainEmbeddings;
            detector.Threshold = CalculateThreshold(detector, calibrationEmbeddings, DefaultThresholdQuantile, cancellationToken);
            return detector;
        }

        public static bool TryLoad(string classificationPath, int expectedLabelCount, int expectedEmbeddingDim, out MineralUnknownDetector? detector, out string message)
        {
            detector = null;
            string path = Path.Combine(classificationPath, FileName);
            if (!File.Exists(path))
            {
                message = $"{FileName} was not found.";
                return false;
            }

            try
            {
                UnknownDetectorArtifact? artifact = JsonSerializer.Deserialize<UnknownDetectorArtifact>(File.ReadAllText(path));
                if (artifact is null)
                {
                    message = $"{FileName} could not be read.";
                    return false;
                }

                if (artifact.Version != CurrentVersion)
                {
                    message = $"{FileName} version is unsupported.";
                    return false;
                }

                if (artifact.EmbeddingDim != expectedEmbeddingDim || artifact.LabelCount != expectedLabelCount)
                {
                    message = $"{FileName} does not match the loaded classifier.";
                    return false;
                }

                if (!IsMatrix(artifact.Means, expectedLabelCount, expectedEmbeddingDim) || !IsMatrix(artifact.Precision, expectedEmbeddingDim, expectedEmbeddingDim))
                {
                    message = $"{FileName} has invalid matrix dimensions.";
                    return false;
                }

                detector = new MineralUnknownDetector(
                    artifact.EmbeddingDim,
                    artifact.Threshold,
                    artifact.ThresholdQuantile,
                    artifact.Ridge,
                    artifact.Means,
                    artifact.Precision);
                message = "loaded";
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message = ex.Message;
                return false;
            }
        }

        public void Save(string classificationPath)
        {
            Directory.CreateDirectory(classificationPath);
            var artifact = new UnknownDetectorArtifact
            {
                Version = CurrentVersion,
                SpectrumLength = SpectrumDataLoader.SpectrumLength,
                EmbeddingDim = EmbeddingDim,
                EmbeddingSource = "Dense(64,relu)",
                Distance = "shared_covariance_mahalanobis_squared",
                Ridge = Ridge,
                ThresholdQuantile = ThresholdQuantile,
                Threshold = Threshold,
                Means = _means,
                Precision = _precision,
                LabelCount = LabelCount,
                CalibrationSource = "classification_test_split"
            };
            File.WriteAllText(Path.Combine(classificationPath, FileName), JsonSerializer.Serialize(artifact, JsonOptions));
        }

        public MineralUnknownDetectionResult Evaluate(float[] embedding)
        {
            if (embedding.Length != EmbeddingDim)
                throw new ArgumentException($"{EmbeddingDim} values are required.", nameof(embedding));

            double bestScore = double.PositiveInfinity;
            int bestLabel = 0;
            var diff = new double[EmbeddingDim];
            for (int label = 0; label < _means.Length; label++)
            {
                double score = Score(embedding, _means[label], diff);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLabel = label;
                }
            }

            return new MineralUnknownDetectionResult(bestScore > Threshold, (float)bestScore, (float)Threshold, bestLabel);
        }

        public MineralUnknownDetectionResult Evaluate(float[,] embeddings, int row)
            => Evaluate(embeddings, row, new double[EmbeddingDim]);

        public MineralUnknownDetectionResult Evaluate(float[,] embeddings, int row, double[] diff)
        {
            if (embeddings.GetLength(1) != EmbeddingDim)
                throw new ArgumentException($"{EmbeddingDim} columns are required.", nameof(embeddings));
            if (diff.Length != EmbeddingDim)
                throw new ArgumentException($"{EmbeddingDim} values are required.", nameof(diff));

            double bestScore = double.PositiveInfinity;
            int bestLabel = 0;
            for (int label = 0; label < _means.Length; label++)
            {
                for (int i = 0; i < EmbeddingDim; i++)
                    diff[i] = embeddings[row, i] - _means[label][i];

                double score = Score(diff);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLabel = label;
                }
            }

            return new MineralUnknownDetectionResult(bestScore > Threshold, (float)bestScore, (float)Threshold, bestLabel);
        }

        private static MineralUnknownDetector BuildFromEmbeddings(float[,] embeddings, int[] labels, double thresholdQuantile, CancellationToken cancellationToken)
        {
            int rows = embeddings.GetLength(0);
            int dim = embeddings.GetLength(1);
            if (rows == 0 || labels.Length != rows)
                throw new InvalidDataException("Unknown detector requires matching training embeddings and labels.");

            int labelCount = labels.Max() + 1;
            var means = CreateMatrix(labelCount, dim);
            var counts = new int[labelCount];
            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int label = labels[row];
                if (label < 0 || label >= labelCount)
                    throw new InvalidDataException("Training labels are not contiguous.");

                counts[label]++;
                for (int i = 0; i < dim; i++)
                    means[label][i] += embeddings[row, i];
            }

            for (int label = 0; label < labelCount; label++)
            {
                if (counts[label] == 0)
                    throw new InvalidDataException("Every classification label must have at least one training sample.");

                for (int i = 0; i < dim; i++)
                    means[label][i] /= counts[label];
            }

            var covariance = CreateMatrix(dim, dim);
            var diff = new double[dim];
            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int label = labels[row];
                for (int i = 0; i < dim; i++)
                    diff[i] = embeddings[row, i] - means[label][i];

                for (int i = 0; i < dim; i++)
                    for (int j = i; j < dim; j++)
                        covariance[i][j] += diff[i] * diff[j];
            }

            double denominator = Math.Max(1, rows - labelCount);
            double diagonalMean = 0d;
            for (int i = 0; i < dim; i++)
            {
                for (int j = i; j < dim; j++)
                {
                    covariance[i][j] /= denominator;
                    covariance[j][i] = covariance[i][j];
                }
                diagonalMean += covariance[i][i];
            }

            diagonalMean /= dim;
            double ridge = Math.Max(1e-9d, Math.Abs(diagonalMean) * BaseRidgeScale);
            for (int i = 0; i < dim; i++)
                covariance[i][i] += ridge;

            double[][] precision = Invert(covariance);
            return new MineralUnknownDetector(dim, 0d, thresholdQuantile, ridge, means, precision);
        }

        private static double CalculateThreshold(MineralUnknownDetector detector, float[,] calibrationEmbeddings, double quantile, CancellationToken cancellationToken)
        {
            int rows = calibrationEmbeddings.GetLength(0);
            if (rows == 0)
                throw new InvalidDataException("Unknown detector calibration requires at least one embedding.");

            var scores = new double[rows];
            var diff = new double[detector.EmbeddingDim];
            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scores[row] = detector.Evaluate(calibrationEmbeddings, row, diff).Score;
            }

            Array.Sort(scores);
            double threshold = QuantileSorted(scores, quantile);
            if (double.IsNaN(threshold) || double.IsInfinity(threshold))
                throw new InvalidDataException("Unknown detector threshold is not finite.");
            return threshold;
        }

        private double Score(float[] embedding, double[] mean, double[] diff)
        {
            for (int i = 0; i < EmbeddingDim; i++)
                diff[i] = embedding[i] - mean[i];
            return Score(diff);
        }

        private double Score(double[] diff)
        {
            double score = 0d;
            for (int i = 0; i < EmbeddingDim; i++)
            {
                double rowSum = 0d;
                for (int j = 0; j < EmbeddingDim; j++)
                    rowSum += _precision[i][j] * diff[j];
                score += diff[i] * rowSum;
            }
            return Math.Max(0d, score);
        }

        private static double QuantileSorted(double[] sorted, double quantile)
        {
            if (sorted.Length == 1)
                return sorted[0];

            double position = Math.Clamp(quantile, 0d, 1d) * (sorted.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return sorted[lower];

            double fraction = position - lower;
            return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
        }

        private static double[][] Invert(double[][] matrix)
        {
            int n = matrix.Length;
            var augmented = new double[n, n * 2];
            for (int row = 0; row < n; row++)
            {
                if (matrix[row].Length != n)
                    throw new InvalidDataException("Matrix must be square.");

                for (int column = 0; column < n; column++)
                    augmented[row, column] = matrix[row][column];
                augmented[row, n + row] = 1d;
            }

            for (int column = 0; column < n; column++)
            {
                int pivotRow = column;
                double pivotSize = Math.Abs(augmented[column, column]);
                for (int row = column + 1; row < n; row++)
                {
                    double size = Math.Abs(augmented[row, column]);
                    if (size > pivotSize)
                    {
                        pivotSize = size;
                        pivotRow = row;
                    }
                }

                if (pivotSize < 1e-12d)
                    throw new InvalidDataException("Unknown detector covariance matrix is singular.");

                if (pivotRow != column)
                    SwapRows(augmented, pivotRow, column);

                double pivot = augmented[column, column];
                for (int j = 0; j < n * 2; j++)
                    augmented[column, j] /= pivot;

                for (int row = 0; row < n; row++)
                {
                    if (row == column)
                        continue;

                    double factor = augmented[row, column];
                    if (factor == 0d)
                        continue;

                    for (int j = 0; j < n * 2; j++)
                        augmented[row, j] -= factor * augmented[column, j];
                }
            }

            var inverse = CreateMatrix(n, n);
            for (int row = 0; row < n; row++)
                for (int column = 0; column < n; column++)
                    inverse[row][column] = augmented[row, n + column];
            return inverse;
        }

        private static void SwapRows(double[,] matrix, int a, int b)
        {
            int columns = matrix.GetLength(1);
            for (int column = 0; column < columns; column++)
            {
                (matrix[a, column], matrix[b, column]) = (matrix[b, column], matrix[a, column]);
            }
        }

        private static double[][] CreateMatrix(int rows, int columns)
        {
            var matrix = new double[rows][];
            for (int row = 0; row < rows; row++)
                matrix[row] = new double[columns];
            return matrix;
        }

        private static bool IsMatrix(double[][]? matrix, int rows, int columns)
            => matrix is not null
               && matrix.Length == rows
               && matrix.All(row => row is not null && row.Length == columns);

        private sealed class UnknownDetectorArtifact
        {
            public int Version { get; set; }

            public int SpectrumLength { get; set; }

            public int EmbeddingDim { get; set; }

            public string EmbeddingSource { get; set; } = string.Empty;

            public string Distance { get; set; } = string.Empty;

            public double Ridge { get; set; }

            public double ThresholdQuantile { get; set; }

            public double Threshold { get; set; }

            public double[][] Means { get; set; } = [];

            public double[][] Precision { get; set; } = [];

            public int LabelCount { get; set; }

            public string CalibrationSource { get; set; } = string.Empty;
        }
    }

    // 260622Codex: Carries the open-set score while keeping the closed-set top-1 prediction available for context.
    internal readonly record struct MineralUnknownDetectionResult(
        bool IsUnknown,
        float Score,
        float Threshold,
        int NearestLabelId);
}
