using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MineraScope
{
    // 260906Codex: Carry observed model metrics from DeepLearning to the promotion workflow without mixing persistence into TensorFlow code.
    internal sealed record TrainingModelMetrics(
        string Target,
        string ModelFolderName,
        string Status,
        string? MineralName,
        string? SkipReason,
        int SampleCount,
        int? TrainingSampleCount,
        int? ValidationSampleCount,
        int? ClassCount,
        int? ComponentCount,
        int? RequestedEpochs,
        int? CompletedEpochs,
        int? BestEpoch,
        string? ValidationLossName,
        double? ValidationLoss,
        double? ValidationAccuracy,
        double? ValidationMae,
        string SplitMethod,
        // 260930Codex: Keep strict source-label validation accuracy beside overlap-aware accuracy.
        double? LegacyValidationAccuracy = null);

    // 260906Codex: Keep training settings separate from per-model values because one model-creation run shares them.
    internal sealed record TrainingResultsSpecification(
        int RequestedEpochs,
        int BatchSize,
        int EarlyStoppingPatience,
        float ValidationSplit,
        int SplitSeed,
        string EarlyStoppingMonitor,
        // 260930Codex: Existing callers can adopt the ordinary default; explicit rates are recorded by the workflow.
        float ClassificationLearningRate = ModelTrainingSettings.DefaultClassificationLearningRate,
        // 261119Codex: 公開版の既存引数順を維持し、実効Dropout条件を任意項目として渡す。
        ClassificationDropoutSettings? ClassificationDropout = null);

    // 260906Codex: Persist one human-readable, versioned record beside the completed model without changing prediction artifacts.
    internal static class TrainingResultsWriter
    {
        private const string TrainingEngine = "graph-session-v1";
        private const string MetricSource = "best-epoch-validation";
        private const string EvaluationNote = "検証データはearly stoppingの監視と最良重み選択に使用しており、独立したテストデータではありません。";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static void Write(
            string modelRootFolder,
            TrainingResultsSpecification specification,
            IReadOnlyList<TrainingModelMetrics> modelMetrics)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelRootFolder);
            ArgumentNullException.ThrowIfNull(specification);
            ArgumentNullException.ThrowIfNull(modelMetrics);
            if (modelMetrics.Count == 0)
                throw new ArgumentException("At least one completed model metric is required.", nameof(modelMetrics));

            var document = new TrainingResultsDocument(
                SchemaVersion: 1,
                CreatedUtc: DateTimeOffset.UtcNow,
                TrainingEngine: TrainingEngine,
                Evaluation: new EvaluationDocument(
                    MetricSource: MetricSource,
                    EarlyStoppingMonitor: specification.EarlyStoppingMonitor,
                    UsedForEarlyStopping: true,
                    Note: EvaluationNote),
                Settings: new TrainingSettingsDocument(
                    RequestedEpochs: specification.RequestedEpochs,
                    BatchSize: specification.BatchSize,
                    EarlyStoppingPatience: specification.EarlyStoppingPatience,
                    ValidationSplit: specification.ValidationSplit,
                    SplitSeed: specification.SplitSeed,
                    ClassificationLearningRate: specification.ClassificationLearningRate,
                    // 261119Codex: 通常学習で実際に使ったDropout条件を記録する。
                    ClassificationDropout: specification.ClassificationDropout),
                Models: modelMetrics.Select(ToDocument).ToArray());

            Directory.CreateDirectory(modelRootFolder);
            string outputPath = Path.Combine(modelRootFolder, ModelArtifactPaths.TrainingResultsFileName);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(document, JsonOptions));
        }

        private static TrainingModelDocument ToDocument(TrainingModelMetrics metric) =>
            new(
                metric.Target,
                metric.ModelFolderName,
                metric.Status,
                metric.MineralName,
                metric.SkipReason,
                metric.SampleCount,
                metric.TrainingSampleCount,
                metric.ValidationSampleCount,
                metric.ClassCount,
                metric.ComponentCount,
                metric.RequestedEpochs,
                metric.CompletedEpochs,
                metric.BestEpoch,
                metric.ValidationLossName,
                ToFiniteOrNull(metric.ValidationLoss),
                ToFiniteOrNull(metric.ValidationAccuracy),
                ToFiniteOrNull(metric.ValidationMae),
                metric.SplitMethod,
                ToFiniteOrNull(metric.LegacyValidationAccuracy));

        private static double? ToFiniteOrNull(double? value) =>
            value is double finite && double.IsFinite(finite) ? finite : null;

        private sealed record TrainingResultsDocument(
            int SchemaVersion,
            DateTimeOffset CreatedUtc,
            string TrainingEngine,
            EvaluationDocument Evaluation,
            TrainingSettingsDocument Settings,
            IReadOnlyList<TrainingModelDocument> Models);

        private sealed record EvaluationDocument(
            string MetricSource,
            string EarlyStoppingMonitor,
            bool UsedForEarlyStopping,
            string Note);

        private sealed record TrainingSettingsDocument(
            int RequestedEpochs,
            int BatchSize,
            int EarlyStoppingPatience,
            float ValidationSplit,
            int SplitSeed,
            // 260930Codex: Make the effective classification rate visible in saved training results.
            float ClassificationLearningRate,
            // 261119Codex: 条件がない場合は旧形式と同じく項目を省略する。
            [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
            ClassificationDropoutSettings? ClassificationDropout);

        private sealed record TrainingModelDocument(
            string Target,
            string ModelFolderName,
            string Status,
            string? MineralName,
            string? SkipReason,
            int SampleCount,
            int? TrainingSampleCount,
            int? ValidationSampleCount,
            int? ClassCount,
            int? ComponentCount,
            int? RequestedEpochs,
            int? CompletedEpochs,
            int? BestEpoch,
            string? ValidationLossName,
            double? ValidationLoss,
            double? ValidationAccuracy,
            double? ValidationMae,
            string SplitMethod,
            // 260930Codex: Keep strict source-label validation accuracy beside overlap-aware accuracy.
        double? LegacyValidationAccuracy = null);
    }
}
