namespace MineraScope
{
    // 260522Codex: 更新時に優先選択したいモデル名を渡し、すべての画面でその指定を使えるようにする。
    internal sealed class ModelCatalogChangedEventArgs(string preferredModelName) : EventArgs
    {
        public string PreferredModelName { get; } = preferredModelName ?? string.Empty;
    }

    // 260522Codex: モデル親フォルダーとモデルの子フォルダー名の唯一の正本とする。
    // FormMain・AnalyzerFormは自分でディスクを走査せず、ここから表示を構成してChangedを購読する。
    internal sealed class ModelCatalog
    {
        public string ParentPath { get; private set; } = string.Empty;

        public IReadOnlyList<string> ModelNames { get; private set; } = [];

        public event EventHandler<ModelCatalogChangedEventArgs>? Changed;

        // 260522Codex: 親フォルダーを一度だけ再走査して全画面へ通知し、モデルフォルダーの列挙をここへ集約する。
        public void Update(string parentPath, string preferredModelName = "")
        {
            ParentPath = parentPath ?? string.Empty;
            ModelNames = Directory.Exists(ParentPath)
                ? Directory.GetDirectories(ParentPath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    // 261014Codex: 保存した独立OCNバンドルを、通常選択可能な分類器に含めない。
                    .Where(name => !string.Equals(name, "OCN", StringComparison.OrdinalIgnoreCase))
                    // 260930Codex: GUID付きの学習作業用フォルダーと昇格バックアップだけを非表示にする。
                    .Where(name => !IsTrainingWorkFolder(name!))
                    .OrderBy(name => name)
                    .Select(name => name!)
                    .ToArray()
                : [];

            Changed?.Invoke(this, new ModelCatalogChangedEventArgs(preferredModelName));
        }

        // 260930Codex: sample.tmpやsample.previousなどのユーザー名は選択可能に保つ。
        private static bool IsTrainingWorkFolder(string name) =>
            name.Split('.') is [.., var id, "tmp" or "previous"] && Guid.TryParseExact(id, "N", out _);
    }
}
