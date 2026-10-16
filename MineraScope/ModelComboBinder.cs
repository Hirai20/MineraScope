namespace MineraScope
{
    // 260522Codex: Shared combo population for model selectors so FormMain and AnalyzerForm don't duplicate the skeleton.
    internal static class ModelComboBinder
    {
        // 260522Codex: Refill the combo from the catalog names, keeping the preferred (or previously selected) item when present.
        public static void Populate(ComboBox combo, IReadOnlyList<string> names, string preferredModelName)
        {
            string previousSelection = combo.SelectedItem as string ?? string.Empty;
            // 261016Codex: 優先モデルが一覧になければ、まだ利用可能な前の選択を維持する。
            string target = !string.IsNullOrWhiteSpace(preferredModelName) && names.Contains(preferredModelName)
                ? preferredModelName : previousSelection;

            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (string name in names)
                    combo.Items.Add(name);

                if (!string.IsNullOrWhiteSpace(target) && combo.Items.Contains(target))
                    combo.SelectedItem = target;
                else if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            finally
            {
                combo.EndUpdate();
            }
        }
    }
}
