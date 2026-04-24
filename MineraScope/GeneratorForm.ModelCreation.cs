using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MineraScope
{
    // 260416Codex: モデル作成まわりの入力変換と選択同期を partial に分けて読みやすく保ちます。
    public partial class GeneratorForm
    {
        // 260416Codex: CheckedListBox から有効な文字列だけを抜き出す共通 helper にします。
        private static string[] GetCheckedStrings(CheckedListBox listBox) =>
            listBox.CheckedItems
                .Cast<object>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();

        // 260416Codex: 生成側で選択済みの鉱物名を集合化し、学習候補との同期ロジックを短くします。
        private HashSet<string> GetSelectedGeneratorMineralNames() =>
            new(
                GetCheckedItems<SolidSolution>(checkedListBoxMineral).Select(solution => solution.Name),
                StringComparer.OrdinalIgnoreCase);

        // 260416Codex: CheckedListBox への項目入れ替えを共通化して一覧更新の重複を減らします。
        private static void ReplaceItems(CheckedListBox listBox, IEnumerable<string> items)
        {
            listBox.Items.Clear();
            listBox.Items.AddRange(items.Cast<object>().ToArray());
        }

        // 260416Codex: 名前集合に基づくチェック状態の反映を helper 化し、同型ループを 1 か所に寄せます。
        private static void SetCheckedStates(CheckedListBox listBox, ISet<string> checkedNames)
        {
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                string? itemName = listBox.Items[i]?.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                listBox.SetItemChecked(i, checkedNames.Contains(itemName));
            }
        }

        // 260416Codex: 画面上の入力値を request に集約し、workflow 側へそのまま渡せるようにします。
        private ModelCreationRequest CreateModelCreationRequest() =>
            new(
                // 260416Codex: Python スクリプトの保存先は固定パスへ集約した値を使います。
                new ModelCreationPaths(
                    textBoxPathEDX.Text.Trim(),
                    PythonScriptOutputPath,
                    textBoxPathDTSA.Text.Trim(),
                    textBoxPathTeacher.Text.Trim(),
                    textBoxModelPath.Text.Trim()),
                new SemEdxCondition(
                    textBoxDetectorName.Text.Trim(),
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
                    (int)numericBoxModel_Epochs.Value,
                    (int)numericBoxModel_BatchSize.Value,
                    (int)numericBoxModel_EarlyStopping.Value,
                    (float)numericBoxValidationSplit.Value / 100f),
                GetCheckedItems<SolidSolution>(checkedListBoxMineral),
                GetCheckedStrings(checkedListBoxTrainMinerals));

        // 260416Codex: 教師データ一覧の再構築と既存チェック復元を 1 つの流れにまとめます。
        private void RefreshTrainingMineralList(string trainingPath)
        {
            HashSet<string> checkedNames = new(GetCheckedStrings(checkedListBoxTrainMinerals), StringComparer.OrdinalIgnoreCase);
            checkedNames.UnionWith(GetSelectedGeneratorMineralNames());

            checkedListBoxTrainMinerals.BeginUpdate();
            try
            {
                // 260416Codex: 一覧差し替えは helper へ寄せ、メソッド本体では意図だけを残します。
                ReplaceItems(checkedListBoxTrainMinerals, _trainingDataScanner.Scan(trainingPath));
                SetCheckedStates(checkedListBoxTrainMinerals, checkedNames);
            }
            finally
            {
                checkedListBoxTrainMinerals.EndUpdate();
            }
        }

        // 260416Codex: 生成側で選んだ鉱物だけを学習候補へ加える処理も同じ helper 群に乗せます。
        private void SyncTrainingSelectionFromSelectedMinerals()
        {
            HashSet<string> checkedNames = new(GetCheckedStrings(checkedListBoxTrainMinerals), StringComparer.OrdinalIgnoreCase);
            checkedNames.UnionWith(GetSelectedGeneratorMineralNames());
            SetCheckedStates(checkedListBoxTrainMinerals, checkedNames);
        }
    }
}
