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
        // 260619Codex: Store overwrite consent in the plan so workflow callers cannot replace models accidentally.
        bool AllowOverwriteExistingModel);

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

            return new ModelTrainingPlan(modelOutputFolder, trainingPools, request.Training, allowOverwriteExistingModel);
        }

        // 260514Codex: target 未設定や pool 不足は学習開始前に止めます。
        public string? Validate(ModelTrainingPlan plan)
        {
            if (plan.TrainingPools.Count == 0)
                return "モデル作成対象の鉱物が選択されていないか、学習可能な spectrum がありません。";

            if (plan.TrainingPools.Any(pool => pool.Samples.Count == 0))
                return "学習可能な spectrum がない鉱物があります。";

            return null;
        }

        // 260514Codex: 学習は tmp フォルダで完走させ、成功時だけ正式フォルダへ昇格します。
        // 260606Claude: 学習進捗 (モデル/エポック) を呼び出し元の UI へ渡すため progress を受け取ります。
        public Task RunAsync(
            ModelTrainingPlan plan,
            IProgress<TrainingProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => Run(plan, progress, cancellationToken), cancellationToken);

        // 260514Codex: キャンセルや失敗では tmp だけを片付け、既存の正式フォルダは残します。
        private void Run(ModelTrainingPlan plan, IProgress<TrainingProgress>? progress, CancellationToken cancellationToken)
        {
            string temporaryOutputFolder = $"{plan.ModelOutputFolder}.tmp";
            _logAction("モデル作成開始");
            _logAction($"保存先（正式）: {plan.ModelOutputFolder}");
            _logAction($"保存先（仮）: {temporaryOutputFolder}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 260619Codex: Stop before expensive training when overwrite consent was not included in the plan.
                if (Directory.Exists(plan.ModelOutputFolder) && !plan.AllowOverwriteExistingModel)
                    throw new InvalidOperationException(OverwriteNotAllowedMessage);

                if (Directory.Exists(temporaryOutputFolder))
                    Directory.Delete(temporaryOutputFolder, recursive: true);

                Directory.CreateDirectory(temporaryOutputFolder);
                _deepLearning.RunTraining(
                    plan.TrainingPools,
                    plan.Settings.Epochs,
                    plan.Settings.BatchSize,
                    plan.Settings.EarlyStoppingPatience,
                    plan.Settings.ValidationSplit,
                    plan.Settings.UnknownDistanceScale,
                    temporaryOutputFolder,
                    progress,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                PromoteTemporaryFolder(temporaryOutputFolder, plan.ModelOutputFolder, plan.AllowOverwriteExistingModel);
            }
            catch
            {
                // 260514Codex: cleanup 失敗でキャンセル例外を隠さないよう、削除エラーはログに残して元の例外を戻します。
                try
                {
                    if (Directory.Exists(temporaryOutputFolder))
                        Directory.Delete(temporaryOutputFolder, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    _logAction($"仮フォルダの削除に失敗しました: {temporaryOutputFolder}");
                    _logAction(cleanupException.Message);
                }

                throw;
            }
        }

        // 260514Codex: 既存の正式フォルダがある場合も、成功した tmp を置く直前まで退避して失敗時に戻せるようにします。
        private void PromoteTemporaryFolder(string temporaryOutputFolder, string modelOutputFolder, bool allowOverwriteExistingModel)
        {
            _logAction("モデル保存先の仮フォルダを正式フォルダへ昇格します。");

            string backupOutputFolder = $"{modelOutputFolder}.previous";
            if (Directory.Exists(backupOutputFolder))
                Directory.Delete(backupOutputFolder, recursive: true);

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
