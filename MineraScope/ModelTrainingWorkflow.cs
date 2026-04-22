using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260416Codex: 学習実行前に確定した値を plan として切り出し、Form 側の分岐を薄く保ちます。
    internal sealed record ModelTrainingPlan(
        string TrainingDataFolder,
        string ModelOutputFolder,
        IReadOnlyList<string> TargetMinerals,
        ModelTrainingSettings Settings);

    // 260416Codex: モデル学習の入力確定・検証・実行を Form から分離し、新しい生成画面でも再利用できるようにします。
    internal sealed class ModelTrainingWorkflow
    {
        private readonly DeepLearning _deepLearning;

        // 260416Codex: 既存の DeepLearning 実装をそのまま使いながら、UI との結合だけを弱めます。
        public ModelTrainingWorkflow(DeepLearning deepLearning)
        {
            _deepLearning = deepLearning ?? throw new ArgumentNullException(nameof(deepLearning));
        }

        // 260416Codex: 学習対象は専用リストを優先し、未選択なら生成側でチェックされた鉱物名へ自然にフォールバックさせます。
        public ModelTrainingPlan CreatePlan(ModelCreationRequest request)
        {
            var targetMinerals = request.SelectedTrainingMinerals.Count > 0
                ? request.SelectedTrainingMinerals
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : request.SelectedMineralSolutions
                    .Select(solution => solution.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var modelOutputFolder = string.IsNullOrWhiteSpace(request.Paths.ModelOutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Models")
                : request.Paths.ModelOutputFolder;

            return new ModelTrainingPlan(
                request.Paths.TeacherDataFolder,
                modelOutputFolder,
                targetMinerals,
                request.Training);
        }

        // 260416Codex: 学習前のエラーメッセージを service 側で揃え、Form は表示に専念できるようにします。
        public string? Validate(ModelTrainingPlan plan)
        {
            if (plan.TargetMinerals.Count == 0)
            {
                return "学習対象の鉱物が選択されていません。";
            }

            if (!Directory.Exists(plan.TrainingDataFolder))
            {
                return "訓練データフォルダが見つかりません。";
            }

            return null;
        }

        // 260416Codex: 実行前に保存先を確実に作成し、既存 DeepLearning.RunTraining へ必要値だけ渡します。
        public Task RunAsync(ModelTrainingPlan plan)
        {
            Directory.CreateDirectory(plan.ModelOutputFolder);

            return Task.Run(() =>
                _deepLearning.RunTraining(
                    plan.TargetMinerals.ToList(),
                    plan.TrainingDataFolder,
                    plan.Settings.Epochs,
                    plan.Settings.BatchSize,
                    plan.Settings.EarlyStoppingPatience,
                    plan.Settings.ValidationSplit,
                    plan.ModelOutputFolder));
        }
    }
}
