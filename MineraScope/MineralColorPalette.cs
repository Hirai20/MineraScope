using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MineraScope
{
    // 260717Codex: Track whether a persisted mineral color was assigned automatically or explicitly by the user.
    internal enum MineralColorAssignment
    {
        Auto,
        Manual,
        Fixed
    }

    // 260717Codex: Carry the persisted color and its origin together through map and legend generation.
    internal readonly record struct MineralColorChoice(Color Color, MineralColorAssignment Assignment);

    // 260717Codex: Own the global mineral-name palette, perceptual assignment, and versioned LocalApplicationData persistence.
    internal sealed class MineralColorPalette
    {
        private const int CurrentVersion = 1;
        private const string FileName = "MineralColorPalette.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly Color[] CandidateColors = BuildCandidateColors();
        private readonly Dictionary<string, PaletteEntry> _entries;
        private bool _isDirty;

        private MineralColorPalette(Dictionary<string, PaletteEntry> entries)
            => _entries = entries;

        // 260717Codex: Recover from a missing or unreadable palette without preventing AnalyzerForm from opening.
        public static MineralColorPalette Load(out string? warning)
        {
            warning = null;
            string path = GetPalettePath();
            if (!File.Exists(path))
                return new MineralColorPalette(new Dictionary<string, PaletteEntry>(StringComparer.OrdinalIgnoreCase));

            try
            {
                PaletteFile? file = JsonSerializer.Deserialize<PaletteFile>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (file is null || file.Version != CurrentVersion)
                    throw new InvalidDataException($"Unsupported mineral color palette version: {file?.Version.ToString(CultureInfo.InvariantCulture) ?? "null"}");

                var entries = new Dictionary<string, PaletteEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (PaletteFileEntry? item in file.Colors ?? [])
                {
                    if (item is null)
                        continue;

                    string key = NormalizeName(item.MineralName);
                    if (key.Length == 0 || !TryParseHex(item.Hex, out Color color))
                        continue;

                    if (!Enum.TryParse(item.Assignment, true, out MineralColorAssignment assignment) ||
                        assignment is not (MineralColorAssignment.Auto or MineralColorAssignment.Manual))
                    {
                        continue;
                    }

                    entries[key] = new PaletteEntry(item.MineralName.Trim(), color, assignment);
                }

                return new MineralColorPalette(entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
            {
                warning = BackupUnreadablePalette(path, ex.Message);
                return new MineralColorPalette(new Dictionary<string, PaletteEntry>(StringComparer.OrdinalIgnoreCase));
            }
        }

        // 260717Codex: Reuse every persisted color unchanged; only callers with a previously unseen mineral receive an automatic color.
        public bool TryGetColor(string mineralName, out MineralColorChoice choice)
        {
            if (_entries.TryGetValue(NormalizeName(mineralName), out PaletteEntry? entry))
            {
                choice = new MineralColorChoice(entry.Color, entry.Assignment);
                return true;
            }

            choice = default;
            return false;
        }

        // 260717Codex: Choose the unused candidate maximizing its minimum OKLab distance from the current TOP20 and fixed colors.
        public MineralColorChoice AssignAutomaticColor(string mineralName, IReadOnlyCollection<Color> currentColors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mineralName);
            ArgumentNullException.ThrowIfNull(currentColors);

            if (TryGetColor(mineralName, out MineralColorChoice existing))
                return existing;

            var globallyUsed = _entries.Values.Select(entry => entry.Color.ToArgb()).ToHashSet();
            Color color = SelectMostDistantColor(currentColors, globallyUsed);
            string name = mineralName.Trim();
            _entries[NormalizeName(name)] = new PaletteEntry(name, color, MineralColorAssignment.Auto);
            _isDirty = true;
            return new MineralColorChoice(color, MineralColorAssignment.Auto);
        }

        // 260717Codex: A manual edit changes only the selected mineral and deliberately permits equal or nearby colors.
        public void SetManualColor(string mineralName, Color color)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mineralName);

            string name = mineralName.Trim();
            string key = NormalizeName(name);
            if (_entries.TryGetValue(key, out PaletteEntry? current) &&
                current.Color.ToArgb() == color.ToArgb() &&
                current.Assignment == MineralColorAssignment.Manual)
            {
                return;
            }

            _entries[key] = new PaletteEntry(name, Color.FromArgb(color.R, color.G, color.B), MineralColorAssignment.Manual);
            _isDirty = true;
        }

        // 260717Codex: Write a deterministic, versioned JSON snapshot through a same-folder temporary file.
        public bool TrySave(out string? error)
        {
            error = null;
            if (!_isDirty)
                return true;

            string path = GetPalettePath();
            string temporaryPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(DefaultStoragePaths.SettingsFolder);
                var file = new PaletteFile
                {
                    Version = CurrentVersion,
                    Colors = _entries.Values
                        .OrderBy(entry => entry.MineralName, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => new PaletteFileEntry
                        {
                            MineralName = entry.MineralName,
                            Hex = ToHex(entry.Color),
                            Assignment = entry.Assignment.ToString()
                        })
                        .ToList()
                };

                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, JsonOptions), new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
                _isDirty = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // 260717Codex: Centralize the single persisted palette path.
        private static string GetPalettePath() => Path.Combine(DefaultStoragePaths.SettingsFolder, FileName);

        // 260717Codex: Match mineral names after trimming while the dictionary supplies ordinal case-insensitivity.
        private static string NormalizeName(string? mineralName) => mineralName?.Trim() ?? string.Empty;

        // 260717Codex: Prefer globally unused RGB values, then maximize distance from colors visible in the current map.
        private static Color SelectMostDistantColor(IReadOnlyCollection<Color> currentColors, HashSet<int> globallyUsed)
        {
            Color best = CandidateColors[0];
            double bestDistance = double.NegativeInfinity;
            bool foundUnused = false;

            foreach (Color candidate in CandidateColors)
            {
                if (globallyUsed.Contains(candidate.ToArgb()))
                    continue;

                foundUnused = true;
                double distance = MinimumOklabDistance(candidate, currentColors);
                if (distance > bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (foundUnused)
                return best;

            foreach (Color candidate in CandidateColors)
            {
                double distance = MinimumOklabDistance(candidate, currentColors);
                if (distance > bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        // 260717Codex: Measure color separation in OKLab rather than device RGB coordinates.
        private static double MinimumOklabDistance(Color candidate, IReadOnlyCollection<Color> references)
        {
            if (references.Count == 0)
                return double.MaxValue;

            OklabColor candidateLab = ToOklab(candidate);
            double minimum = double.MaxValue;
            foreach (Color reference in references)
            {
                OklabColor referenceLab = ToOklab(reference);
                double deltaL = candidateLab.L - referenceLab.L;
                double deltaA = candidateLab.A - referenceLab.A;
                double deltaB = candidateLab.B - referenceLab.B;
                minimum = Math.Min(minimum, Math.Sqrt(deltaL * deltaL + deltaA * deltaA + deltaB * deltaB));
            }

            return minimum;
        }

        // 260717Codex: Prepare 108 deterministic OKLCH candidates spanning hue and useful lightness levels.
        private static Color[] BuildCandidateColors()
        {
            (double Lightness, double Chroma, double HueOffset)[] layers =
            [
                (0.62, 0.21, 0),
                (0.74, 0.17, 5),
                (0.50, 0.18, 0)
            ];

            var colors = new List<Color>();
            var seen = new HashSet<int>();
            foreach ((double lightness, double chroma, double hueOffset) in layers)
            {
                for (int hueIndex = 0; hueIndex < 36; hueIndex++)
                {
                    Color color = FromOklch(lightness, chroma, hueIndex * 10 + hueOffset);
                    if (seen.Add(color.ToArgb()))
                        colors.Add(color);
                }
            }

            return colors.ToArray();
        }

        // 260717Codex: Reduce chroma only as needed to keep each OKLCH candidate inside the sRGB gamut.
        private static Color FromOklch(double lightness, double chroma, double hueDegrees)
        {
            double hueRadians = hueDegrees * Math.PI / 180;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                double adjustedChroma = chroma * Math.Pow(0.92, attempt);
                double a = adjustedChroma * Math.Cos(hueRadians);
                double b = adjustedChroma * Math.Sin(hueRadians);
                (double red, double green, double blue) = OklabToLinearRgb(lightness, a, b);
                if (IsInRgbGamut(red, green, blue))
                {
                    return Color.FromArgb(
                        ToColorByte(ToSrgb(red)),
                        ToColorByte(ToSrgb(green)),
                        ToColorByte(ToSrgb(blue)));
                }
            }

            (double fallbackRed, double fallbackGreen, double fallbackBlue) = OklabToLinearRgb(lightness, 0, 0);
            return Color.FromArgb(
                ToColorByte(ToSrgb(Math.Clamp(fallbackRed, 0, 1))),
                ToColorByte(ToSrgb(Math.Clamp(fallbackGreen, 0, 1))),
                ToColorByte(ToSrgb(Math.Clamp(fallbackBlue, 0, 1))));
        }

        // 260717Codex: Convert OKLab coordinates to linear sRGB using the published OKLab transform.
        private static (double Red, double Green, double Blue) OklabToLinearRgb(double lightness, double a, double b)
        {
            double lRoot = lightness + 0.3963377774 * a + 0.2158037573 * b;
            double mRoot = lightness - 0.1055613458 * a - 0.0638541728 * b;
            double sRoot = lightness - 0.0894841775 * a - 1.2914855480 * b;
            double l = lRoot * lRoot * lRoot;
            double m = mRoot * mRoot * mRoot;
            double s = sRoot * sRoot * sRoot;
            return (
                4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
        }

        // 260717Codex: Convert persisted 8-bit sRGB colors to OKLab for perceptual distance comparisons.
        private static OklabColor ToOklab(Color color)
        {
            double red = ToLinearRgb(color.R / 255.0);
            double green = ToLinearRgb(color.G / 255.0);
            double blue = ToLinearRgb(color.B / 255.0);
            double lRoot = Math.Cbrt(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue);
            double mRoot = Math.Cbrt(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue);
            double sRoot = Math.Cbrt(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue);
            return new OklabColor(
                0.2104542553 * lRoot + 0.7936177850 * mRoot - 0.0040720468 * sRoot,
                1.9779984951 * lRoot - 2.4285922050 * mRoot + 0.4505937099 * sRoot,
                0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.8086757660 * sRoot);
        }

        // 260717Codex: Accept only linear RGB values representable without clipping.
        private static bool IsInRgbGamut(double red, double green, double blue)
            => red is >= 0 and <= 1 && green is >= 0 and <= 1 && blue is >= 0 and <= 1;

        // 260717Codex: Decode one sRGB channel to linear light.
        private static double ToLinearRgb(double value)
            => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        // 260717Codex: Encode one linear-light channel to sRGB.
        private static double ToSrgb(double value)
            => value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

        // 260717Codex: Round a normalized sRGB channel to its persisted 8-bit representation.
        private static int ToColorByte(double value)
            => (int)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue, MidpointRounding.AwayFromZero);

        // 260717Codex: Accept the palette file's strict #RRGGBB representation.
        private static bool TryParseHex(string? value, out Color color)
        {
            color = default;
            if (value is null || value.Length != 7 || value[0] != '#' ||
                !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return false;
            }

            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        // 260717Codex: Persist colors without alpha because map palettes are always opaque.
        private static string ToHex(Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        // 260717Codex: Retain an unreadable settings file before a later successful save replaces the main path.
        private static string BackupUnreadablePalette(string path, string reason)
        {
            string backupPath = Path.Combine(
                Path.GetDirectoryName(path) ?? DefaultStoragePaths.SettingsFolder,
                $"{Path.GetFileNameWithoutExtension(path)}.invalid.json");
            try
            {
                File.Copy(path, backupPath, true);
                return $"鉱物色設定を読み込めなかったため、初期状態で開始します。破損ファイルは次へ退避しました。\r\n{backupPath}\r\n\r\n{reason}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"鉱物色設定を読み込めなかったため、初期状態で開始します。\r\n{reason}\r\n\r\n退避にも失敗しました。\r\n{ex.Message}";
            }
        }

        // 260717Codex: Store normalized-key values independently from their preferred display spelling.
        private sealed record PaletteEntry(string MineralName, Color Color, MineralColorAssignment Assignment);

        // 260717Codex: Keep distance calculations in a compact value type.
        private readonly record struct OklabColor(double L, double A, double B);

        // 260717Codex: Define the versioned root DTO explicitly for future migrations.
        private sealed class PaletteFile
        {
            public int Version { get; set; }
            public List<PaletteFileEntry>? Colors { get; set; } = [];
        }

        // 260717Codex: Keep each persisted mineral color human-readable and source-aware.
        private sealed class PaletteFileEntry
        {
            public string MineralName { get; set; } = string.Empty;
            public string Hex { get; set; } = string.Empty;
            public string Assignment { get; set; } = string.Empty;
        }
    }
}
