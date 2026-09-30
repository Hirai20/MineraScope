using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260514Codex: manifest から選んだ学習入力と正式保存先を workflow の plan にまとめます。
    internal sealed record ModelTrainingPlan(
        string ModelOutputFolder,
        IReadOnlyList<SpectrumTrainingPool> TrainingPools,
        ModelTrainingSettings Settings,
        DetectorProfile DetectorProfile,
        // 260619Codex: Store overwrite consent in the plan so workflow callers cannot replace models accidentally.
        bool AllowOverwriteExistingModel,
        // 260901Codex: Explicit multi-time requests carry strict counts and reproducibility metadata into promotion.
        TrainingDataProvenanceSpecification? TrainingDataProvenance = null);

    // 260901Codex: 正式モデルとして利用可能になったかを、学習処理の正常終了とは分けて表します。
    internal enum ModelTrainingStatus
    {
        Promoted,
        NotPromoted
    }

    // 260902Codex: UI が想定内の未昇格理由ごとに状態と案内を変えられるよう、理由を型で渡します。
    internal enum ModelTrainingFailureKind
    {
        TrainingDataLoadFailed
    }

    // 260901Codex: 想定内の未昇格理由と保存済み成果物を例外にせず UI へ返します。
    internal sealed record ModelTrainingResult
    {
        private ModelTrainingResult(
            ModelTrainingStatus status,
            ModelTrainingFailureKind? failureKind,
            string? failureReason,
            string? preservedArtifactsFolder)
        {
            Status = status;
            FailureKind = failureKind;
            FailureReason = failureReason;
            PreservedArtifactsFolder = preservedArtifactsFolder;
        }

        public ModelTrainingStatus Status { get; }

        public ModelTrainingFailureKind? FailureKind { get; }

        public string? FailureReason { get; }

        public string? PreservedArtifactsFolder { get; }

        public static ModelTrainingResult CreatePromoted() =>
            new(
                ModelTrainingStatus.Promoted,
                failureKind: null,
                failureReason: null,
                preservedArtifactsFolder: null);

        public static ModelTrainingResult CreateNotPromoted(
            ModelTrainingFailureKind failureKind,
            string failureReason,
            string? preservedArtifactsFolder = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            if (preservedArtifactsFolder is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(preservedArtifactsFolder);

            return new(ModelTrainingStatus.NotPromoted, failureKind, failureReason, preservedArtifactsFolder);
        }
    }

    // 260514Codex: モデル学習の検証、tmp 保存先の管理、成功時の正式フォルダ昇格を Form から分離します。
    internal sealed class ModelTrainingWorkflow
    {
        // 260619Codex: Share the overwrite guard message between preflight and promotion-time checks.
        private const string OverwriteNotAllowedMessage = "同じ名前のモデルが既に存在するため、上書き許可なしでは作成できません。";

        private readonly DeepLearning _deepLearning;
        private readonly Action<string> _logAction;

        // 260514Codex: workflow 実行に必要な学習本体とログ出力を constructor で固定します。
        public ModelTrainingWorkflow(DeepLearning deepLearning, Action<string> logAction)
        {
            ArgumentNullException.ThrowIfNull(deepLearning);
            ArgumentNullException.ThrowIfNull(logAction);

            _deepLearning = deepLearning;
            _logAction = logAction;
        }

        // 260514Codex: 学習対象は pool workflow が抽出した Completed spectrum だけにします。
        public ModelTrainingPlan CreatePlan(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            // 260619Codex: UI callers pass true only after the user confirms overwriting an existing model.
            bool allowOverwriteExistingModel = false)
        {
            string modelRootFolder = string.IsNullOrWhiteSpace(request.Paths.ModelOutputFolder)
                ? DefaultStoragePaths.ModelsFolder
                : request.Paths.ModelOutputFolder;
            string modelOutputFolder = Path.Combine(modelRootFolder, request.ModelName);

            return new ModelTrainingPlan(
                modelOutputFolder,
                trainingPools,
                request.Training,
                request.SemEdxCondition.GetDetectorProfile(),
                allowOverwriteExistingModel,
                // 260907Codex: A composition/time schedule needs provenance even when the legacy explicit-allocation list is empty.
                request.SpectrumTimeAllocations.Count == 0 && !request.UsesCompositionTimePlan
                    ? null
                    : new TrainingDataProvenanceSpecification(
                        request.GetEffectiveSpectrumTimeAllocations(),
                        TrainingDataProvenanceSpecification.DefaultSelectionSeed,
                        TrainingDataProvenanceSpecification.DefaultSplitSeed,
                        request.Training.ValidationSplit));
        }

        // 260514Codex: target 未設定や pool 不足は学習開始前に止めます。
        public string? Validate(ModelTrainingPlan plan)
        {
            // 260930Codex: Reject invalid learning rates before training starts.
            if (!float.IsFinite(plan.Settings.LearningRate) || plan.Settings.LearningRate <= 0)
                return "分類の学習率は0より大きい有限の値にしてください。";
            if (plan.TrainingPools.Count == 0)
                return "モデル作成対象の鉱物が選択されていないか、学習可能な spectrum がありません。";

            if (plan.TrainingPools.Any(pool => pool.Samples.Count == 0))
                return "学習可能な spectrum がない鉱物があります。";

            return ValidateTrainingSources(plan);
        }

        // 260901Codex: Reject partial metadata and any multi-time imbalance before TensorFlow starts.
        private static string? ValidateTrainingSources(ModelTrainingPlan plan)
        {
            SpectrumTrainingSample[] samples = plan.TrainingPools
                .SelectMany(pool => pool.Samples)
                .ToArray();
            int sourceCount = samples.Count(sample => sample.Source is not null);
            if (sourceCount > 0 && sourceCount != samples.Length)
                return $"学習元情報が一部のスペクトルにありません（全{samples.Length}件、情報あり{sourceCount}件）。";

            var specification = plan.TrainingDataProvenance;
            if (specification is null)
                return null;
            if (sourceCount == 0)
                return "測定時間を混在させる学習データに、manifest由来の学習元情報がありません。";

            string? allocationError = ValidateTimeAllocations(specification.TimeAllocations);
            if (allocationError is not null)
                return allocationError;
            if (!float.IsFinite(specification.ValidationSplit)
                || specification.ValidationSplit <= 0
                || specification.ValidationSplit >= 1)
                return "検証データ割合は0より大きく1より小さい値にしてください。";

            var duplicatePath = samples
                .GroupBy(sample => sample.FilePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicatePath is not null)
                return $"同じスペクトルファイルが重複して選択されています: {duplicatePath.Key}";

            foreach (var pool in plan.TrainingPools)
            {
                foreach (var sample in pool.Samples)
                {
                    var source = sample.Source!;
                    if (!double.IsFinite(source.LiveTime) || source.LiveTime <= 0
                        || string.IsNullOrWhiteSpace(source.ConditionKey)
                        || source.SimulationId < 0
                        || string.IsNullOrWhiteSpace(source.ManifestPath))
                        return $"学習元情報が不正です: {sample.FilePath}";
                    // 260902Codex: spectrum の存在と可読性は分類ローダーで一括検査し、全失敗をまとめて報告します。
                    if (!File.Exists(source.ManifestPath))
                        return $"学習元manifestが見つかりません: {source.ManifestPath}";
                }

                foreach (var allocation in specification.TimeAllocations)
                {
                    SpectrumTrainingSample[] allocationSamples = pool.Samples
                        .Where(sample => LiveTimesMatch(sample.Source!.LiveTime, allocation.LiveTime))
                        .ToArray();
                    if (allocationSamples.Length != allocation.TargetSpectrumCount)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データは"
                            + $"{allocationSamples.Length}件です。必要数は{allocation.TargetSpectrumCount}件です。";
                    if (allocationSamples.Select(sample => sample.Source!.ConditionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データに複数のconditionKeyが混在しています。";
                    if (allocationSamples.Select(sample => sample.Source!.ManifestPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データに複数のmanifestが混在しています。";
                }

                int expectedCount = specification.TimeAllocations.Sum(allocation => allocation.TargetSpectrumCount);
                if (pool.Samples.Count != expectedCount)
                    return $"{pool.MineralName}の学習データは{pool.Samples.Count}件です。必要数は{expectedCount}件です。";

                var duplicateSource = pool.Samples
                    .GroupBy(sample => (sample.Source!.ManifestPath, sample.Source.SimulationId))
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateSource is not null)
                    return $"{pool.MineralName}で同じsimulation IDが重複しています: "
                        + $"{duplicateSource.Key.ManifestPath} / {duplicateSource.Key.SimulationId}";
            }

            return null;
        }

        // 260901Codex: Validate the provenance copy independently of request construction for direct workflow callers.
        private static string? ValidateTimeAllocations(IReadOnlyList<SpectrumTimeAllocation> allocations)
        {
            if (allocations.Count == 0)
                return "測定時間の配分が指定されていません。";

            for (int i = 0; i < allocations.Count; i++)
            {
                var allocation = allocations[i];
                if (!double.IsFinite(allocation.LiveTime) || allocation.LiveTime <= 0)
                    return "測定時間は0より大きい有限値にしてください。";
                if (allocation.TargetSpectrumCount <= 0)
                    return "測定時間ごとのスペクトル数は1以上にしてください。";
                for (int j = i + 1; j < allocations.Count; j++)
                    if (LiveTimesMatch(allocation.LiveTime, allocations[j].LiveTime))
                        return $"測定時間 {allocation.LiveTime} 秒が重複しています。";
            }

            return null;
        }

        // 260901Codex: Use the same tolerance as SEM condition identity validation.
        private static bool LiveTimesMatch(double left, double right) =>
            Math.Abs(left - right) <= 1e-9;

        // 260514Codex: 学習は tmp フォルダで完走させ、成功時だけ正式フォルダへ昇格します。
        // 260606Claude: 学習進捗 (モデル/エポック) を呼び出し元の UI へ渡すため progress を受け取ります。
        // 260901Codex: 呼び出し元が正式昇格と想定内の未昇格を区別できる結果を返します。
        public Task<ModelTrainingResult> RunAsync(
            ModelTrainingPlan plan,
            IProgress<TrainingProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => Run(plan, progress, cancellationToken), cancellationToken);

        // 260514Codex: キャンセルや失敗では tmp だけを片付け、既存の正式フォルダは残します。
        // 260901Codex: 正式フォルダへの昇格が完了した時点でだけ Promoted を返します。
        private ModelTrainingResult Run(ModelTrainingPlan plan, IProgress<TrainingProgress>? progress, CancellationToken cancellationToken)
        {
            // 260930Codex: Use the committed private staging convention recognized by the model catalog.
            string temporaryOutputFolder = Path.Combine(Path.GetDirectoryName(plan.ModelOutputFolder)!, $".{Guid.NewGuid():N}.tmp");
            _logAction("モデル作成開始");
            _logAction($"保存先（正式）: {plan.ModelOutputFolder}");
            _logAction($"保存先（仮）: {temporaryOutputFolder}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 260901Codex: Direct workflow callers receive the same strict multi-time preflight as the UI.
                string? validationError = Validate(plan);
                if (validationError is not null)
                    throw new InvalidOperationException(validationError);

                // 260619Codex: Stop before expensive training when overwrite consent was not included in the plan.
                if (Directory.Exists(plan.ModelOutputFolder) && !plan.AllowOverwriteExistingModel)
                    throw new InvalidOperationException(OverwriteNotAllowedMessage);

                if (Directory.Exists(temporaryOutputFolder))
                    Directory.Delete(temporaryOutputFolder, recursive: true);

                Directory.CreateDirectory(temporaryOutputFolder);
                var trainingResult = _deepLearning.RunTraining(
                    plan.TrainingPools,
                    plan.Settings.Epochs,
                    plan.Settings.BatchSize,
                    plan.Settings.EarlyStoppingPatience,
                    plan.Settings.ValidationSplit,
                    plan.Settings.UnknownDistanceScale,
                    temporaryOutputFolder,
                    progress,
                    cancellationToken,
                    // 260930Codex: Train with the value preserved in this model creation request.
                    classificationLearningRate: plan.Settings.LearningRate);
                if (trainingResult.Status == DeepLearningTrainingStatus.NotCompleted)
                {
                    string failureReason = trainingResult.FailureReason
                        ?? throw new InvalidOperationException("未完了の学習結果に理由がありません。");
                    var failureKind = trainingResult.FailureKind switch
                    {
                        DeepLearningTrainingFailureKind.TrainingDataLoadFailed => ModelTrainingFailureKind.TrainingDataLoadFailed,
                        null => throw new InvalidOperationException("未完了の学習結果に理由種別がありません。"),
                        _ => throw new InvalidOperationException($"未対応の学習失敗理由です: {trainingResult.FailureKind}")
                    };

                    // 260902Codex: 読込関門で止まった一時フォルダを消し、provenance・metadata・正式昇格へ進みません。
                    string? preservedArtifactsFolder = TryDeleteTemporaryFolder(temporaryOutputFolder)
                        ? null
                        : temporaryOutputFolder;
                    return ModelTrainingResult.CreateNotPromoted(
                        failureKind,
                        failureReason,
                        preservedArtifactsFolder);
                }
                if (trainingResult.Status != DeepLearningTrainingStatus.Completed)
                    throw new InvalidOperationException($"未対応の学習結果です: {trainingResult.Status}");

                // 260901Codex: Provenance is part of the temporary model and must succeed before formal promotion.
                if (plan.TrainingDataProvenance is not null)
                    TrainingDataProvenanceWriter.Write(
                        temporaryOutputFolder,
                        plan.TrainingDataProvenance,
                        plan.TrainingPools);
                // 260906Codex: Keep the observed validation results in the temporary model so incomplete runs are never promoted as results.
                TrainingResultsWriter.Write(
                    temporaryOutputFolder,
                    new TrainingResultsSpecification(
                        plan.Settings.Epochs,
                        plan.Settings.BatchSize,
                        plan.Settings.EarlyStoppingPatience,
                        plan.Settings.ValidationSplit,
                        DeepLearningDataSplitter.DefaultRandomState,
                        DeepLearning.EarlyStoppingMonitor,
                        // 260930Codex: Persist the actual classification optimizer rate with the model.
                        ClassificationLearningRate: plan.Settings.LearningRate),
                    trainingResult.ModelMetrics);
                WriteDetectorMetadata(plan.DetectorProfile, temporaryOutputFolder);

                cancellationToken.ThrowIfCancellationRequested();
                PromoteTemporaryFolder(temporaryOutputFolder, plan.ModelOutputFolder, plan.AllowOverwriteExistingModel);
                return ModelTrainingResult.CreatePromoted();
            }
            catch (Exception exception)
            {
                // 260827Claude: 予期しない失敗は型とスタックを学習ログへ残す。UI へ返るのは Message だけで、
                //   ログ TextBox も保存されないため、これが後から原因を追える唯一の記録になる。
                //   キャンセルは正常な終わり方なので記録しない。
                if (exception is not OperationCanceledException)
                    TensorFlowTrainingDebugLog.Write(
                        "training-run-failed",
                        $"path={TensorFlowTrainingDebugLog.Clean(plan.ModelOutputFolder)} type={exception.GetType().FullName} detail={TensorFlowTrainingDebugLog.Clean(exception.ToString())}");

                // 260514Codex: cleanup 失敗でキャンセル例外を隠さないよう、削除エラーはログに残して元の例外を戻します。
                TryDeleteTemporaryFolder(temporaryOutputFolder);

                throw;
            }
        }

        // 260902Codex: 想定内の未昇格でも例外でも、cleanup失敗が本来の結果を上書きしないよう共通化します。
        private bool TryDeleteTemporaryFolder(string temporaryOutputFolder)
        {
            try
            {
                Directory.Delete(temporaryOutputFolder, recursive: true);
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception cleanupException)
            {
                _logAction($"仮フォルダの削除に失敗しました: {temporaryOutputFolder}");
                _logAction(cleanupException.Message);
                return false;
            }
        }

        // 260626Codex: Record the detector profile beside trained artifacts so later predictions can trace training spectra.
        private static void WriteDetectorMetadata(DetectorProfile detectorProfile, string modelRootFolder)
        {
            detectorProfile.WriteToModelFolder(modelRootFolder);
            foreach (string modelFolder in Directory.EnumerateDirectories(modelRootFolder))
            {
                if (File.Exists(Path.Combine(modelFolder, ModelArtifactPaths.ModelTypeFileName)))
                    detectorProfile.WriteToModelFolder(modelFolder);
            }
        }

        // 260514Codex: 既存の正式フォルダがある場合も、成功した tmp を置く直前まで退避して失敗時に戻せるようにします。
        private void PromoteTemporaryFolder(string temporaryOutputFolder, string modelOutputFolder, bool allowOverwriteExistingModel)
        {
            _logAction("モデル保存先の仮フォルダを正式フォルダへ昇格します。");

            // 260930Codex: Keep promotion backups distinct from user models named with a .previous suffix.
            string backupOutputFolder = $"{modelOutputFolder}.{Guid.NewGuid():N}.previous";

            bool backupCreated = false;
            if (Directory.Exists(modelOutputFolder))
            {
                // 260619Codex: Re-check at promotion time so a late-created folder is not overwritten silently.
                if (!allowOverwriteExistingModel)
                    throw new InvalidOperationException(OverwriteNotAllowedMessage);

                _logAction($"既存モデルを上書きします: {modelOutputFolder}");
                Directory.Move(modelOutputFolder, backupOutputFolder);
                backupCreated = true;
            }

            try
            {
                Directory.Move(temporaryOutputFolder, modelOutputFolder);

                if (backupCreated)
                    Directory.Delete(backupOutputFolder, recursive: true);
            }
            catch
            {
                if (backupCreated && Directory.Exists(backupOutputFolder))
                {
                    if (Directory.Exists(modelOutputFolder))
                        Directory.Delete(modelOutputFolder, recursive: true);

                    Directory.Move(backupOutputFolder, modelOutputFolder);
                }

                throw;
            }

            _logAction($"モデル保存先を昇格しました: {modelOutputFolder}");
        }
    }
}
