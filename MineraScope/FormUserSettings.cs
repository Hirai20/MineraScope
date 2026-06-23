using System.IO;
using System.Text.Json;

namespace MineraScope
{
    // 260507Codex: WinForms の前回入力値を LocalApplicationData 配下の JSON に明示リストで保存します。
    internal static class FormUserSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MineraScope");

        public static T Load<T>(string fileName)
            where T : new()
        {
            string path = Path.Combine(SettingsFolder, fileName);
            if (!File.Exists(path))
                return new T();

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }

        // 260507Codex: 初回起動では Designer の既定値を壊さないよう、設定ファイルの有無を確認します。
        public static bool Exists(string fileName) =>
            File.Exists(Path.Combine(SettingsFolder, fileName));

        public static void Save<T>(string fileName, T settings)
        {
            Directory.CreateDirectory(SettingsFolder);
            string path = Path.Combine(SettingsFolder, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }

    // 260507Codex: FormMain では共通パス欄だけを保存し、解析ログや入力 spectrum 表示は保存しません。
    internal sealed class FormMainUserSettings
    {
        public string ModelPath { get; set; } = string.Empty;
        public string SelectedModelName { get; set; } = string.Empty;
        public string EdxOutputPath { get; set; } = string.Empty;
        public string DtsaPath { get; set; } = string.Empty;
    }

    // 260507Codex: GeneratorForm では生成・EDX・学習設定だけを保存し、鉱物詳細やログは保存しません。
    internal sealed class GeneratorFormUserSettings
    {
        public string DetectorName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public double TargetSpectrumCount { get; set; }
        public double ParallelCount { get; set; }
        public double Resolution { get; set; }
        public double Epochs { get; set; }
        public double BatchSize { get; set; }
        // 260622Claude: 旧設定ファイルにこのフィールドが無いとき (JSON 欠落時) は既定 1.5 を保つ (既知で Unknown が出ないラインを探す出発点)。
        public double UnknownDistanceScale { get; set; } = 1.5;
        public double EarlyStopping { get; set; }
        public double ValidationSplit { get; set; }
        public double ProbeCurrent { get; set; }
        public double LiveTime { get; set; }
        public double BeamEnergy { get; set; }
        public double CarbonThickness { get; set; }
        // 260622Claude: カーボン蒸着膜厚を spectrum ごとに振るばらつき幅 (%)。旧設定ファイルに無いとき (JSON 欠落時) は既定 20 を保つ。
        public double CarbonThicknessJitterPercent { get; set; } = 20;
    }
}
