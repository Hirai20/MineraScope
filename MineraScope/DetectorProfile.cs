using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineraScope
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DetectorWindowKind
    {
        NoWindow
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
        public int ChannelCount { get; set; } = 2048;
        public double ChannelWidth { get; set; } = 10.0;
        public double ZeroOffset { get; set; } = 0.0;
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
                Window = Window
            };

        public void WriteToModelFolder(string modelFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelFolder);
            Directory.CreateDirectory(modelFolder);
            File.WriteAllText(Path.Combine(modelFolder, FileName), JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
