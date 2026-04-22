using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MineraScope
{
    // 260416Codex: モデル作成用の入力変換と選択同期を partial に切り出し、Form 本体を見通しやすくします。
    public partial class GeneratorForm
    {
        // 260416Codex: CheckedListBox から文字列のチェック状態を共通取得します。
        private static string[] GetCheckedStrings(CheckedListBox listBox) =>
            listBox.CheckedItems
                .Cast<object>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();

        // 260416Codex: 画面上の入力値を request に集約し、workflow へ渡しやすくします。
        private ModelCreationRequest CreateModelCreationRequest() =>
            new(
                // 260416Codex: Python スクリプト保存先は LocalAppData 配下へ統一します。
                new ModelCreationPaths(
                    textBoxPathEDX.Text.Trim(),
                    PythonScriptOutputPath,
                    textBoxPathDTSA.Text.Trim(),
                    textBoxPathTeacher.Text.Trim(),
                    textBoxModelPath.Text.Trim()),
                new SemEdxCondition(
                    textBoxDetectorName.Text.Trim(),
                    // 260416Codex: SEM-EDX 条件の数値入力は NumericBox からそのまま受け取ります。
                    numericBoxCarbonThickness.Value,
                    numericBoxBeamEnergy.Value,
                    numericBoxLiveTime.Value,
                    numericBoxProbeCurrent.Value),
                new SimulationExecutionSettings(
                    (int)numericUpDownMineral_Target.Value,
                    (double)numericUpDownEndmembers_Resolution.Value / 100,
                    (int)numericUpDownExecution_Count.Value,
                    (int)numericUpDownExecution_Parallel.Value),
                new ModelTrainingSettings(
                    // 260416Codex: モデル訓練設定は NumericBox へ統一したため、double 値を int に変換して使います。
                    (int)numericBoxModel_Epochs.Value,
                    (int)numericBoxModel_BatchSize.Value,
                    (int)numericBoxModel_EarlyStopping.Value,
                    (float)numericUpDownModel_ValidationSplit.Value / 100f),
                GetCheckedItems<SolidSolution>(checkedListBoxMineral),
                GetCheckedStrings(checkedListBoxTrainMinerals));

        // 260416Codex: 教師データ一覧の再構築を 1 か所にまとめ、UI 再利用をしやすくします。
        private void RefreshTrainingMineralList(string trainingPath)
        {
            HashSet<string> previousChecked = new(GetCheckedStrings(checkedListBoxTrainMinerals), StringComparer.OrdinalIgnoreCase);
            string[] scannedMinerals = _trainingDataScanner.Scan(trainingPath);

            checkedListBoxTrainMinerals.Items.Clear();
            checkedListBoxTrainMinerals.Items.AddRange(scannedMinerals.Cast<object>().ToArray());

            // 260416Codex: 生成側で選択済みの鉱物は学習候補でも初期チェックします。
            HashSet<string> selectedGeneratorMinerals = new(
                GetCheckedItems<SolidSolution>(checkedListBoxMineral).Select(solution => solution.Name),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < checkedListBoxTrainMinerals.Items.Count; i++)
            {
                string? itemName = checkedListBoxTrainMinerals.Items[i]?.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                bool shouldCheck = previousChecked.Contains(itemName) || selectedGeneratorMinerals.Contains(itemName);
                checkedListBoxTrainMinerals.SetItemChecked(i, shouldCheck);
            }
        }

        // 260416Codex: 生成側の選択状態を教師データ一覧へ同期します。
        private void SyncTrainingSelectionFromSelectedMinerals()
        {
            HashSet<string> selectedGeneratorMinerals = new(
                GetCheckedItems<SolidSolution>(checkedListBoxMineral).Select(solution => solution.Name),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < checkedListBoxTrainMinerals.Items.Count; i++)
            {
                string? itemName = checkedListBoxTrainMinerals.Items[i]?.ToString();
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
