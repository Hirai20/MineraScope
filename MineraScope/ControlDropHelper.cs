namespace MineraScope
{
    // 260523Claude: フォーム配下の全コントロールへ同じファイルドロップ処理を再帰配線する共通ヘルパー。
    internal static class ControlDropHelper
    {
        // 260527Codex: Skip controls already wired by the Designer while still wiring their dynamic descendants.
        public static void EnableRecursive(Control root, DragEventHandler dragEnter, DragEventHandler dragDrop, params Control[] designerWiredControls)
        {
            foreach (Control control in root.Controls)
            {
                control.AllowDrop = true;
                if (!designerWiredControls.Contains(control))
                {
                    control.DragEnter += dragEnter;
                    control.DragDrop += dragDrop;
                }

                EnableRecursive(control, dragEnter, dragDrop, designerWiredControls);
            }
        }
    }
}
