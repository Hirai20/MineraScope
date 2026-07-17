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

        // 260717Codex: Keep hidden application settings under one LocalApplicationData folder.
        public static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MineraScope");
    }
}
