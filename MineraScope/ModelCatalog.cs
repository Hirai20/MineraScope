namespace MineraScope
{
    // 260522Codex: Carries the model name that a refresh wants pre-selected, so every view can honor it.
    internal sealed class ModelCatalogChangedEventArgs(string preferredModelName) : EventArgs
    {
        public string PreferredModelName { get; } = preferredModelName ?? string.Empty;
    }

    // 260522Codex: Single source of truth for the model parent folder and its model subfolder names.
    // Views (FormMain / AnalyzerForm) render from this and subscribe to Changed instead of scanning disk themselves.
    internal sealed class ModelCatalog
    {
        public string ParentPath { get; private set; } = string.Empty;

        public IReadOnlyList<string> ModelNames { get; private set; } = [];

        public event EventHandler<ModelCatalogChangedEventArgs>? Changed;

        // 260522Codex: Re-scan the parent folder once and notify every view; the only place model folders are enumerated.
        public void Update(string parentPath, string preferredModelName = "")
        {
            ParentPath = parentPath ?? string.Empty;
            ModelNames = Directory.Exists(ParentPath)
                ? Directory.GetDirectories(ParentPath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name)
                    .Select(name => name!)
                    .ToArray()
                : [];

            Changed?.Invoke(this, new ModelCatalogChangedEventArgs(preferredModelName));
        }
    }
}
