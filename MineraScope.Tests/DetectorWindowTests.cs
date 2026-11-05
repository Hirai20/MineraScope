// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MineraScope.Tests
{
    // 261030Claude: 検出器の窓の層が、窓なしの既存条件を変えずに条件キー・生成スクリプト・pool再利用へ反映されることを検証する。
    internal static class DetectorWindowTests
    {
        // 261030Claude: 窓の層を追加する前のコードで、下の固定入力から記録した値。
        // 261103Claude: 検出器の一覧のテストも同じ値で、移行した test の互換を確かめる。
        internal const string NoWindowConditionKey = "dd4f4280a9d1195a1ff5c3c2371f8efddf449a5b21a99efd85842ec5d07d9157";
        internal const string NoWindowScriptSha256 = "effaf2ffad1fa2d101ce0e1bb6dfe8858b095231776efb42f0b8b17b2015dd4b";

        private static readonly DetectorWindowLayer Polymer = new() { Formula = "C5H8O2", DensityGPerCm3 = 1.15, ThicknessNm = 30 };
        private static readonly DetectorWindowLayer Nitride = new() { Formula = "Si3N4", DensityGPerCm3 = 3.44, ThicknessNm = 40 };

        // 261103Claude: 窓の層 1 つ分。元素ごとの個数 (原子番号順に連結)、密度、厚さを取り出す。
        private static readonly Regex LayerPattern = new(
            @"\tlayer = epq\.Composition\(\)\r?\n(?:\t+layer\.addElementByStoiciometry\(epq\.Element\.(?<element>[A-Za-z]+), (?<count>[^)]+)\)\r?\n)+" +
            @"\t+window\.addLayer\(epq\.Material\(layer, epq\.ToSI\.gPerCC\((?<density>[^)]+)\)\), (?<thickness>[^)]+)\)\r?\n");

        public static void Run()
        {
            RunNoWindowCompatibility();
            RunJsonRoundTrip();
            RunScriptOutput();
            RunInvalidWindowRefusal();
            RunConditionKeys();
            RunPoolReuse();
            RunFormulaParsing();
            RunOpenAreaFraction();
            RunPresetWindow();
            RunValueRanges();
            RunExactDetectorScript();
            RunFailureReasons();
        }

        // 261030Claude: 窓なしでは、条件キーと生成スクリプトが追加前と完全に一致する。
        //   WindowLayers が JSON に出ればキーが、XRayWindow が出ればスクリプトのハッシュが変わるので、この2値で両方を確かめる。
        private static void RunNoWindowCompatibility()
        {
            var profile = DetectorProfile.CreateLegacyTest();
            TestAssert.Equal(NoWindowConditionKey, BuildKey(profile), "A windowless profile keeps the existing condition key.");
            TestAssert.Equal(NoWindowScriptSha256, Sha256(GenerateScript(profile)), "A windowless profile keeps the existing script.");
            var restored = JsonSerializer.Deserialize<DetectorProfile>(JsonSerializer.Serialize(profile))!;
            TestAssert.Equal(NoWindowConditionKey, BuildKey(restored), "A saved windowless profile keeps the existing condition key.");
        }

        // 261030Claude: 手書きの設定とmanifestの読み書きで、窓の層が順序と値を保つ。
        private static void RunJsonRoundTrip()
        {
            const string handWritten = """
                {
                  "Name": "calibrated",
                  "Window": "Layers",
                  "WindowLayers": [
                    { "Formula": "C5H8O2", "DensityGPerCm3": 1.15, "ThicknessNm": 30.0 }
                  ]
                }
                """;
            var parsed = JsonSerializer.Deserialize<DetectorProfile>(handWritten)!;
            TestAssert.Equal(DetectorWindowKind.Layers, parsed.Window, "The hand-written window kind is read.");
            TestAssert.SequenceEqual([Polymer], parsed.WindowLayers!, "The hand-written window layer is read.");

            var profile = Layered(Polymer, Nitride);
            string json = JsonSerializer.Serialize(profile);
            TestAssert.True(json.Contains("\"Window\":\"Layers\"", StringComparison.Ordinal), "The window kind is saved by name.");
            TestAssert.SequenceEqual([Polymer, Nitride], JsonSerializer.Deserialize<DetectorProfile>(json)!.WindowLayers!,
                "A saved profile keeps its window layers in order.");

            var condition = JsonSerializer.Deserialize<SemEdxCondition>(JsonSerializer.Serialize(Condition(profile)))!;
            TestAssert.SequenceEqual([Polymer, Nitride], condition.GetDetectorProfile().WindowLayers!,
                "A saved pool condition keeps its window layers.");

            var clone = profile.Clone();
            clone.WindowLayers!.Add(Polymer);
            TestAssert.Equal(2, profile.WindowLayers!.Count, "Changing a cloned layer list must not change the original.");
        }

        // 261030Claude: 窓ありでは、層の数だけ addLayer を invariant の数値で出力し、検出器の層を設定する前に窓を付ける。
        //   純 Au・Ni の窓層は setWindow で検出器側の GoldLayer・NickelLayer へ書き写されるため、検出器の値が窓の後に1回だけ設定されることを確かめる。
        // 261103Claude: 組成式は DTSA-II の createCompound に渡さず、元素ごとの個数を createCompound と同じ原子番号順に渡す。
        private static void RunScriptOutput()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var gold = new DetectorWindowLayer { Formula = "Au", DensityGPerCm3 = 19.3, ThicknessNm = 10 };
                var nickel = new DetectorWindowLayer { Formula = "Ni", DensityGPerCm3 = 8.9, ThicknessNm = 5 };
                var profile = Layered(Polymer, Nitride, gold, nickel);
                profile.GoldLayer = 6;
                profile.NickelLayer = 3;
                string script = GenerateScript(profile);
                var layers = LayerPattern.Matches(script);
                TestAssert.SequenceEqual(["H8C5O2", "N4Si3", "Au1", "Ni1"], layers.Select(Stoichiometry),
                    "Every window layer is added once and in order, with its elements in atomic-number order.");
                TestAssert.SequenceEqual([1.15, 3.44, 19.3, 8.9], layers.Select(match => ParseInvariant(match.Groups["density"].Value)),
                    "Window densities are written in g/cm3 with invariant formatting.");
                TestAssert.SequenceEqual([30e-9, 40e-9, 10e-9, 5e-9], layers.Select(match => ParseInvariant(match.Groups["thickness"].Value)),
                    "Window thicknesses are written in meters with invariant formatting.");
                TestAssert.True(!script.Contains("createCompound", StringComparison.Ordinal), "Window formulas are not parsed by DTSA-II.");

                int window = script.IndexOf("\twindow = epd.XRayWindow(1.0)", StringComparison.Ordinal);
                int setWindow = script.IndexOf("\tdet.setWindow(window)", StringComparison.Ordinal);
                int setName = script.IndexOf("dp.setName(", StringComparison.Ordinal);
                TestAssert.True(window >= 0 && window < layers[0].Index && layers[^1].Index < setWindow && setWindow < setName,
                    "The window is built, attached to the detector, and then the detector is named.");
                TestAssert.Equal(1, Regex.Count(script, "XRayWindow"), "Only one X-ray window is created.");

                foreach (var (property, value) in new[] { ("GoldLayer", 6.0), ("NickelLayer", 3.0) })
                {
                    var assignments = Regex.Matches(script, $@"\tsp\.setNumericProperty\(epq\.SpectrumProperties\.{property}, ([^)]+)\)");
                    TestAssert.True(assignments.Count == 1 && assignments[0].Index > setWindow,
                        $"The detector {property} is set once after the window, so a pure-metal window layer cannot replace it.");
                    TestAssert.Equal(value, ParseInvariant(assignments[0].Groups[1].Value), $"The detector {property} keeps its configured value.");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        // 261030Claude: 不正な窓は既定値へ戻さず、条件キーの作成とスクリプト生成の両方で拒否する。
        private static void RunInvalidWindowRefusal()
        {
            var invalidProfiles = new (string Name, DetectorProfile Profile)[]
            {
                ("Layers without a list", WithWindow(DetectorWindowKind.Layers, null)),
                ("Layers with an empty list", Layered()),
                ("a missing layer", Layered([null!])),
                ("an empty formula", Layered(Polymer with { Formula = "" })),
                ("a blank formula", Layered(Polymer with { Formula = "  " })),
                ("a formula with surrounding spaces", Layered(Polymer with { Formula = " C5H8O2" })),
                ("a zero density", Layered(Polymer with { DensityGPerCm3 = 0 })),
                ("a negative density", Layered(Polymer with { DensityGPerCm3 = -1.15 })),
                ("a NaN density", Layered(Polymer with { DensityGPerCm3 = double.NaN })),
                ("an infinite density", Layered(Polymer with { DensityGPerCm3 = double.PositiveInfinity })),
                ("a zero thickness", Layered(Polymer with { ThicknessNm = 0 })),
                ("a negative thickness", Layered(Polymer with { ThicknessNm = -30 })),
                ("a NaN thickness", Layered(Polymer with { ThicknessNm = double.NaN })),
                ("an infinite thickness", Layered(Polymer with { ThicknessNm = double.PositiveInfinity })),
                ("NoWindow with layers", WithWindow(DetectorWindowKind.NoWindow, [Polymer])),
                ("NoWindow with an empty list", WithWindow(DetectorWindowKind.NoWindow, [])),
                // 261103Claude: 端成分の解析が黙って捨てる部分を残した式は、組成が変わったまま生成しないよう拒否する。
                ("a lowercase formula", Layered(Polymer with { Formula = "c5h8o2" })),
                ("a lowercase element", Layered(Polymer with { Formula = "Si3n4" })),
                ("an unknown element", Layered(Polymer with { Formula = "Xx2" })),
                ("a formula with parentheses", Layered(Polymer with { Formula = "Ca5(PO4)3Cl" })),
                ("a trailing parenthesis", Layered(Polymer with { Formula = "C5H8O2)" })),
                ("a zero count", Layered(Polymer with { Formula = "C0H8O2" })),
                ("a full-width digit", Layered(Polymer with { Formula = "C５H8O2" })),
                ("an inner space", Layered(Polymer with { Formula = "C5 H8O2" })),
                ("a dangling decimal point", Layered(Polymer with { Formula = "Fe0.SiO3" })),
                ("a mixture", Layered(Polymer with { Formula = "SiO2+Al2O3" })),
                ("a scaled mixture", Layered(Polymer with { Formula = "0.5*SiO2" }))
            };

            foreach (var (name, profile) in invalidProfiles)
            {
                // 261030Claude: 起動時の設定読込は止めない。
                // 261103Claude: 不正な窓でも旧設定の読込 (CreateWithDefaults) は例外を出さず、条件キーとスクリプト生成で拒否する。
                DetectorProfile.CreateWithDefaults(profile);
                AssertRefused(profile, $"a window with {name}");
            }
        }

        // 261030Claude: 層の値・組成式の大小文字が違えば別の条件キーにし、表示名だけの違いは同じキーにする。
        private static void RunConditionKeys()
        {
            string layered = BuildKey(Layered(Polymer));
            TestAssert.True(layered != NoWindowConditionKey, "A window changes the condition key.");
            TestAssert.True(layered != BuildKey(Layered(Polymer with { ThicknessNm = 40 })), "A different window thickness changes the key.");
            TestAssert.True(layered != BuildKey(Layered(Polymer with { DensityGPerCm3 = 1.2 })), "A different window density changes the key.");
            TestAssert.True(layered != BuildKey(Layered(Polymer with { Formula = "C5H8O2N" })), "A different window formula changes the key.");
            TestAssert.True(BuildKey(Layered(Polymer, Nitride)) != BuildKey(Layered(Nitride, Polymer)), "The window layer order changes the key.");

            var renamed = Layered(Polymer);
            renamed.Name = "renamed";
            TestAssert.Equal(layered, BuildKey(renamed), "Only the display name differs, so the key stays the same.");
        }

        // 261030Claude: 条件キーが別でも物理条件が同じなら再利用する経路で、窓の層が違う pool を再利用しない。
        private static void RunPoolReuse()
        {
            // 261103Claude: pool の解決は他のテストと同じ ResolvePool を使う。
            using var fixture = new TestFixture();
            var saved = ResolvePool(fixture, Layered(Polymer));
            fixture.Repository.Save(saved, fixture.Repository.LoadOrCreate(saved));
            var upperCase = ResolvePool(fixture, Layered(Polymer with { Formula = "CO" }));
            fixture.Repository.Save(upperCase, fixture.Repository.LoadOrCreate(upperCase));

            var nearlySame = Layered(Polymer with { DensityGPerCm3 = Polymer.DensityGPerCm3 + 1e-12 });
            TestAssert.True(BuildKey(nearlySame) != saved.ConditionKey, "The tolerance case must reach the compatible-pool search.");
            TestAssert.Equal(saved.PoolFolder, ResolvePool(fixture, nearlySame).PoolFolder, "A pool with the same window within tolerance is reused.");
            TestAssert.True(ResolvePool(fixture, Layered(Polymer with { ThicknessNm = 40 })).PoolFolder != saved.PoolFolder,
                "A pool generated with a different window thickness must not be reused.");
            TestAssert.True(ResolvePool(fixture, Layered(Polymer, Nitride)).PoolFolder != saved.PoolFolder,
                "A pool generated with a different number of window layers must not be reused.");
            var lowerCase = ResolvePool(fixture, Layered(Polymer with { Formula = "Co" })).PoolFolder;
            TestAssert.True(lowerCase != upperCase.PoolFolder && lowerCase != saved.PoolFolder,
                "Window formulas are compared case-sensitively.");
            TestAssert.True(ResolvePool(fixture, DetectorProfile.CreateLegacyTest()).PoolFolder != saved.PoolFolder,
                "A windowless request must not reuse a windowed pool.");
        }

        // 261103Claude: 窓の組成式は端成分と同じ書き方で読み、同じ元素の個数を足して DTSA-II の createCompound と同じ原子番号順に並べる。
        private static void RunFormulaParsing()
        {
            foreach (var (formula, expected) in new (string Formula, string Expected)[]
            {
                ("C5H8O2", "H8C5O2"),
                ("Si3N4", "N4Si3"),
                ("Au", "Au1"),
                ("Ca5P3O12Cl", "O12P3Cl1Ca5"),
                ("KMg3AlSi3O10O2H2", "H2O12Mg3Al1Si3K1"),
                ("Fe0.5Mg1.5SiO4", "O4Mg1.5Si1Fe0.5"),
                ("CO", "C1O1"),
                ("Co", "Co1")
            })
            {
                TestAssert.True(DetectorWindowLayer.TryParseFormula(formula, out var stoichiometry, out string error), $"{formula} is accepted. {error}");
                TestAssert.Equal(expected, string.Concat(stoichiometry.Select(static item => item.Element + item.Count.ToString(CultureInfo.InvariantCulture))),
                    $"{formula} keeps its element counts in atomic-number order.");
            }
        }

        // 261103Claude: 開口率は省略で 1.0 (グリッドなし)。省略時は今までの窓ありの条件キーとスクリプトを保ち、値があれば JSON・条件キー・スクリプト・再利用判定に反映する。
        private static void RunOpenAreaFraction()
        {
            var withoutGrid = Layered(Polymer);
            var withGrid = Layered(Polymer);
            withGrid.WindowOpenAreaFraction = 0.77;

            TestAssert.True(!JsonSerializer.Serialize(withoutGrid).Contains(nameof(DetectorProfile.WindowOpenAreaFraction), StringComparison.Ordinal),
                "A window without a support grid does not write the open-area fraction.");
            TestAssert.True(JsonSerializer.Serialize(withGrid).Contains("\"WindowOpenAreaFraction\":0.77", StringComparison.Ordinal), "An open-area fraction is saved.");
            TestAssert.Equal(0.77, JsonSerializer.Deserialize<DetectorProfile>(JsonSerializer.Serialize(withGrid))!.WindowOpenAreaFraction,
                "A saved open-area fraction is read back.");
            TestAssert.True(BuildKey(withGrid) != BuildKey(withoutGrid), "An open-area fraction changes the condition key.");
            TestAssert.True(GenerateScript(withoutGrid).Contains("\twindow = epd.XRayWindow(1.0)", StringComparison.Ordinal),
                "A window without a support grid keeps XRayWindow(1.0).");
            var openArea = Regex.Match(GenerateScript(withGrid), @"\twindow = epd\.XRayWindow\(([^)]+)\)");
            TestAssert.Equal(0.77, ParseInvariant(openArea.Groups[1].Value), "The open-area fraction is passed to XRayWindow.");

            // 261103Claude: 明示した 1.0 は、代入でも手書きの JSON でも省略と同じ形になり、条件キーとスクリプトも省略時と同じになる。
            var explicitOne = WithOpenArea(Layered(Polymer), 1.0);
            TestAssert.True(explicitOne.WindowOpenAreaFraction is null, "An explicit 1.0 open area is stored as omitted.");
            TestAssert.Equal(BuildKey(withoutGrid), BuildKey(explicitOne), "An explicit 1.0 open area keeps the key of an omitted one.");
            TestAssert.Equal(GenerateScript(withoutGrid), GenerateScript(explicitOne), "An explicit 1.0 open area keeps the script of an omitted one.");
            string handWritten = JsonSerializer.Serialize(withoutGrid).Replace("\"Window\":", "\"WindowOpenAreaFraction\":1.0,\"Window\":", StringComparison.Ordinal);
            TestAssert.True(handWritten.Contains("\"WindowOpenAreaFraction\":1.0", StringComparison.Ordinal)
                && JsonSerializer.Deserialize<DetectorProfile>(handWritten)!.WindowOpenAreaFraction is null,
                "A hand-written 1.0 open area is read as omitted.");

            foreach (var (name, profile) in new (string Name, DetectorProfile Profile)[]
            {
                ("a zero open area", WithOpenArea(Layered(Polymer), 0)),
                ("a negative open area", WithOpenArea(Layered(Polymer), -0.5)),
                ("an open area above 1", WithOpenArea(Layered(Polymer), 1.5)),
                ("a NaN open area", WithOpenArea(Layered(Polymer), double.NaN)),
                ("an open area without a window", WithOpenArea(DetectorProfile.CreateLegacyTest(), 0.77)),
                ("an open area on a preset window", WithOpenArea(WithPreset("Beryllium (5 µm)"), 0.77))
            })
                AssertRefused(profile, name);

            using var fixture = new TestFixture();
            var saved = ResolvePool(fixture, withGrid);
            fixture.Repository.Save(saved, fixture.Repository.LoadOrCreate(saved));
            var nearlySame = WithOpenArea(Layered(Polymer), 0.77 + 1e-12);
            TestAssert.True(BuildKey(nearlySame) != saved.ConditionKey, "The open-area tolerance case must reach the compatible-pool search.");
            TestAssert.Equal(saved.PoolFolder, ResolvePool(fixture, nearlySame).PoolFolder, "A pool with the same open area within tolerance is reused.");
            TestAssert.True(ResolvePool(fixture, withoutGrid).PoolFolder != saved.PoolFolder, "A pool with a support grid is not reused without one.");
            TestAssert.True(ResolvePool(fixture, WithOpenArea(Layered(Polymer), 0.75)).PoolFolder != saved.PoolFolder,
                "A pool with a different open area is not reused.");
        }

        // 261103Claude: 既製の窓は DTSA-II の名前で作り、µ を含む名前も ASCII だけのスクリプトで正しく渡す。その版に無い名前は理由を付けて失敗させる。
        private static void RunPresetWindow()
        {
            const string escapedName = "u\"Beryllium (5 \\u00b5m)\"";
            var beryllium = WithPreset("Beryllium (5 µm)");
            string script = GenerateScript(beryllium);
            TestAssert.True(script.Contains("\twindow = epd.XRayWindowFactory.createWindow(" + escapedName + ")", StringComparison.Ordinal),
                "A preset window is created by its DTSA-II name as a unicode literal.");
            TestAssert.True(script.Contains("\tif window is None:", StringComparison.Ordinal)
                && script.Contains("\t\traise Exception(\"DTSA-II has no X-ray window named \" + repr(" + escapedName + "))", StringComparison.Ordinal),
                "A preset name missing from the installed DTSA-II fails with a reason.");
            TestAssert.True(script.All(static c => c < 128), "The generated script stays ASCII.");
            TestAssert.True(!script.Contains("XRayWindow(", StringComparison.Ordinal), "A preset window does not build a layered window.");
            int create = script.IndexOf("createWindow(", StringComparison.Ordinal);
            int setWindow = script.IndexOf("\tdet.setWindow(window)", StringComparison.Ordinal);
            int gold = script.IndexOf("SpectrumProperties.GoldLayer", StringComparison.Ordinal);
            TestAssert.True(create >= 0 && create < setWindow && setWindow < gold, "A preset window is attached before the detector layers are set.");

            string json = JsonSerializer.Serialize(beryllium);
            TestAssert.True(json.Contains("\"Window\":\"Preset\"", StringComparison.Ordinal) && !json.Contains("WindowLayers", StringComparison.Ordinal),
                "A preset window is saved by kind without layers.");
            TestAssert.Equal("Beryllium (5 µm)", JsonSerializer.Deserialize<DetectorProfile>(json)!.WindowPreset, "A saved preset name is read back.");
            TestAssert.True(BuildKey(beryllium) != NoWindowConditionKey, "A preset window changes the condition key.");
            TestAssert.True(BuildKey(beryllium) != BuildKey(WithPreset("Beryllium (8 µm)")), "A different preset changes the condition key.");

            var withLayers = WithPreset("Beryllium (5 µm)");
            withLayers.WindowLayers = [Polymer];
            var noWindowWithPreset = DetectorProfile.CreateLegacyTest();
            noWindowWithPreset.WindowPreset = "Beryllium (5 µm)";
            var layeredWithPreset = Layered(Polymer);
            layeredWithPreset.WindowPreset = "Beryllium (5 µm)";
            foreach (var (name, profile) in new (string Name, DetectorProfile Profile)[]
            {
                ("the Custom Table preset", WithPreset("Custom Table")),
                ("the No window preset", WithPreset("No window")),
                ("an empty preset name", WithPreset("")),
                ("a missing preset name", WithPreset(null)),
                ("a preset name with surrounding spaces", WithPreset(" Beryllium (5 µm)")),
                ("a preset with layers", withLayers),
                ("NoWindow with a preset name", noWindowWithPreset),
                ("Layers with a preset name", layeredWithPreset)
            })
                AssertRefused(profile, name);

            using var fixture = new TestFixture();
            var saved = ResolvePool(fixture, beryllium);
            fixture.Repository.Save(saved, fixture.Repository.LoadOrCreate(saved));
            TestAssert.Equal(saved.PoolFolder, ResolvePool(fixture, WithPreset("Beryllium (5 µm)")).PoolFolder, "The same preset reuses its pool.");
            TestAssert.True(ResolvePool(fixture, WithPreset("Beryllium (8 µm)")).PoolFolder != saved.PoolFolder, "A different preset does not reuse the pool.");
            TestAssert.True(ResolvePool(fixture, DetectorProfile.CreateLegacyTest()).PoolFolder != saved.PoolFolder, "A windowless request does not reuse a preset pool.");
        }

        // 261103Claude: 登録した検出器の値は DTSA-II の検出器編集画面の範囲で検査し、範囲外・非有限値を既定値で埋めずに拒否する。
        private static void RunValueRanges()
        {
            var cases = new (string Name, Action<DetectorProfile, double> Set, double[] Accepted, double[] Rejected)[]
            {
                (nameof(DetectorProfile.ChannelWidth), static (p, v) => p.ChannelWidth = v, [0.1, 9.875, 1000], [0, 0.0999, 1000.001]),
                (nameof(DetectorProfile.ZeroOffset), static (p, v) => p.ZeroOffset = v, [-10000, -1, 0, 10000], [-10000.1, 10000.1]),
                (nameof(DetectorProfile.ResolutionFwhmAtMnKa), static (p, v) => p.ResolutionFwhmAtMnKa = v, [120, 130.25, 1000], [0, 119.9, 1000.1]),
                (nameof(DetectorProfile.Elevation), static (p, v) => p.Elevation = v, [-90, 35, 90], [-90.1, 90.1]),
                (nameof(DetectorProfile.Azimuth), static (p, v) => p.Azimuth = v, [0, 90, 360], [-0.1, 360.1]),
                (nameof(DetectorProfile.OptimalWorkingDistance), static (p, v) => p.OptimalWorkingDistance = v, [0.001, 10, 1000], [0, -1, 1000.1]),
                (nameof(DetectorProfile.SpecimenToDetectorDistance), static (p, v) => p.SpecimenToDetectorDistance = v, [0.001, 50, 1000], [0, -1, 1000.1]),
                (nameof(DetectorProfile.DetectorArea), static (p, v) => p.DetectorArea = v, [0.01, 30, 1000], [0, 0.0099, 1000.1]),
                (nameof(DetectorProfile.SiThickness), static (p, v) => p.SiThickness = v, [0.01, 0.8, 100], [0, 0.0099, 100.1]),
                (nameof(DetectorProfile.GoldLayer), static (p, v) => p.GoldLayer = v, [0, 12, 1000], [-0.1, 1000.1]),
                (nameof(DetectorProfile.AluminumLayer), static (p, v) => p.AluminumLayer = v, [0, 80, 1000], [-0.1, 1000.1]),
                (nameof(DetectorProfile.NickelLayer), static (p, v) => p.NickelLayer = v, [0, 1000], [-0.1, 1000.1]),
                (nameof(DetectorProfile.DeadLayer), static (p, v) => p.DeadLayer = v, [0, 0.2, 1], [-0.01, 1.01])
            };
            foreach (var (name, set, accepted, rejected) in cases)
            {
                foreach (double value in accepted)
                {
                    var profile = DetectorProfile.CreateLegacyTest();
                    set(profile, value);
                    profile.Validate();
                }

                foreach (double value in rejected.Concat([double.NaN, double.PositiveInfinity, double.NegativeInfinity]))
                {
                    var profile = DetectorProfile.CreateLegacyTest();
                    set(profile, value);
                    AssertRefused(profile, $"{name} = {value}");
                }
            }

            foreach (int channelCount in new[] { 0, 2047, 2049, 4096 })
            {
                var profile = DetectorProfile.CreateLegacyTest();
                profile.ChannelCount = channelCount;
                AssertRefused(profile, $"{channelCount} channels");
            }
        }

        // 261103Claude: 合成入力の軸と窓の層順を確認する。
        // 261106Codex: 公開用の検証値には任意の例を使う。
        internal static DetectorProfile CreateExampleProfile() =>
            new()
            {
                Name = "Example SDD",
                ChannelCount = 2048,
                ChannelWidth = 9.875,
                ZeroOffset = -1.0,
                ResolutionFwhmAtMnKa = 130.25,
                SiThickness = 0.45,
                AluminumLayer = 20,
                GoldLayer = 6,
                NickelLayer = 0,
                DeadLayer = 0,
                Elevation = 35,
                Azimuth = 90,
                SpecimenToDetectorDistance = 50,
                OptimalWorkingDistance = 10,
                DetectorArea = 30,
                Window = DetectorWindowKind.Layers,
                WindowLayers = [new() { Formula = "C5H8O2", DensityGPerCm3 = 1.15, ThicknessNm = 60 }]
            };

        private static void RunExactDetectorScript()
        {
            string script = GenerateScript(CreateExampleProfile());
            var detector = Regex.Match(script, @"createSDDDetector\(([0-9]+), ([^,]+), ([^,]+), ([^)]+)\)");
            TestAssert.Equal("2048", detector.Groups[1].Value, "The example detector has 2048 channels.");
            TestAssert.Equal(9.875, ParseInvariant(detector.Groups[2].Value), "The channel width is passed without rounding.");
            TestAssert.Equal(-1.0, ParseInvariant(detector.Groups[3].Value), "The zero offset is passed without rounding.");
            TestAssert.Equal(130.25, ParseInvariant(detector.Groups[4].Value), "The resolution is passed without rounding.");
            TestAssert.True(script.Contains("\twindow = epd.XRayWindow(1.0)", StringComparison.Ordinal), "The example window has no support grid.");

            var layers = LayerPattern.Matches(script);
            TestAssert.SequenceEqual(["H8C5O2"], layers.Select(Stoichiometry), "The example window is one C5H8O2 layer.");
            TestAssert.Equal(1.15, ParseInvariant(layers[0].Groups["density"].Value), "The window density is 1.15 g/cm3.");
            TestAssert.Equal(6e-08, ParseInvariant(layers[0].Groups["thickness"].Value), "The window thickness is 6e-08 m.");
            int setWindow = script.IndexOf("\tdet.setWindow(window)", StringComparison.Ordinal);
            TestAssert.True(setWindow > layers[0].Index && setWindow < script.IndexOf("SpectrumProperties.AluminumLayer", StringComparison.Ordinal),
                "The window is attached before the detector layers are set.");

            foreach (var (property, value) in new[] { ("DetectorThickness", 0.45), ("AluminumLayer", 20.0), ("GoldLayer", 6.0), ("NickelLayer", 0.0), ("DeadLayer", 0.0), ("DetectorArea", 30.0) })
            {
                var assignment = Regex.Match(script, $@"\tsp\.setNumericProperty\(epq\.SpectrumProperties\.{property}, ([^)]+)\)");
                TestAssert.Equal(value, ParseInvariant(assignment.Groups[1].Value), $"The example {property} is passed unchanged.");
            }

            TestAssert.True(script.Contains("\tdp.setName(\"Example SDD\")", StringComparison.Ordinal), "The detector is named as registered.");
        }

        // 261103Claude: C# で拾えない失敗の理由を、生成スクリプトが stderr へ書いた印付きの行から取り出す。
        //   実際の stderr は Java の警告が先に並ぶので、その形で印の行だけが取り出されることを確かめる。
        private static void RunFailureReasons()
        {
            string script = GenerateScript(DetectorProfile.CreateLegacyTest());
            TestAssert.True(script.Contains("\tSystem.err.println(\"MINERASCOPE_ERROR|\" + str(ex))", StringComparison.Ordinal),
                "The generated script prints its error with the tag that is searched for.");
            TestAssert.Equal("DTSA-II has no X-ray window named 'X'",
                SimulationScriptGenerator.FindScriptError("WARNING: A restricted method in java.lang.System has been called\r\n"
                    + "org.python warning: cp932\n"
                    + "  MINERASCOPE_ERROR|DTSA-II has no X-ray window named 'X'  \r\n"),
                "The script error line is found after the Java warnings.");
            TestAssert.Equal("second", SimulationScriptGenerator.FindScriptError("MINERASCOPE_ERROR|first\nMINERASCOPE_ERROR|second\n"),
                "The last script error line is used.");
            TestAssert.True(SimulationScriptGenerator.FindScriptError("WARNING: noise\r\nTraceback (most recent call last):\n") is null,
                "Without a script error line, nothing is found and the job failure message stays as before.");
            TestAssert.True(SimulationScriptGenerator.FindScriptError(string.Empty) is null, "An empty stderr has no script error.");
        }

        private static string Stoichiometry(Match match) =>
            string.Concat(match.Groups["element"].Captures.Zip(match.Groups["count"].Captures, static (element, count) => element.Value + count.Value));

        private static DetectorProfile WithOpenArea(DetectorProfile profile, double openArea)
        {
            profile.WindowOpenAreaFraction = openArea;
            return profile;
        }

        private static DetectorProfile WithPreset(string? preset)
        {
            var profile = DetectorProfile.CreateLegacyTest();
            profile.Window = DetectorWindowKind.Preset;
            profile.WindowPreset = preset;
            return profile;
        }

        // 261103Claude: 不正な検出器では、条件キーの作成とスクリプト生成の両方が拒否する。
        private static void AssertRefused(DetectorProfile profile, string description)
        {
            TestAssert.Throws<InvalidDataException>(() => BuildKey(profile), $"A pool key must not be created for {description}.");
            TestAssert.Throws<InvalidDataException>(() => GenerateScript(profile), $"A script must not be generated for {description}.");
        }

        private static SpectrumPoolHandle ResolvePool(TestFixture fixture, DetectorProfile profile) =>
            fixture.Repository.ResolvePool(fixture.Request.Paths.SpectrumOutputFolder, TestFixture.CreateSolution(), 0.5, Condition(profile));

        private static DetectorProfile Layered(params DetectorWindowLayer[] layers) => WithWindow(DetectorWindowKind.Layers, [.. layers]);

        private static DetectorProfile WithWindow(DetectorWindowKind window, List<DetectorWindowLayer>? layers)
        {
            var profile = DetectorProfile.CreateLegacyTest();
            profile.Window = window;
            profile.WindowLayers = layers;
            return profile;
        }

        internal static string BuildKey(DetectorProfile profile)
        {
            var builder = new SpectrumConditionKeyBuilder();
            return builder.BuildKey(builder.CreateSnapshot(TestFixture.CreateSolution(), 0.5, Condition(profile)));
        }

        private static SemEdxCondition Condition(DetectorProfile profile) => new(profile, 0.02, 15, 1, 1);

        // 261030Claude: 一時フォルダーに依存しない固定のパスで、スクリプト全文を比較できるようにする。
        internal static string GenerateScript(DetectorProfile profile) =>
            new SimulationScriptGenerator().Generate(new SimulationProperty
            {
                MineralGroupName = "SyntheticOlivine",
                Atoms1 = [[("Mg", 0.3), ("Fe", 0.1), ("Si", 0.2), ("O", 0.4)]],
                DetectorProfile = profile,
                CarbonCoatThickness = 0.02,
                BeamEnergy = 15,
                LiveTime = 1,
                ProbeCurrent = 1,
                ParallelCount = 1,
                OutputFolder = @"C:\MineraScopeWindowTest\out",
                OutputFiles = ["a.msa"]
            });

        private static double ParseInvariant(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static string Sha256(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")))).ToLowerInvariant();
    }
}
