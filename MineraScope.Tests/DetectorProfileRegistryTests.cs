// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MineraScope.Tests
{
    // 261103Claude: 検出器名から登録済みの値を引く一覧を、DTSA-II を起動せずに検証する。
    //   名前は書式に依らない自由な文字列として扱い、移行した test は今の条件キーと生成スクリプトを保つことを確かめる。
    internal static class DetectorProfileRegistryTests
    {
        // 261103Claude: 旧形式の設定を模した合成入力。利用者の設定ファイルは読まない。
        private const string CurrentSettingsJson = """
            {
              "Version": 1,
              "DetectorName": "test",
              "DetectorProfile": {
                "Name": "test",
                "ChannelCount": 2048,
                "ChannelWidth": 10,
                "ZeroOffset": 0,
                "ResolutionFwhmAtMnKa": 126,
                "DetectorArea": 30,
                "Elevation": 35,
                "Azimuth": 90,
                "SpecimenToDetectorDistance": 50,
                "OptimalWorkingDistance": 10,
                "SiThickness": 0.45,
                "AluminumLayer": 20,
                "GoldLayer": 6,
                "NickelLayer": 0,
                "DeadLayer": 0.2,
                "Window": "NoWindow"
              },
              "ModelName": "SyntheticModel",
              "TargetSpectrumCount": 1000,
              "ParallelCount": 20,
              "Resolution": 10,
              "Epochs": 500,
              "BatchSize": 16,
              "UnknownDistanceScale": 1,
              "EarlyStopping": 10,
              "ValidationSplit": 20,
              "ProbeCurrent": 0.5,
              "LiveTime": 50,
              "MeasurementTimePresetBEnabled": false,
              "BeamEnergy": 20,
              "CarbonThickness": 0.02,
              "TrainingMethod": "Standard"
            }
            """;

        public static void Run()
        {
            RunCurrentSettingsMigration();
            RunMigrationCases();
            RunFreeFormNames();
            RunRejectedNames();
            RunDuplicateNames();
            RunUnreadableLists();
            RunReloadReasons();
            // 261105Codex: 一覧の必須項目と、共用プロファイルの旧形式互換を別々に検証する。
            RunMissingFieldsRefusal();
            RunExplicitZerosAndOptionalFields();
            RunLegacyProfileCompatibility();
            RunInvalidEntries();
            RunRegistration();
            RunSettingsFormat();
            // 261103Claude: 壊れた一覧の読込警告は、この検証の中で意図して出したものなので後に残さない。
            FormUserSettingsStore.DrainWarnings();
        }

        // 261103Claude: 今の設定の検出器を、その名前 (test) のまま一覧へ移し、条件キーと生成スクリプトが追加前と同じになる。
        private static void RunCurrentSettingsMigration()
        {
            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            TestAssert.True(registry.WasStored && registry.LoadError is null, "The current detector is migrated into a new list.");
            TestAssert.SequenceEqual(["test"], registry.Names, "The migrated detector keeps its saved name.");
            TestAssert.True(registry.TryResolve("test", out var profile, out string error), error);
            TestAssert.Equal(DetectorWindowTests.NoWindowConditionKey, DetectorWindowTests.BuildKey(profile), "The migrated detector keeps the existing condition key.");
            TestAssert.Equal(DetectorWindowTests.NoWindowScriptSha256, DetectorWindowTests.Sha256(DetectorWindowTests.GenerateScript(profile)),
                "The migrated detector keeps the existing script.");

            string stored = File.ReadAllText(ListPath(fixture.Root));
            foreach (string field in new[] { "WindowLayers", "WindowOpenAreaFraction", "WindowPreset" })
                TestAssert.True(!stored.Contains(field, StringComparison.Ordinal), $"The migrated list does not write {field}.");

            var renamedLegacy = Loaded(CurrentSettingsJson.Replace("\"Name\": \"test\"", "\"Name\": \"other\"", StringComparison.Ordinal));
            TestAssert.SequenceEqual(["test"], DetectorProfileRegistry.Load(renamedLegacy, fixture.Root).Names, "An existing list is not migrated again.");

            // 261103Claude: GUI の操作ごとの読み直しは手で書き換えた一覧を反映し、一覧が消えても起動時の設定から作り直さない。
            WriteList(fixture.Root, Named("edited"));
            TestAssert.SequenceEqual(["edited"], registry.Reload().Names, "Reloading picks up a hand-edited list.");
            File.Delete(ListPath(fixture.Root));
            File.Delete(DurableJsonFile.GetBackupPath(ListPath(fixture.Root)));
            var deleted = registry.Reload();
            TestAssert.True(!File.Exists(ListPath(fixture.Root))
                && !deleted.TryResolve("test", out _, out string deletedError) && deletedError.Contains(DetectorProfileRegistry.FileName, StringComparison.Ordinal),
                "Reloading after the list is deleted reports it instead of migrating again.");
            // 261104Claude: 一度読めた一覧は読み直しに失敗しても WasStored を戻さず、終了時の保存が旧形式の値を書き戻さない。
            TestAssert.True(deleted.WasStored && deleted.Reload().WasStored, "A failed reload does not undo the migration when settings are saved.");
        }

        // 261103Claude: 移行は旧設定の形ごとに今までの読込規則の値を使い、読めない設定や一覧から引く形式の設定からは作らない。
        private static void RunMigrationCases()
        {
            using var fixture = new TestFixture();

            var nameOnly = DetectorProfileRegistry.Load(Loaded("""{ "Version": 1, "DetectorName": "旧 検出器" }"""), Folder(fixture, "name-only"));
            TestAssert.SequenceEqual(["旧 検出器"], nameOnly.Names, "A name-only setting is migrated under its saved name.");
            TestAssert.True(nameOnly.TryResolve("旧 検出器", out var nameOnlyProfile, out _)
                && DetectorWindowTests.BuildKey(nameOnlyProfile) == DetectorWindowTests.NoWindowConditionKey,
                "A name-only setting keeps the values it has been generated with.");

            var fresh = DetectorProfileRegistry.Load(new(new GeneratorFormUserSettings(), UserSettingsLoadOutcome.Missing), Folder(fixture, "fresh"));
            TestAssert.SequenceEqual([GeneratorFormUserSettings.CreateInitialDefaults().GetSelectedDetectorName()], fresh.Names,
                "A first launch registers the initial default detector.");

            string unreadableFolder = Folder(fixture, "unreadable");
            var unreadable = DetectorProfileRegistry.Load(new(new GeneratorFormUserSettings(), UserSettingsLoadOutcome.FellBackToDefaults), unreadableFolder);
            TestAssert.True(!unreadable.WasStored && unreadable.LoadError is not null && unreadable.Names.Count == 0 && !File.Exists(ListPath(unreadableFolder)),
                "An unreadable setting does not create a list from defaults.");

            string listFormatFolder = Folder(fixture, "list-format");
            var listFormat = DetectorProfileRegistry.Load(
                Loaded("""{ "Version": 1, "DetectorName": "Example SDD", "UsesDetectorProfileList": true }"""), listFormatFolder);
            TestAssert.True(!listFormat.WasStored && !listFormat.Reload().WasStored && !File.Exists(ListPath(listFormatFolder)),
                "A setting saved for the list is not migrated as a name-only setting.");
            TestAssert.True(!listFormat.TryResolve("Example SDD", out _, out string listFormatError) && listFormatError.Contains(listFormat.LoadError!, StringComparison.Ordinal),
                "A missing list is reported instead of using default values.");

            string invalidFolder = Folder(fixture, "invalid");
            var invalid = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson.Replace("\"ChannelCount\": 2048", "\"ChannelCount\": 4096", StringComparison.Ordinal)), invalidFolder);
            TestAssert.True(!invalid.WasStored && invalid.LoadError!.Contains("ChannelCount", StringComparison.Ordinal) && !File.Exists(ListPath(invalidFolder)),
                "Invalid current values are not migrated.");
        }

        // 261103Claude: 名前の書式 (装置名・日付・区切り) に依らず、日本語・空白・記号を含む名前でも登録・切り替え・生成スクリプトが動く。
        private static void RunFreeFormNames()
        {
            var expectedPythonNames = new (string Name, string Literal)[]
            {
                ("検出器 テスト①", "u\"\\u691c\\u51fa\\u5668 \\u30c6\\u30b9\\u30c8\\u2460\""),
                ("Detector / Example", "\"Detector / Example\""),
                ("a\\b:*?\"<>|", "\"a\\\\b:*?\\\"<>|\""),
                ("日本語", "u\"\\u65e5\\u672c\\u8a9e\"")
            };

            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            foreach (var (name, _) in expectedPythonNames)
                TestAssert.Equal(DetectorProfileRegistrationOutcome.Added, registry.Register(Named(name), overwrite: false).Outcome, $"「{name}」 is registered.");

            var calibrated = DetectorWindowTests.CreateExampleProfile();
            calibrated.Name = "校正 #2 (2026/11)";
            registry.Register(calibrated, overwrite: false);

            var reloaded = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            foreach (var (name, literal) in expectedPythonNames)
            {
                TestAssert.True(reloaded.TryResolve(name, out var profile, out string error), error);
                TestAssert.Equal(name, profile.Name, $"「{name}」 resolves to itself.");
                TestAssert.Equal(DetectorWindowTests.NoWindowConditionKey, DetectorWindowTests.BuildKey(profile), $"「{name}」 only differs by name, so the key stays.");
                string script = DetectorWindowTests.GenerateScript(profile);
                TestAssert.True(script.All(static c => c < 128), $"The script for 「{name}」 stays ASCII.");
                TestAssert.True(script.Contains("\tdp.setName(" + literal + ")", StringComparison.Ordinal), $"The script names the detector 「{name}」 exactly.");
            }

            TestAssert.True(reloaded.TryResolve("校正 #2 (2026/11)", out var switched, out string switchError), switchError);
            TestAssert.Equal(9.875, switched.ChannelWidth, "Another name switches to its own values.");
            TestAssert.True(DetectorWindowTests.BuildKey(switched) != DetectorWindowTests.NoWindowConditionKey, "Another detector's values give another key.");
            TestAssert.True(reloaded.TryResolve("  校正 #2 (2026/11)　", out _, out _), "Surrounding spaces in the typed name are ignored.");
            TestAssert.True(!reloaded.TryResolve("未登録の検出器", out var missing, out string missingError) && missing is null && missingError.Contains("未登録の検出器", StringComparison.Ordinal),
                "An unregistered name is refused without default values.");
        }

        // 261103Claude: 書式に依らない制限 (空・前後の空白・制御文字・不正な文字) だけを拒否する。
        private static void RunRejectedNames()
        {
            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            foreach (string name in new[] { "", "   ", " test2", "test2 ", "test2　", "a\nb", "a\tb", "a\u0000b", "\ud800x", "x\udc00" })
                TestAssert.Throws<InvalidDataException>(() => registry.Register(Named(name), overwrite: false), $"The name {JsonSerializer.Serialize(name)} is refused.");

            foreach (string name in new[] { "", "a\nb", "a\tb", "\ud800x" })
                TestAssert.True(!registry.TryResolve(name, out _, out _), $"The name {JsonSerializer.Serialize(name)} is not looked up.");
            TestAssert.SequenceEqual(["test"], registry.Names, "Refused names are not saved.");
        }

        // 261103Claude: 大文字・小文字と全角・半角だけが違う名前は登録させず、引くときは読み替えずに候補を示す。
        private static void RunDuplicateNames()
        {
            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), Folder(fixture, "registered"));
            registry.Register(Named("JED"), overwrite: false);
            foreach (string name in new[] { "Test", "TEST", "ｔｅｓｔ", "jed", "ＪＥＤ" })
                TestAssert.Throws<InvalidDataException>(() => registry.Register(Named(name), overwrite: false), $"「{name}」 is refused as a duplicate.");

            TestAssert.True(!registry.TryResolve("TEST", out _, out string caseError) && caseError.Contains("「test」", StringComparison.Ordinal),
                "A name differing only by case is not substituted, and the registered name is suggested.");
            TestAssert.True(!registry.TryResolve("ＪＥＤ", out _, out string widthError) && widthError.Contains("「JED」", StringComparison.Ordinal),
                "A name differing only by width is not substituted, and the registered name is suggested.");

            string handWritten = Folder(fixture, "hand-written");
            WriteList(handWritten, Named("x"), Named("X"));
            var duplicated = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), handWritten);
            TestAssert.True(!duplicated.TryResolve("x", out _, out _) && !duplicated.TryResolve("X", out _, out _),
                "Hand-written names that differ only by case are both refused.");
        }

        // 261103Claude: 壊れた一覧は退避コピーから戻し、どちらも読めなければ既定値で引かず保存もしない。綴りを誤った項目も既定値で読み飛ばさない。
        private static void RunUnreadableLists()
        {
            using var fixture = new TestFixture();

            string recovered = Folder(fixture, "recovered");
            File.WriteAllText(ListPath(recovered), "{ broken");
            File.WriteAllText(DurableJsonFile.GetBackupPath(ListPath(recovered)), ListJson(Named("from-backup")));
            TestAssert.True(DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), recovered).TryResolve("from-backup", out _, out string recoveredError), recoveredError);

            string broken = Folder(fixture, "broken");
            File.WriteAllText(ListPath(broken), "{ broken");
            File.WriteAllText(DurableJsonFile.GetBackupPath(ListPath(broken)), "{ broken");
            var unreadable = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), broken);
            TestAssert.True(!unreadable.TryResolve("test", out _, out string brokenError) && brokenError.Contains(unreadable.LoadError!, StringComparison.Ordinal),
                "An unreadable list is reported instead of migrating or using defaults.");
            TestAssert.Throws<InvalidOperationException>(() => unreadable.Register(Named("new"), overwrite: false), "An unreadable list is not overwritten.");

            string misspelled = Folder(fixture, "misspelled");
            File.WriteAllText(ListPath(misspelled), ListJson(Named("typo")).Replace("\"ChannelWidth\"", "\"ChanelWidth\"", StringComparison.Ordinal));
            TestAssert.True(!DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), misspelled).TryResolve("typo", out _, out _),
                "A misspelled field is not read as its default value.");
        }

        // 261104Claude: GUI が警告を引き取るのは起動時だけなので、読み直しは読めない理由を起動時の通知に積まず、LoadError で返す。
        private static void RunReloadReasons()
        {
            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            string listPath = ListPath(fixture.Root);
            string backupPath = DurableJsonFile.GetBackupPath(listPath);
            string misspelled = ListJson(Named("typo")).Replace("\"ChannelWidth\"", "\"ChanelWidth\"", StringComparison.Ordinal);
            FormUserSettingsStore.DrainWarnings();

            File.WriteAllText(listPath, misspelled);
            File.WriteAllText(backupPath, ListJson(Named("from-backup")));
            var recovered = registry.Reload();
            TestAssert.True(recovered.TryResolve("from-backup", out _, out string recoveredError), recoveredError);
            TestAssert.True(recovered.LoadError!.Contains("ChanelWidth", StringComparison.Ordinal), "Using the backup list reports why the main list was not read.");
            TestAssert.True(!recovered.TryResolve("typo", out _, out string missingError) && missingError.Contains(recovered.LoadError, StringComparison.Ordinal),
                "A name missing from the backup list explains that the main list was not read.");

            File.WriteAllText(backupPath, misspelled);
            TestAssert.True(!registry.Reload().TryResolve("typo", out _, out string unreadableError)
                && unreadableError.Contains("ChanelWidth", StringComparison.Ordinal) && unreadableError.Contains("DetectorProfiles.invalid.json", StringComparison.Ordinal),
                "An unreadable list reports the reason and the quarantined copy.");
            TestAssert.Equal(0, FormUserSettingsStore.DrainWarnings().Length, "Reloading does not queue warnings that the GUI never collects.");

            TestAssert.True(DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root).LoadError is not null && FormUserSettingsStore.DrainWarnings().Length == 1,
                "Loading at startup still queues the warning.");

            WriteList(fixture.Root, Named("fixed"));
            var repaired = registry.Reload();
            TestAssert.True(repaired.TryResolve("fixed", out _, out string repairedError) && repaired.LoadError is null, repairedError);
        }

        // 261105Codex: 基本16項目を1つずつ省いた一覧は既定値で補わず、項目名を含む理由で拒否する。有効な退避コピーからの復旧は維持する。
        private static void RunMissingFieldsRefusal()
        {
            using var fixture = new TestFixture();
            var calibrated = DetectorWindowTests.CreateExampleProfile();
            var complete = JsonNode.Parse(ListJson(calibrated))!;
            foreach (string field in new[]
            {
                nameof(DetectorProfile.Name), nameof(DetectorProfile.ChannelCount), nameof(DetectorProfile.ChannelWidth),
                nameof(DetectorProfile.ZeroOffset), nameof(DetectorProfile.ResolutionFwhmAtMnKa), nameof(DetectorProfile.DetectorArea),
                nameof(DetectorProfile.Elevation), nameof(DetectorProfile.Azimuth), nameof(DetectorProfile.SpecimenToDetectorDistance),
                nameof(DetectorProfile.OptimalWorkingDistance), nameof(DetectorProfile.SiThickness), nameof(DetectorProfile.AluminumLayer),
                nameof(DetectorProfile.GoldLayer), nameof(DetectorProfile.NickelLayer), nameof(DetectorProfile.DeadLayer), nameof(DetectorProfile.Window)
            })
            {
                // 261106Codex: 元の一覧を保ち、対象1項目だけを省いたJSONの作成を共通化する。
                string missingJson = ListJsonWithoutField(complete, field);
                string folder = Folder(fixture, field);
                File.WriteAllText(ListPath(folder), missingJson);
                var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), folder);
                TestAssert.True(!registry.TryResolve(calibrated.Name, out _, out string error) && error.Contains(field, StringComparison.Ordinal),
                    $"An omitted {field} must be reported instead of being replaced with its default value.");
                TestAssert.True(!registry.WasStored && registry.Names.Count == 0,
                    "An incomplete list must not be replaced by migrating the old settings.");
            }

            // 261106Codex: 退避コピーの検証でも同じ手順で1項目だけを省く。
            string missingThicknessJson = ListJsonWithoutField(complete, nameof(DetectorProfile.SiThickness));
            string recoveredFolder = Folder(fixture, "missing-backup");
            File.WriteAllText(ListPath(recoveredFolder), missingThicknessJson);
            File.WriteAllText(DurableJsonFile.GetBackupPath(ListPath(recoveredFolder)), complete.ToJsonString());
            var recovered = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), recoveredFolder);
            TestAssert.True(recovered.TryResolve(calibrated.Name, out var profile, out string recoveredError), recoveredError);
            TestAssert.True(recovered.LoadError!.Contains(nameof(DetectorProfile.SiThickness), StringComparison.Ordinal),
                "Backup recovery must explain which field was missing from the primary list.");
            TestAssert.Equal(DetectorWindowTests.BuildKey(calibrated), DetectorWindowTests.BuildKey(profile),
                "Backup recovery must keep the complete detector's physical conditions.");
        }

        // 261105Codex: 必須なのは項目の存在であり、有効な0は受け付ける。窓の付随項目は必要な方式だけが持ち、完全な入力の条件キーとスクリプトを保つ。
        private static void RunExplicitZerosAndOptionalFields()
        {
            using var fixture = new TestFixture();
            var zero = Named("zero-fields");
            zero.ZeroOffset = 0;
            zero.Azimuth = 0;
            zero.AluminumLayer = 0;
            zero.GoldLayer = 0;
            zero.NickelLayer = 0;
            zero.DeadLayer = 0;
            var layered = DetectorWindowTests.CreateExampleProfile();
            var preset = Named("preset");
            preset.Window = DetectorWindowKind.Preset;
            preset.WindowPreset = "Beryllium (5 µm)";
            DetectorProfile[] profiles = [zero, layered, preset];
            WriteList(fixture.Root, profiles);
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            foreach (var expected in profiles)
            {
                TestAssert.True(registry.TryResolve(expected.Name, out var actual, out string error), error);
                TestAssert.Equal(DetectorWindowTests.BuildKey(expected), DetectorWindowTests.BuildKey(actual),
                    "Explicit zero and omitted optional window fields must preserve the physical condition key.");
                TestAssert.Equal(DetectorWindowTests.GenerateScript(expected), DetectorWindowTests.GenerateScript(actual),
                    "Reading a complete detector must preserve its generated script exactly.");
                TestAssert.True(actual.WindowOpenAreaFraction is null, "An omitted support-grid fraction must stay omitted (1.0).");
            }
        }

        // 261105Codex: 旧設定の移行、モデル相当の単独JSON、旧manifestのプロファイルには一覧専用の必須指定を適用しない。
        private static void RunLegacyProfileCompatibility()
        {
            using var fixture = new TestFixture();
            const string legacyProfileJson = """{ "Name": "test" }""";
            var registry = DetectorProfileRegistry.Load(
                Loaded("""{ "Version": 1, "DetectorProfile": { "Name": "test" } }"""), Folder(fixture, "legacy-settings"));
            TestAssert.True(registry.Reload().TryResolve("test", out var migrated, out string migrationError), migrationError);
            TestAssert.Equal(DetectorWindowTests.NoWindowConditionKey, DetectorWindowTests.BuildKey(migrated),
                "Migrating a partial legacy profile must keep its existing condition key and write a complete list.");
            TestAssert.Equal(DetectorWindowTests.NoWindowScriptSha256, DetectorWindowTests.Sha256(DetectorWindowTests.GenerateScript(migrated)),
                "Migrating a partial legacy profile must keep its existing script.");

            string modelProfilePath = Path.Combine(Folder(fixture, "legacy-model"), DetectorProfile.FileName);
            File.WriteAllText(modelProfilePath, legacyProfileJson);
            var savedProfile = JsonSerializer.Deserialize<DetectorProfile>(File.ReadAllText(modelProfilePath))!;
            TestAssert.Equal(DetectorWindowTests.NoWindowConditionKey, DetectorWindowTests.BuildKey(savedProfile),
                "A standalone persisted profile must retain legacy defaults outside the registry reader.");

            var solution = TestFixture.CreateSolution();
            var handle = fixture.Handle(solution, 1);
            fixture.Repository.Save(handle, fixture.Repository.LoadOrCreate(handle));
            var manifestJson = JsonNode.Parse(File.ReadAllText(handle.ManifestPath))!;
            var oldProfile = manifestJson["Condition"]!["SemEdxCondition"]!["DetectorProfile"]!.AsObject();
            oldProfile.Remove(nameof(DetectorProfile.SiThickness));
            oldProfile.Remove(nameof(DetectorProfile.Window));
            File.WriteAllText(handle.ManifestPath, manifestJson.ToJsonString());
            var manifest = fixture.Repository.Load(handle.ManifestPath) ?? throw new InvalidDataException("The legacy manifest was not loaded.");
            TestAssert.Equal(handle.ConditionKey, new SpectrumConditionKeyBuilder().BuildKey(manifest.Condition),
                "Reading a legacy manifest with omitted profile fields must preserve its condition key.");
            TestAssert.Equal(handle.PoolFolder, fixture.Handle(solution, 1).PoolFolder, "The legacy pool must remain reusable.");
        }

        // 261103Claude: 一覧の中の不正な値はその名前だけを拒否し、既定値で埋めず、一覧のファイルも書き換えない。
        private static void RunInvalidEntries()
        {
            using var fixture = new TestFixture();
            var wrongChannels = Named("wrong-channels");
            wrongChannels.ChannelCount = 4096;
            var zeroWidth = Named("zero-width");
            zeroWidth.ChannelWidth = 0;
            WriteList(fixture.Root, Named("good"), wrongChannels, zeroWidth);
            byte[] before = File.ReadAllBytes(ListPath(fixture.Root));

            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            TestAssert.True(!registry.TryResolve("wrong-channels", out _, out string channelError) && channelError.Contains("ChannelCount", StringComparison.Ordinal),
                "An entry with a wrong channel count is refused with its reason.");
            TestAssert.True(!registry.TryResolve("zero-width", out _, out string widthError) && widthError.Contains("ChannelWidth", StringComparison.Ordinal),
                "An entry with a zero channel width is refused instead of using the default width.");
            TestAssert.True(registry.TryResolve("good", out _, out string goodError), goodError);
            TestAssert.SequenceEqual(before, File.ReadAllBytes(ListPath(fixture.Root)), "Reading the list does not rewrite it.");
        }

        // 261103Claude: 同じ名前で値が違うときは確認なしでは上書きせず違う項目を返し、開口率 1.0 は省く書き方へそろえる。
        private static void RunRegistration()
        {
            using var fixture = new TestFixture();
            var registry = DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);
            var calibrated = DetectorWindowTests.CreateExampleProfile();
            TestAssert.Equal(DetectorProfileRegistrationOutcome.Added, registry.Register(calibrated, overwrite: false).Outcome, "A new name is added.");
            TestAssert.Equal(DetectorProfileRegistrationOutcome.Unchanged, registry.Register(calibrated, overwrite: false).Outcome, "The same values change nothing.");

            var changed = calibrated.Clone();
            changed.ChannelWidth = 9.925;
            changed.ZeroOffset = -0.5;
            var pending = registry.Register(changed, overwrite: false);
            TestAssert.Equal(DetectorProfileRegistrationOutcome.NeedsOverwriteConfirmation, pending.Outcome, "Different values need a confirmation.");
            TestAssert.SequenceEqual(["ChannelWidth", "ZeroOffset"], pending.ChangedFields, "The changed fields are listed for the confirmation.");
            TestAssert.True(Reload(fixture).TryResolve(calibrated.Name, out var kept, out _) && kept.ChannelWidth == 9.875,
                "Without a confirmation the saved values are kept.");

            TestAssert.Equal(DetectorProfileRegistrationOutcome.Overwritten, registry.Register(changed, overwrite: true).Outcome, "A confirmed change overwrites.");
            TestAssert.True(Reload(fixture).TryResolve(calibrated.Name, out var overwritten, out _) && overwritten.ChannelWidth == 9.925,
                "A confirmed change is saved.");

            var noGrid = DetectorWindowTests.CreateExampleProfile();
            noGrid.Name = "no-grid";
            noGrid.WindowOpenAreaFraction = 1.0;
            registry.Register(noGrid, overwrite: false);
            TestAssert.True(!File.ReadAllText(ListPath(fixture.Root)).Contains("WindowOpenAreaFraction", StringComparison.Ordinal),
                "An open area of 1.0 is saved by omitting the field.");
            TestAssert.True(Reload(fixture).TryResolve("no-grid", out var omitted, out _) && omitted.WindowOpenAreaFraction is null,
                "The omitted open area reads back as no support grid.");

            var invalid = DetectorWindowTests.CreateExampleProfile();
            invalid.Name = "invalid";
            invalid.ChannelCount = 4096;
            TestAssert.Throws<InvalidDataException>(() => registry.Register(invalid, overwrite: false), "Invalid values are not registered.");
            TestAssert.True(registry.Remove("no-grid") && !registry.Remove("no-grid"), "A registered detector is removed once.");
            TestAssert.SequenceEqual(["test", calibrated.Name], Reload(fixture).Names, "Only the remaining detectors are saved.");
        }

        // 261103Claude: 一覧から引く形式の設定は検出器の値を書かず、選んだ名前を既定値で埋めない。旧形式は DetectorProfile.Name を名前とする。
        private static void RunSettingsFormat()
        {
            string json = JsonSerializer.Serialize(new GeneratorFormUserSettings { DetectorName = "Example SDD", UsesDetectorProfileList = true });
            TestAssert.True(!json.Contains("\"DetectorProfile\":", StringComparison.Ordinal) && json.Contains("\"UsesDetectorProfileList\":true", StringComparison.Ordinal),
                "A list-format setting saves only the selected name.");
            TestAssert.Equal(string.Empty, new GeneratorFormUserSettings { UsesDetectorProfileList = true }.GetSelectedDetectorName(),
                "An empty selected name is not replaced by a default name.");

            var legacy = JsonSerializer.Deserialize<GeneratorFormUserSettings>(CurrentSettingsJson)!;
            TestAssert.Equal("test", legacy.GetSelectedDetectorName(), "A current setting selects its saved detector name.");
            TestAssert.True(legacy.UsesDetectorProfileList is null && !JsonSerializer.Serialize(legacy).Contains("UsesDetectorProfileList", StringComparison.Ordinal),
                "A current setting is read without the list marker.");
        }

        private static UserSettingsLoad<GeneratorFormUserSettings> Loaded(string json) =>
            new(JsonSerializer.Deserialize<GeneratorFormUserSettings>(json)!, UserSettingsLoadOutcome.Loaded);

        private static DetectorProfileRegistry Reload(TestFixture fixture) =>
            DetectorProfileRegistry.Load(Loaded(CurrentSettingsJson), fixture.Root);

        private static DetectorProfile Named(string name)
        {
            var profile = DetectorProfile.CreateLegacyTest();
            profile.Name = name;
            return profile;
        }

        private static string Folder(TestFixture fixture, string name) =>
            Directory.CreateDirectory(Path.Combine(fixture.Root, name)).FullName;

        private static string ListPath(string folder) => Path.Combine(folder, DetectorProfileRegistry.FileName);

        private static string ListJson(params DetectorProfile[] detectors) =>
            JsonSerializer.Serialize(new DetectorProfileListFile { Version = 1, Detectors = [.. detectors] });

        // 261106Codex: 完全な一覧を複製してから1項目を省き、別のテストケースや退避コピーへ変更を波及させない。
        private static string ListJsonWithoutField(JsonNode complete, string field)
        {
            var missing = complete.DeepClone();
            missing["Detectors"]![0]!.AsObject().Remove(field);
            return missing.ToJsonString();
        }

        private static void WriteList(string folder, params DetectorProfile[] detectors) =>
            File.WriteAllText(ListPath(folder), ListJson(detectors));
    }
}
