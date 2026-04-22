using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MineraScope
{
    // 260416Codex: 生成画面の request 化と補助ロジックを partial に分け、既存の巨大な Form 本体をこれ以上膨らませないようにします。
    public partial class GeneratorForm
    {
        // 260416Codex: CheckedListBox からの文字列取得を共通化し、学習対象一覧の扱いを安定させます。
        private static string[] GetCheckedStrings(CheckedListBox listBox) =>
            listBox.CheckedItems
                .Cast<object>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();

        // 260416Codex: 画面上の入力値を request に変換し、今後の workflow 化の共通入口にします。
        private ModelCreationRequest CreateModelCreationRequest() =>
            new(
                new ModelCreationPaths(
                    textBoxPathEDX.Text.Trim(),
                    textBoxPathPython.Text.Trim().Trim('"'),
                    textBoxPathDTSA.Text.Trim(),
                    textBoxModel_Teacher.Text.Trim(),
                    textBoxModel_Save.Text.Trim()),
                new SemEdxCondition(
                    textBoxDetectorName.Text.Trim(),
                    (double)numericUpDownCarbonThichness.Value,
                    (double)numericUpDownBeamEnergy.Value,
                    (double)numericUpDownLiveTime.Value,
                    (double)numericUpDownProbeCurrent.Value),
                new SimulationExecutionSettings(
                    (int)numericUpDownMineral_Target.Value,
                    (double)numericUpDownEndmembers_Resolution.Value / 100,
                    (int)numericUpDownExecution_Count.Value,
                    (int)numericUpDownExecution_Parallel.Value),
                new ModelTrainingSettings(
                    (int)numericUpDownModel_Epochs.Value,
                    (int)numericUpDownModel_BatchSize.Value,
                    (int)numericUpDownModel_EaryStopping.Value,
                    (float)numericUpDownModel_ValidationSplit.Value / 100f),
                GetCheckedItems<SolidSolution>(checkedListBoxMineral),
                GetCheckedStrings(checkedListBoxTrainMinerals));

        // 260416Codex: 教師データの一覧更新を 1 か所に集め、後で UI を作り替えても再利用しやすくします。
        private void RefreshTrainingMineralList(string trainingPath)
        {
            var previousChecked = new HashSet<string>(GetCheckedStrings(checkedListBoxTrainMinerals), StringComparer.OrdinalIgnoreCase);
            var scannedMinerals = _trainingDataScanner.Scan(trainingPath);

            checkedListBoxTrainMinerals.Items.Clear();
            checkedListBoxTrainMinerals.Items.AddRange(scannedMinerals.Cast<object>().ToArray());

            // 260416Codex: 既存の選択と生成側のチェック状態を学習リストへ反映し、2 リストのズレを少しずつ減らします。
            var selectedGeneratorMinerals = new HashSet<string>(
                GetCheckedItems<SolidSolution>(checkedListBoxMineral).Select(solution => solution.Name),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < checkedListBoxTrainMinerals.Items.Count; i++)
            {
                var itemName = checkedListBoxTrainMinerals.Items[i]?.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                var shouldCheck = previousChecked.Contains(itemName) || selectedGeneratorMinerals.Contains(itemName);
                checkedListBoxTrainMinerals.SetItemChecked(i, shouldCheck);
            }
        }

        // 260416Codex: 生成側リストのチェックを学習側へ寄せ、専用リストを残したまま操作感を 1 リストに近づけます。
        private void SyncTrainingSelectionFromSelectedMinerals()
        {
            var selectedGeneratorMinerals = new HashSet<string>(
                GetCheckedItems<SolidSolution>(checkedListBoxMineral).Select(solution => solution.Name),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < checkedListBoxTrainMinerals.Items.Count; i++)
            {
                var itemName = checkedListBoxTrainMinerals.Items[i]?.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                if (!selectedGeneratorMinerals.Contains(itemName))
                {
                    continue;
                }

                checkedListBoxTrainMinerals.SetItemChecked(i, true);
            }
        }
    }
}
