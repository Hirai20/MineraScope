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
        private const string NoWindowConditionKey = "dd4f4280a9d1195a1ff5c3c2371f8efddf449a5b21a99efd85842ec5d07d9157";
        private const string NoWindowScriptSha256 = "effaf2ffad1fa2d101ce0e1bb6dfe8858b095231776efb42f0b8b17b2015dd4b";

        private static readonly DetectorWindowLayer Polymer = new() { Formula = "C5H8O2", DensityGPerCm3 = 1.15, ThicknessNm = 30 };
        private static readonly DetectorWindowLayer Nitride = new() { Formula = "Si3N4", DensityGPerCm3 = 3.44, ThicknessNm = 40 };

        public static void Run()
        {
            RunNoWindowCompatibility();
            RunJsonRoundTrip();
            RunScriptOutput();
            RunInvalidWindowRefusal();
            RunConditionKeys();
            RunPoolReuse();
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
                var layers = Regex.Matches(script,
                    @"\twindow\.addLayer\(epq\.Material\(epq\.MaterialFactory\.createCompound\(""([^""]+)""\), epq\.ToSI\.gPerCC\(([^)]+)\)\), ([^)]+)\)\r?\n");
                TestAssert.SequenceEqual(["C5H8O2", "Si3N4", "Au", "Ni"], layers.Select(match => match.Groups[1].Value),
                    "Every window layer is added once and in order.");
                TestAssert.SequenceEqual([1.15, 3.44, 19.3, 8.9], layers.Select(match => ParseInvariant(match.Groups[2].Value)),
                    "Window densities are written in g/cm3 with invariant formatting.");
                TestAssert.SequenceEqual([30e-9, 40e-9, 10e-9, 5e-9], layers.Select(match => ParseInvariant(match.Groups[3].Value)),
                    "Window thicknesses are written in meters with invariant formatting.");

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
                ("NoWindow with an empty list", WithWindow(DetectorWindowKind.NoWindow, []))
            };

            foreach (var (name, profile) in invalidProfiles)
            {
                // 261030Claude: 起動時の設定読込は止めない。両経路は正規化してから検査するため、下の拒否は正規化が値を直していないことも示す。
                DetectorProfile.CreateWithDefaults(profile);
                TestAssert.Throws<InvalidDataException>(() => BuildKey(profile), $"A pool key must not be created for a window with {name}.");
                TestAssert.Throws<InvalidDataException>(() => GenerateScript(profile), $"A script must not be generated for a window with {name}.");
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
            using var fixture = new TestFixture();
            var solution = TestFixture.CreateSolution();
            SpectrumPoolHandle Resolve(DetectorProfile profile) =>
                fixture.Repository.ResolvePool(fixture.Request.Paths.SpectrumOutputFolder, solution, 0.5, Condition(profile));

            var saved = Resolve(Layered(Polymer));
            fixture.Repository.Save(saved, fixture.Repository.LoadOrCreate(saved));
            var upperCase = Resolve(Layered(Polymer with { Formula = "CO" }));
            fixture.Repository.Save(upperCase, fixture.Repository.LoadOrCreate(upperCase));

            var nearlySame = Layered(Polymer with { DensityGPerCm3 = Polymer.DensityGPerCm3 + 1e-12 });
            TestAssert.True(BuildKey(nearlySame) != saved.ConditionKey, "The tolerance case must reach the compatible-pool search.");
            TestAssert.Equal(saved.PoolFolder, Resolve(nearlySame).PoolFolder, "A pool with the same window within tolerance is reused.");
            TestAssert.True(Resolve(Layered(Polymer with { ThicknessNm = 40 })).PoolFolder != saved.PoolFolder,
                "A pool generated with a different window thickness must not be reused.");
            TestAssert.True(Resolve(Layered(Polymer, Nitride)).PoolFolder != saved.PoolFolder,
                "A pool generated with a different number of window layers must not be reused.");
            var lowerCase = Resolve(Layered(Polymer with { Formula = "Co" })).PoolFolder;
            TestAssert.True(lowerCase != upperCase.PoolFolder && lowerCase != saved.PoolFolder,
                "Window formulas are compared case-sensitively.");
            TestAssert.True(Resolve(DetectorProfile.CreateLegacyTest()).PoolFolder != saved.PoolFolder,
                "A windowless request must not reuse a windowed pool.");
        }

        private static DetectorProfile Layered(params DetectorWindowLayer[] layers) => WithWindow(DetectorWindowKind.Layers, [.. layers]);

        private static DetectorProfile WithWindow(DetectorWindowKind window, List<DetectorWindowLayer>? layers)
        {
            var profile = DetectorProfile.CreateLegacyTest();
            profile.Window = window;
            profile.WindowLayers = layers;
            return profile;
        }

        private static string BuildKey(DetectorProfile profile)
        {
            var builder = new SpectrumConditionKeyBuilder();
            return builder.BuildKey(builder.CreateSnapshot(TestFixture.CreateSolution(), 0.5, Condition(profile)));
        }

        private static SemEdxCondition Condition(DetectorProfile profile) => new(profile, 0.02, 15, 1, 1);

        // 261030Claude: 一時フォルダーに依存しない固定のパスで、スクリプト全文を比較できるようにする。
        private static string GenerateScript(DetectorProfile profile) =>
            new SimulationScriptGenerator().Generate(new SimulationProperty
            {
                MineralGroupName = "SyntheticOlivine",
                Atoms1 = [[("Mg", 0.3), ("Fe", 0.1), ("Si", 0.2), ("O", 0.4)]],
                DetectorName = profile.Name,
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

        private static string Sha256(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")))).ToLowerInvariant();
    }
}
