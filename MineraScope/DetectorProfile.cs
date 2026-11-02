using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineraScope
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DetectorWindowKind
    {
        NoWindow,
        // 261030Claude: WindowLayers の層を DTSA-II の XRayWindow として検出器の前に置く。
        Layers
    }

    // 261030Claude: 窓の1層。窓は設定 JSON の手書きでしか指定できないため、単位を項目名に含めて取り違えを防ぐ。
    //   厚さは既存の AluminumLayer・GoldLayer と同じ nm で持つ。
    public sealed record DetectorWindowLayer
    {
        public string Formula { get; init; } = string.Empty;
        public double DensityGPerCm3 { get; init; }
        public double ThicknessNm { get; init; }
    }

    // 260626Codex: Keep DTSA-II detector physics in MineraScope settings instead of the DTSA-II Derby detector DB.
    public sealed class DetectorProfile
    {
        public const string FileName = "detectorProfile.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Name { get; set; } = "test";
        // 260803Codex: JSON フィールドは維持したまま、軸の既定値を SpectrumAxis へ一本化します。
        public int ChannelCount { get; set; } = SpectrumAxis.Default.ChannelCount;
        public double ChannelWidth { get; set; } = SpectrumAxis.Default.ChannelWidth;
        public double ZeroOffset { get; set; } = SpectrumAxis.Default.ZeroOffset;
        public double ResolutionFwhmAtMnKa { get; set; } = 126.0;
        public double DetectorArea { get; set; } = 30.0;
        public double Elevation { get; set; } = 35.0;
        public double Azimuth { get; set; } = 90.0;
        public double SpecimenToDetectorDistance { get; set; } = 50.0;
        public double OptimalWorkingDistance { get; set; } = 10.0;
        public double SiThickness { get; set; } = 0.45;
        public double AluminumLayer { get; set; } = 20.0;
        public double GoldLayer { get; set; } = 6.0;
        public double NickelLayer { get; set; } = 0.0;
        public double DeadLayer { get; set; } = 0.2;
        public DetectorWindowKind Window { get; set; } = DetectorWindowKind.NoWindow;
        // 261030Claude: 窓なしでは null のまま JSON へ書かず、既存 pool の条件キーと detectorProfile.json を変えない。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DetectorWindowLayer>? WindowLayers { get; set; }

        // 260803Codex: 永続化済みの 3 フィールドから検証済みの不変軸を返します。
        public SpectrumAxis GetSpectrumAxis() => new(ChannelCount, ZeroOffset, ChannelWidth);

        // 261030Claude: 窓の不正な設定は既定値へ戻さずに拒否する。戻すと、窓なしのスペクトルを窓ありの条件として作ってしまう。
        //   起動時の設定読込 (CreateWithDefaults) では検査せず、条件キーの作成とスクリプト生成の直前に呼ぶ。
        public void ValidateWindow()
        {
            if (Window != DetectorWindowKind.Layers)
            {
                // 261030Claude: 空の一覧も JSON に出ると条件キーが変わるため、窓なしでは一覧そのものを持たせない。
                if (WindowLayers is not null)
                    throw new InvalidDataException($"検出器の窓が {Window} なのに WindowLayers が指定されています。窓を使う場合は Window を {DetectorWindowKind.Layers} にしてください。");

                return;
            }

            if (WindowLayers is null || WindowLayers.Count == 0)
                throw new InvalidDataException($"検出器の窓が {DetectorWindowKind.Layers} ですが、WindowLayers に層がありません。");

            for (int i = 0; i < WindowLayers.Count; i++)
            {
                var layer = WindowLayers[i]
                    ?? throw new InvalidDataException($"検出器の窓の層 {i + 1} が空です。");
                // 261030Claude: 化学式としての正しさは DTSA-II の createCompound に任せ、条件キーを揺らす空白だけをここで拒否する。
                if (string.IsNullOrWhiteSpace(layer.Formula) || layer.Formula != layer.Formula.Trim())
                    throw new InvalidDataException($"検出器の窓の層 {i + 1} の組成式が空か、前後に空白があります。");
                if (!double.IsFinite(layer.DensityGPerCm3) || layer.DensityGPerCm3 <= 0)
                    throw new InvalidDataException($"検出器の窓の層 {i + 1} の密度 (g/cm³) は 0 より大きい有限値にしてください。");
                if (!double.IsFinite(layer.ThicknessNm) || layer.ThicknessNm <= 0)
                    throw new InvalidDataException($"検出器の窓の層 {i + 1} の厚さ (nm) は 0 より大きい有限値にしてください。");
            }
        }

        public static DetectorProfile CreateLegacyTest(string? name = null)
        {
            var profile = new DetectorProfile();
            if (!string.IsNullOrWhiteSpace(name))
                profile.Name = name.Trim();

            return profile;
        }

        public static DetectorProfile CreateWithDefaults(DetectorProfile? profile, string? fallbackName = null)
        {
            var defaults = CreateLegacyTest(fallbackName);
            if (profile is null)
                return defaults;

            var value = profile.Clone();
            if (string.IsNullOrWhiteSpace(value.Name))
                value.Name = defaults.Name;
            else
                value.Name = value.Name.Trim();

            if (value.ChannelCount <= 0)
                value.ChannelCount = defaults.ChannelCount;
            if (value.ChannelWidth <= 0)
                value.ChannelWidth = defaults.ChannelWidth;
            if (value.ResolutionFwhmAtMnKa <= 0)
                value.ResolutionFwhmAtMnKa = defaults.ResolutionFwhmAtMnKa;
            if (value.DetectorArea <= 0)
                value.DetectorArea = defaults.DetectorArea;
            if (value.SpecimenToDetectorDistance <= 0)
                value.SpecimenToDetectorDistance = defaults.SpecimenToDetectorDistance;
            if (value.OptimalWorkingDistance <= 0)
                value.OptimalWorkingDistance = defaults.OptimalWorkingDistance;
            if (value.SiThickness <= 0)
                value.SiThickness = defaults.SiThickness;
            if (!Enum.IsDefined(value.Window))
                value.Window = defaults.Window;

            return value;
        }

        public DetectorProfile Clone() =>
            new()
            {
                Name = Name,
                ChannelCount = ChannelCount,
                ChannelWidth = ChannelWidth,
                ZeroOffset = ZeroOffset,
                ResolutionFwhmAtMnKa = ResolutionFwhmAtMnKa,
                DetectorArea = DetectorArea,
                Elevation = Elevation,
                Azimuth = Azimuth,
                SpecimenToDetectorDistance = SpecimenToDetectorDistance,
                OptimalWorkingDistance = OptimalWorkingDistance,
                SiThickness = SiThickness,
                AluminumLayer = AluminumLayer,
                GoldLayer = GoldLayer,
                NickelLayer = NickelLayer,
                DeadLayer = DeadLayer,
                Window = Window,
                // 261030Claude: 層は不変なので一覧だけを複製し、複製先での追加・削除を元へ波及させない。
                WindowLayers = WindowLayers?.ToList()
            };

        public void WriteToModelFolder(string modelFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelFolder);
            Directory.CreateDirectory(modelFolder);
            File.WriteAllText(Path.Combine(modelFolder, FileName), JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
