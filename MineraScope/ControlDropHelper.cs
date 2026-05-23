namespace MineraScope
{
    // 260523Claude: フォーム配下の全コントロールへ同じファイルドロップ処理を再帰配線する共通ヘルパー。
    internal static class ControlDropHelper
    {
        // 260523Claude: 子孫コントロールに AllowDrop と DragEnter/DragDrop を再帰的に付ける（複合コントロールの内部も対象）。
        public static void EnableRecursive(Control root, DragEventHandler dragEnter, DragEventHandler dragDrop)
        {
            foreach (Control control in root.Controls)
            {
                control.AllowDrop = true;
                control.DragEnter += dragEnter;
                control.DragDrop += dragDrop;
                EnableRecursive(control, dragEnter, dragDrop);
            }
        }
    }
}
