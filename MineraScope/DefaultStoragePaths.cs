// 260430Codex: 既定保存先の組み立てに Path helper を使います。
using System.IO;

namespace MineraScope
{
    // 260430Codex: ユーザー管理データの既定保存先を Documents 配下へ一元化します。
    internal static class DefaultStoragePaths
    {
        // 260430Codex: ユーザー名や OneDrive リダイレクトに依存しない Documents 配下のアプリ用ルートです。
        private static string RootFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MineraScope");

        // 260430Codex: 学習済みモデルの既定保存先です。
        public static string ModelsFolder => Path.Combine(RootFolder, "Models");

        // 260430Codex: EDX スペクトル生成データと教師データの既定保存先です。
        public static string TrainingDataFolder => Path.Combine(RootFolder, "TrainingData");

        // 260717Codex: Keep persistent DTSA-II run logs under the shared Documents root.
        public static string LogsFolder => Path.Combine(RootFolder, "Logs");

        // 260717Codex: Keep hidden application settings under one LocalApplicationData folder.
        public static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MineraScope");

        // 260727Claude: DTSA-II へ渡す生成 Python スクリプトの固定保存先。GUI と headless で同じ場所を使う。
        public static string PythonScriptsFolder => Path.Combine(SettingsFolder, "PythonScripts");

        // 260730Claude: 改修前後の挙動を比べるための基準値スナップショット置き場 (BaselineSnapshotRunner)。
        //   diff して見る開発用データなので、Logs と同じく Documents 側に置いて見つけやすくする。
        public static string BaselinesFolder => Path.Combine(RootFolder, "Baselines");
    }
}
