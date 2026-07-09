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

        private const int CurrentVersion = 2;
        private const double DefaultThresholdQuantile = 1.0d;
        private const double DefaultThresholdRadiusExpansion = 1.15d;
        private const double BaseRidgeScale = 1e-3d;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly double[][] _means;
        private readonly double[][] _precision;
        private readonly double[] _thresholds;

        private MineralUnknownDetector(
            int embeddingDim,
            double threshold,
            double thresholdQuantile,
            double thresholdRadiusExpansion,
            double ridge,
            double[][] means,
            double[][] precision,
            double[] thresholds)
        {
            EmbeddingDim = embeddingDim;
            Threshold = threshold;
            ThresholdQuantile = thresholdQuantile;
            ThresholdRadiusExpansion = thresholdRadiusExpansion;
            Ridge = ridge;
            _means = means;
            _precision = precision;
            _thresholds = thresholds;
        }

        public int EmbeddingDim { get; }

        public double Threshold { get; private set; }

        public double ThresholdQuantile { get; }

        public double ThresholdRadiusExpansion { get; }

        public double Ridge { get; }

        public int LabelCount => _means.Length;

        // 260622Claude: radiusExpansion は UI の「既知と認める距離の倍率」から渡る。不正値 (1 未満や NaN) は既定値へフォールバックする。
        public static MineralUnknownDetector Build(
            Model model,
            NDArray xTrain,
            NDArray yTrain,
            NDArray xValidation,
            NDArray yValidation,
            double radiusExpansion,
            CancellationToken cancellationToken)
        {
            double resolvedRadiusExpansion = radiusExpansion >= 1d ? radiusExpansion : DefaultThresholdRadiusExpansion;
            var extractor = DenseClassificationFeatureExtractor.FromWeights(model.get_weights());
            float[,] trainEmbeddings = extractor.TransformFlat(xTrain.ToArray<float>(), (int)xTrain.shape[0]);
            int[] trainLabels = yTrain.ToArray<int>();
            var detector = BuildFromEmbeddings(
                trainEmbeddings,
                trainLabels,
                DefaultThresholdQuantile,
                resolvedRadiusExpansion,
                cancellationToken);

            float[,] calibrationEmbeddings = xValidation.shape[0] > 0
                ? extractor.TransformFlat(xValidation.ToArray<float>(), (int)xValidation.shape[0])
                : trainEmbeddings;
            int[] calibrationLabels = xValidation.shape[0] > 0 ? yValidation.ToArray<int>() : trainLabels;
            detector.SetThresholds(CalculateThresholds(
                detector,
                trainEmbeddings,
                trainLabels,
                calibrationEmbeddings,
                calibrationLabels,
                DefaultThresholdQuantile,
                resolvedRadiusExpansion,
                cancellationToken));
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

                if (artifact.Version < 1 || artifact.Version > CurrentVersion)
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

                double[] thresholds = IsVector(artifact.Thresholds, expectedLabelCount)
                    ? artifact.Thresholds
                    : Enumerable.Repeat(artifact.Threshold, expectedLabelCount).ToArray();
                detector = new MineralUnknownDetector(
                    artifact.EmbeddingDim,
                    artifact.Threshold,
                    artifact.ThresholdQuantile,
                    artifact.ThresholdRadiusExpansion > 0d ? artifact.ThresholdRadiusExpansion : 1d,
                    artifact.Ridge,
                    artifact.Means,
                    artifact.Precision,
                    thresholds);
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
                ThresholdMode = "per_label_known_max_squared_distance_with_radius_margin",
                ThresholdRadiusExpansion = ThresholdRadiusExpansion,
                Threshold = Threshold,
                Thresholds = _thresholds,
                Means = _means,
                Precision = _precision,
                LabelCount = LabelCount,
                CalibrationSource = "classification_train_and_test_split"
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

            double threshold = _thresholds[bestLabel];
            return new MineralUnknownDetectionResult(bestScore > threshold, (float)bestScore, (float)threshold, bestLabel);
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

            double threshold = _thresholds[bestLabel];
            return new MineralUnknownDetectionResult(bestScore > threshold, (float)bestScore, (float)threshold, bestLabel);
        }

        private static MineralUnknownDetector BuildFromEmbeddings(
            float[,] embeddings,
            int[] labels,
            double thresholdQuantile,
            double thresholdRadiusExpansion,
            CancellationToken cancellationToken)
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
            return new MineralUnknownDetector(
                dim,
                0d,
                thresholdQuantile,
                thresholdRadiusExpansion,
                ridge,
                means,
                precision,
                new double[labelCount]);
        }

        private void SetThresholds(double[] thresholds)
        {
            if (thresholds.Length != LabelCount)
                throw new InvalidDataException("Unknown detector threshold count does not match label count.");

            Array.Copy(thresholds, _thresholds, thresholds.Length);
            Threshold = thresholds.Max();
        }

        private static double[] CalculateThresholds(
            MineralUnknownDetector detector,
            float[,] trainEmbeddings,
            int[] trainLabels,
            float[,] calibrationEmbeddings,
            int[] calibrationLabels,
            double quantile,
            double radiusExpansion,
            CancellationToken cancellationToken)
        {
            if (trainEmbeddings.GetLength(0) == 0)
                throw new InvalidDataException("Unknown detector calibration requires at least one embedding.");

            var scoresByLabel = new List<double>[detector.LabelCount];
            for (int label = 0; label < scoresByLabel.Length; label++)
                scoresByLabel[label] = [];

            var diff = new double[detector.EmbeddingDim];
            AddKnownScores(detector, trainEmbeddings, trainLabels, scoresByLabel, diff, cancellationToken);
            if (!ReferenceEquals(trainEmbeddings, calibrationEmbeddings))
                AddKnownScores(detector, calibrationEmbeddings, calibrationLabels, scoresByLabel, diff, cancellationToken);

            var thresholds = new double[detector.LabelCount];
            for (int label = 0; label < thresholds.Length; label++)
            {
                if (scoresByLabel[label].Count == 0)
                    throw new InvalidDataException("Every classification label must have at least one calibration sample.");

                scoresByLabel[label].Sort();
                double threshold = QuantileSorted(scoresByLabel[label], quantile);
                thresholds[label] = ExpandSquaredDistance(threshold, radiusExpansion);
                if (double.IsNaN(thresholds[label]) || double.IsInfinity(thresholds[label]))
                    throw new InvalidDataException("Unknown detector threshold is not finite.");
            }

            return thresholds;
        }

        // 260622Codex: Calibrate each label from its true known samples so broad solid-solution classes get broader accepted regions.
        private static void AddKnownScores(
            MineralUnknownDetector detector,
            float[,] embeddings,
            int[] labels,
            List<double>[] scoresByLabel,
            double[] diff,
            CancellationToken cancellationToken)
        {
            int rows = embeddings.GetLength(0);
            if (labels.Length != rows)
                throw new InvalidDataException("Unknown detector calibration requires matching embeddings and labels.");

            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int label = labels[row];
                if (label < 0 || label >= detector.LabelCount)
                    throw new InvalidDataException("Calibration labels are not contiguous.");

                for (int i = 0; i < detector.EmbeddingDim; i++)
                    diff[i] = embeddings[row, i] - detector._means[label][i];
                scoresByLabel[label].Add(detector.Score(diff));
            }
        }

        private static double ExpandSquaredDistance(double squaredDistance, double radiusExpansion)
        {
            double expansion = Math.Max(1d, radiusExpansion);
            return squaredDistance * expansion * expansion;
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

        private static double QuantileSorted(IReadOnlyList<double> sorted, double quantile)
        {
            if (sorted.Count == 1)
                return sorted[0];

            double position = Math.Clamp(quantile, 0d, 1d) * (sorted.Count - 1);
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

        private static bool IsVector(double[]? vector, int length)
            => vector is not null && vector.Length == length;

        private sealed class UnknownDetectorArtifact
        {
            public int Version { get; set; }

            public int SpectrumLength { get; set; }

            public int EmbeddingDim { get; set; }

            public string EmbeddingSource { get; set; } = string.Empty;

            public string Distance { get; set; } = string.Empty;

            public double Ridge { get; set; }

            public double ThresholdQuantile { get; set; }

            public string ThresholdMode { get; set; } = string.Empty;

            public double ThresholdRadiusExpansion { get; set; }

            public double Threshold { get; set; }

            public double[] Thresholds { get; set; } = [];

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
