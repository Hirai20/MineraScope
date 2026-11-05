// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using Crystallography;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static System.FormattableString;

namespace MineraScope
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DetectorWindowKind
    {
        NoWindow,
        // 261030Claude: WindowLayers の層を DTSA-II の XRayWindow として検出器の前に置く。
        Layers,
        // 261103Claude: DTSA-II の XRayWindowFactory.WindowTypes にある既製の窓を、名前 (WindowPreset) で選ぶ。
        Preset
    }

    // 261030Claude: 窓の1層。窓は設定 JSON の手書きでしか指定できないため、単位を項目名に含めて取り違えを防ぐ。
    //   厚さは既存の AluminumLayer・GoldLayer と同じ nm で持つ。
    public sealed record DetectorWindowLayer
    {
        // 261103Claude: 端成分と同じ「元素記号＋個数」の並び。CompositionParser は合わない部分を黙って捨てるので、窓では式全体を読み切れることを先に確かめる。
        // 261106Codex: $は末尾改行の手前でも一致するため、入力の厳密な終端まで検査する。
        private static readonly Regex FormulaPattern = new("^(?:[A-Z][a-z]?(?:[0-9]+(?:\\.[0-9]+)?)?)+\\z", RegexOptions.CultureInvariant);

        public string Formula { get; init; } = string.Empty;
        public double DensityGPerCm3 { get; init; }
        public double ThicknessNm { get; init; }

        // 261103Claude: 窓の組成式を端成分と同じ書き方で読み、DTSA-II の createCompound には渡さない (DTSA-II の文法に合わせる必要をなくす)。
        //   同じ元素が複数回出れば個数を足し、createCompound と同じ原子番号順に並べる。元素表は端成分と同じ Crystallography のもの。
        public static bool TryParseFormula(string? formula, out (string Element, double Count)[] stoichiometry, out string error)
        {
            stoichiometry = [];
            if (string.IsNullOrEmpty(formula) || !FormulaPattern.IsMatch(formula))
            {
                error = $"組成式「{formula}」は、元素記号 (大文字 1 字と小文字 0〜1 字) と個数の並びで書いてください (例: C5H8O2)。括弧・空白・記号・全角文字は使えません。";
                return false;
            }

            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (element, count) in CompositionParser.ParseComposition(formula))
            {
                if (AtomStatic.AtomicNumber(element) == 0)
                {
                    error = $"組成式「{formula}」の元素記号 {element} がありません。";
                    return false;
                }

                if (!(count > 0))
                {
                    error = $"組成式「{formula}」の {element} の個数は 0 より大きくしてください。";
                    return false;
                }

                totals[element] = totals.GetValueOrDefault(element) + count;
            }

            stoichiometry = totals
                .OrderBy(static entry => AtomStatic.AtomicNumber(entry.Key))
                .Select(static entry => (entry.Key, entry.Value))
                .ToArray();
            error = string.Empty;
            return true;
        }
    }

    // 260626Codex: DTSA-IIの検出器物理設定を、DTSA-IIのDerby検出器DBではなくMineraScope設定で保持する。
    public sealed class DetectorProfile
    {
        public const string FileName = "detectorProfile.json";

        // 261103Claude: Custom Table は custom.csv を DTSA-II の作業フォルダーから読むので除き、No window は窓なし (NoWindow) と同じ物理なので除く。
        //   残りの名前は DTSA-II の版で増減しうるため一覧とは照合せず、無い名前は生成スクリプトが理由を付けて失敗する。
        private static readonly string[] ExcludedWindowPresets = ["Custom Table", "No window"];

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
        // 261103Claude: 組成を指定する窓の支持グリッドの開口率 (XRayWindow(openAreaFrac) の値)。null は 1.0 (グリッドなし) を表し、JSON に書かない。
        //   1.0 は読込・複製・登録のどの経路でも null にそろえ、同じグリッドなしの窓に条件キーが 2 つできないようにする。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? WindowOpenAreaFraction { get; set => field = value == 1.0 ? null : value; }
        // 261103Claude: 既製の窓の名前 (DTSA-II の XRayWindowFactory.WindowTypes)。既製の窓以外では null のまま JSON に書かない。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WindowPreset { get; set; }

        // 260803Codex: 永続化済みの 3 フィールドから検証済みの不変軸を返します。
        public SpectrumAxis GetSpectrumAxis() => new(ChannelCount, ZeroOffset, ChannelWidth);

        // 261103Claude: 名前は自由な文字列として扱い、書式 (装置名・日付・区切り文字) に依らない制限だけを課す。
        //   制御文字はログの 1 行と生成スクリプトの文字列を壊すので拒否する。
        public static string? GetNameError(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "検出器名が空です。";
            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
                return $"検出器名「{name}」の前後に空白があります。";

            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsControl(name[i]))
                    return "検出器名に制御文字 (改行・タブなど) が含まれています。";
                if (!char.IsSurrogate(name[i]))
                    continue;
                if (!char.IsSurrogatePair(name, i))
                    return "検出器名に不正な文字 (対になっていないサロゲート) が含まれています。";
                i++;
            }

            return null;
        }

        // 261103Claude: 登録した検出器の値を既定値で埋めずに拒否する。範囲は DTSA-II の検出器編集画面 (AppPreferences.SiLiPanel) に合わせ、
        //   距離だけは 0 も拒否する (距離 0 では立体角が決まらない)。一覧の読込・条件キーの作成・スクリプト生成の直前に呼ぶ。
        //   起動時の旧設定の読込 (CreateWithDefaults) では検査しない。
        public void Validate()
        {
            if (GetNameError(Name) is string nameError)
                throw new InvalidDataException(nameError);
            if (ChannelCount != SpectrumDataLoader.SpectrumLength)
                throw new InvalidDataException($"ChannelCount は学習・推論の入力長 {SpectrumDataLoader.SpectrumLength} にしてください。現在の値: {ChannelCount}");

            RequireRange(ChannelWidth, 0.1, 1000, nameof(ChannelWidth), "eV/ch");
            RequireRange(ZeroOffset, -10000, 10000, nameof(ZeroOffset), "eV");
            RequireRange(ResolutionFwhmAtMnKa, 120, 1000, nameof(ResolutionFwhmAtMnKa), "eV");
            RequireRange(Elevation, -90, 90, nameof(Elevation), "°");
            RequireRange(Azimuth, 0, 360, nameof(Azimuth), "°");
            RequireRange(OptimalWorkingDistance, 0, 1000, nameof(OptimalWorkingDistance), "mm", excludeMinimum: true);
            RequireRange(SpecimenToDetectorDistance, 0, 1000, nameof(SpecimenToDetectorDistance), "mm", excludeMinimum: true);
            RequireRange(DetectorArea, 0.01, 1000, nameof(DetectorArea), "mm²");
            RequireRange(SiThickness, 0.01, 100, nameof(SiThickness), "mm");
            RequireRange(GoldLayer, 0, 1000, nameof(GoldLayer), "nm");
            RequireRange(AluminumLayer, 0, 1000, nameof(AluminumLayer), "nm");
            RequireRange(NickelLayer, 0, 1000, nameof(NickelLayer), "nm");
            RequireRange(DeadLayer, 0, 1, nameof(DeadLayer), "µm");
            ValidateWindow();
        }

        private static void RequireRange(double value, double minimum, double maximum, string name, string unit, bool excludeMinimum = false)
        {
            if (double.IsFinite(value) && (excludeMinimum ? value > minimum : value >= minimum) && value <= maximum)
                return;

            string lower = excludeMinimum ? $"{minimum} より大きく" : $"{minimum} 以上";
            throw new InvalidDataException($"{name} ({unit}) は {lower} {maximum} 以下にしてください。現在の値: {value}");
        }

        // 261030Claude: 窓の不正な設定は既定値へ戻さずに拒否する。戻すと、窓なしのスペクトルを窓ありの条件として作ってしまう。
        // 261103Claude: 方式ごとに使わない項目が残っていれば拒否し、同じ物理に複数の書き方 (条件キー) ができないようにする。
        private void ValidateWindow()
        {
            // 261030Claude: 空の一覧も JSON に出ると条件キーが変わるため、組成指定以外では一覧そのものを持たせない。
            if (Window != DetectorWindowKind.Layers && WindowLayers is not null)
                throw new InvalidDataException($"検出器の窓が {Window} なのに WindowLayers が指定されています。組成を指定する窓では Window を {DetectorWindowKind.Layers} にしてください。");
            if (Window != DetectorWindowKind.Layers && WindowOpenAreaFraction is not null)
                throw new InvalidDataException($"支持グリッドの開口率 (WindowOpenAreaFraction) は、組成を指定する窓 ({DetectorWindowKind.Layers}) にだけ指定できます。");
            if (Window != DetectorWindowKind.Preset && WindowPreset is not null)
                throw new InvalidDataException($"検出器の窓が {Window} なのに WindowPreset が指定されています。既製の窓では Window を {DetectorWindowKind.Preset} にしてください。");

            switch (Window)
            {
                case DetectorWindowKind.NoWindow:
                    return;
                case DetectorWindowKind.Layers:
                    ValidateWindowLayers();
                    return;
                case DetectorWindowKind.Preset:
                    if (string.IsNullOrWhiteSpace(WindowPreset) || WindowPreset != WindowPreset.Trim())
                        throw new InvalidDataException("既製の窓の名前 (WindowPreset) が空か、前後に空白があります。");
                    if (ExcludedWindowPresets.Contains(WindowPreset, StringComparer.Ordinal))
                        throw new InvalidDataException($"既製の窓「{WindowPreset}」は使えません。Custom Table は作業フォルダーの custom.csv に依存し、No window は窓なし ({DetectorWindowKind.NoWindow}) で指定します。");
                    return;
                default:
                    throw new InvalidDataException($"検出器の窓の種類 {Window} は使えません。");
            }
        }

        private void ValidateWindowLayers()
        {
            if (WindowOpenAreaFraction is double openArea && !(double.IsFinite(openArea) && openArea > 0 && openArea <= 1))
                throw new InvalidDataException($"支持グリッドの開口率 (WindowOpenAreaFraction) は 0 より大きく 1 以下にしてください。現在の値: {openArea}");
            if (WindowLayers is null || WindowLayers.Count == 0)
                throw new InvalidDataException($"検出器の窓が {DetectorWindowKind.Layers} ですが、WindowLayers に層がありません。");

            for (int i = 0; i < WindowLayers.Count; i++)
            {
                var layer = WindowLayers[i]
                    ?? throw new InvalidDataException($"検出器の窓の層 {i + 1} が空です。");
                if (!DetectorWindowLayer.TryParseFormula(layer.Formula, out _, out string formulaError))
                    throw new InvalidDataException($"検出器の窓の層 {i + 1}: {formulaError}");
                if (!double.IsFinite(layer.DensityGPerCm3) || layer.DensityGPerCm3 <= 0)
                    throw new InvalidDataException($"検出器の窓の層 {i + 1} の密度 (g/cm³) は 0 より大きい有限値にしてください。");
                if (!double.IsFinite(layer.ThicknessNm) || layer.ThicknessNm <= 0)
                    throw new InvalidDataException($"検出器の窓の層 {i + 1} の厚さ (nm) は 0 より大きい有限値にしてください。");
            }
        }

        // 261103Claude: 生成・学習の開始時にログへ出す 1 行の要約。値は丸めずに invariant で書き、どの値で生成したかを後から確かめられるようにする。
        public string Describe()
        {
            string window = Window switch
            {
                DetectorWindowKind.Layers => "窓 "
                    + string.Join(" + ", (WindowLayers ?? []).Select(static layer => Invariant($"{layer.Formula} {layer.DensityGPerCm3} g/cm³ {layer.ThicknessNm} nm")))
                    + (WindowOpenAreaFraction is double openArea ? Invariant($" (開口率 {openArea})") : string.Empty),
                DetectorWindowKind.Preset => $"窓 DTSA-II の既製「{WindowPreset}」",
                _ => "窓なし"
            };
            return Invariant($"検出器「{Name}」: SDD {ChannelCount} ch, 幅 {ChannelWidth} eV/ch, ゼロ点 {ZeroOffset} eV, FWHM {ResolutionFwhmAtMnKa} eV, ")
                + Invariant($"仰角 {Elevation}°, 方位角 {Azimuth}°, 距離 {SpecimenToDetectorDistance} mm, 最適 WD {OptimalWorkingDistance} mm, 面積 {DetectorArea} mm², ")
                + Invariant($"Si {SiThickness} mm, Au {GoldLayer} nm, Al {AluminumLayer} nm, Ni {NickelLayer} nm, デッドレイヤ {DeadLayer} µm, ")
                + window;
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
                WindowLayers = WindowLayers?.ToList(),
                WindowOpenAreaFraction = WindowOpenAreaFraction,
                WindowPreset = WindowPreset
            };

        public void WriteToModelFolder(string modelFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelFolder);
            Directory.CreateDirectory(modelFolder);
            File.WriteAllText(Path.Combine(modelFolder, FileName), JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
