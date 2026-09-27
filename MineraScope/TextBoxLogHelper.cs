namespace MineraScope
{
    // 260416Codex: UI スレッドをまたぐ TextBox へのログ追記処理を共通化します。
    internal static class TextBoxLogHelper
    {
        // 260416Codex: 呼び出し元 Form ごとの重複を避けつつ安全に改行付き追記を行います。
        public static void AppendLine(TextBox target, string message)
        {
            var line = $"{message}{Environment.NewLine}";

            if (target.InvokeRequired)
            {
                target.Invoke(new Action(() => target.AppendText(line)));
                return;
            }

            target.AppendText(line);
        }
    }
}
