using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260507Codex: manifest から選ばれた学習入力とモデル保存先を学習 workflow の plan にします。
    internal sealed record ModelTrainingPlan(
        string ModelOutputFolder,
        IReadOnlyList<SpectrumTrainingPool> TrainingPools,
        ModelTrainingSettings Settings);

    // 260507Codex: モデル学習の検証と DeepLearning 呼び出しを Form から分離します。
    internal sealed class ModelTrainingWorkflow
    {
        private readonly DeepLearning _deepLearning;

        public ModelTrainingWorkflow(DeepLearning deepLearning)
        {
            _deepLearning = deepLearning ?? throw new ArgumentNullException(nameof(deepLearning));
        }

        // 260507Codex: 学習対象は pool workflow が抽出した Completed spectrum だけにします。
        public ModelTrainingPlan CreatePlan(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumTrainingPool> trainingPools)
        {
            string modelRootFolder = string.IsNullOrWhiteSpace(request.Paths.ModelOutputFolder)
                ? DefaultStoragePaths.ModelsFolder
                : request.Paths.ModelOutputFolder;
            // 260508Codex: 学習成果物は FormMain が一覧できる親フォルダ直下のモデル名フォルダへまとめます。
            string modelOutputFolder = Path.Combine(modelRootFolder, request.ModelName);

            return new ModelTrainingPlan(modelOutputFolder, trainingPools, request.Training);
        }

        // 260507Codex: target 未設定や pool 不足は学習開始前に止めます。
        public string? Validate(ModelTrainingPlan plan)
        {
            if (plan.TrainingPools.Count == 0)
            {
                return "モデル作成対象の鉱物が選択されていないか、学習可能な spectrum がありません。";
            }

            if (plan.TrainingPools.Any(pool => pool.Samples.Count == 0))
            {
                return "学習可能な spectrum がない鉱物があります。";
            }

            return null;
        }

        // 260507Codex: DeepLearning へ manifest ベースの学習入力を渡します。
        public Task RunAsync(ModelTrainingPlan plan)
        {
            Directory.CreateDirectory(plan.ModelOutputFolder);

            return Task.Run(() =>
                _deepLearning.RunTraining(
                    plan.TrainingPools,
                    plan.Settings.Epochs,
                    plan.Settings.BatchSize,
                    plan.Settings.EarlyStoppingPatience,
                    plan.Settings.ValidationSplit,
                    plan.ModelOutputFolder));
        }
    }
}
