namespace MineraScope
{
    // 260416Codex: フォルダ選択ダイアログを Form 間で再利用できる共通処理にします。
    internal static class FolderSelectionHelper
    {
        // 260416Codex: 選択結果の書き戻し先だけを受け取る形にして UI 移設後も流用しやすくします。
        public static bool TrySelectFolder(TextBox targetTextBox)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            targetTextBox.Text = dialog.SelectedPath;
            return true;
        }
    }
}
