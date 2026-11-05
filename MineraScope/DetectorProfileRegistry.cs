// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MineraScope
{
    // 261103Claude: 検出器の一覧ファイル (DetectorProfiles.json) の中身。各要素は pool の manifest やモデルの detectorProfile.json と同じ DetectorProfile の形で持つ。
    internal sealed class DetectorProfileListFile : IVersionedUserSettings
    {
        public int Version { get; set; }
        public List<DetectorProfile?> Detectors { get; set; } = [];
    }

    // 261103Claude: 登録の結果。値の違う同名の検出器は確認なしでは上書きしない (同じ名前で値の違う pool やモデルができるため)。
    internal enum DetectorProfileRegistrationOutcome
    {
        Added,
        Unchanged,
        Overwritten,
        NeedsOverwriteConfirmation
    }

    // 261103Claude: ChangedFields は上書きの確認に示す、値の違う項目名 (JSON の項目名)。
    internal sealed record DetectorProfileRegistrationResult(DetectorProfileRegistrationOutcome Outcome, IReadOnlyList<string> ChangedFields);

    // 261103Claude: 検出器名から登録済みの DetectorProfile を引く。GUI と GUI なし起動 (生成・基準値採取) はすべてここを通し、
    //   名前の規則・一覧の読込・旧設定からの移行を共有する。名前は自由な文字列として扱い、書式を解釈せず、ファイル名やフォルダ名にも使わない。
    //   名前は完全一致 (Ordinal) で引き、重複は大文字・小文字と全角・半角を同じとみなして拒否する。打ち間違いで別の検出器へ切り替わらないようにするため。
    internal sealed class DetectorProfileRegistry
    {
        public const string FileName = "DetectorProfiles.json";

        // 261103Claude: 一覧は当面手で書くので、綴りを誤った項目を既定値のまま黙って読まないよう未知の項目を拒否する。
        // 261105Codex: 登録一覧だけは基本項目の欠落も拒否する。窓の付随3項目は方式ごとの検査に任せ、旧設定・manifest・モデルの読込は従来のままにする。
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                // 261106Codex: 必須項目指定の処理に名前を付け、読込設定の入れ子を減らす。
                Modifiers = { RequireRegisteredDetectorFields }
            }
        };

        private readonly string _path;
        private List<DetectorProfile> _detectors;

        private DetectorProfileRegistry(string path, List<DetectorProfile> detectors, string? loadError)
        {
            _path = path;
            _detectors = detectors;
            LoadError = loadError;
        }

        // 261103Claude: 一覧を読めなかった・作れなかった理由。名前を引けなかったときの説明に添える。
        // 261104Claude: 本体を読めず退避コピーの内容を使っているときも理由を持つ。
        public string? LoadError { get; private set; }

        // 261104Claude: この起動で一覧のファイルを読めた・作れたことがあるか。GeneratorForm が旧形式の検出器の値を書き残すかの判断に使う。
        //   読み直しに失敗しても false へ戻さない。戻すと終了時の保存が旧形式の値を書き戻し、移行を取り消してしまう。
        public bool WasStored { get; private set; }

        public IReadOnlyList<string> Names => _detectors.Select(static detector => detector.Name).OfType<string>().ToArray();

        private static readonly string MissingListError = $"検出器の一覧 ({FileName}) がありません。検出器を登録してください。";

        public static DetectorProfileRegistry Load(UserSettingsLoad<GeneratorFormUserSettings> legacySettings, string? settingsFolder = null)
        {
            string path = Path.Combine(settingsFolder ?? DefaultStoragePaths.SettingsFolder, FileName);
            return TryRead(path, queueWarning: true) ?? Migrate(path, legacySettings);
        }

        // 261103Claude: 開いたまま手で書き換えた一覧を、GUI の操作ごとに読み直す。移行は起動時の Load だけで行い、
        //   起動後に一覧が消えても起動時の古い設定から作り直さない。
        // 261104Claude: 読めない理由は LoadError で画面へ返すので、起動時の通知には積まない。
        public DetectorProfileRegistry Reload()
        {
            var reloaded = TryRead(_path, queueWarning: false) ?? new(_path, [], MissingListError);
            reloaded.WasStored |= WasStored;
            return reloaded;
        }

        // 261106Codex: 登録一覧のプロファイルだけを対象にし、窓の付随3項目を除いて存在を必須にする。
        private static void RequireRegisteredDetectorFields(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Type != typeof(DetectorProfile))
                return;

            foreach (var property in typeInfo.Properties)
                if (property.Name is not (nameof(DetectorProfile.WindowLayers)
                    or nameof(DetectorProfile.WindowOpenAreaFraction) or nameof(DetectorProfile.WindowPreset)))
                    property.IsRequired = true;
        }

        // 261103Claude: 一覧のファイルを読む。本体も退避コピーも無ければ null を返し、移行するかは呼び出し側が決める。
        // 261104Claude: 読めなかった理由を LoadError に含め、綴りの誤りなどを手で直す手がかりを画面に出す。
        private static DetectorProfileRegistry? TryRead(string path, bool queueWarning)
        {
            var loaded = FormUserSettingsStore.LoadFromPath<DetectorProfileListFile>(path, ReadOptions, queueWarning);
            // 261105Claude: 手書きの null 要素はここで 1 回だけ除き、以後の処理では null を考えない。
            List<DetectorProfile> detectors = loaded.Settings.Detectors?.OfType<DetectorProfile>().ToList() ?? [];
            return loaded.Outcome switch
            {
                UserSettingsLoadOutcome.Loaded =>
                    new(path, detectors, null) { WasStored = true },
                UserSettingsLoadOutcome.RecoveredFromBackup =>
                    new(path, detectors, $"検出器の一覧 ({FileName}) の本体を読み込めなかったため、退避コピーの内容を使っています。\r\n\r\n{loaded.ReadError}") { WasStored = true },
                UserSettingsLoadOutcome.FellBackToDefaults =>
                    new(path, [], $"検出器の一覧 ({FileName}) を本体も退避コピーも読み込めませんでした。\r\n\r\n{loaded.ReadError}"),
                _ => null
            };
        }

        // 261103Claude: 一覧がまだ無いときに 1 回だけ、GeneratorFormSettings.json の検出器を今までの読込規則で得られる値 (これまで生成に使ってきた値) のまま登録する。
        //   名前は比べず、旧設定の名前 (今は test) をそのまま使う。設定ファイルが無い初回起動では、今の初回既定値を登録する。
        private static DetectorProfileRegistry Migrate(string path, UserSettingsLoad<GeneratorFormUserSettings> legacySettings)
        {
            // 261103Claude: 設定を読めなかったときに既定値で一覧を作ると、設定を直しても次回以降に今の検出器が移されなくなる。
            if (legacySettings.Outcome == UserSettingsLoadOutcome.FellBackToDefaults)
                return new(path, [], $"GeneratorFormSettings.json を読み込めなかったため、検出器の一覧 ({FileName}) を作っていません。");
            // 261103Claude: 一覧から引く形式で保存した設定には検出器の値が無い。名前だけの旧形式とみなして既定値を登録すると、選んだ名前に別の値が入る。
            if (legacySettings.HasStoredValues && legacySettings.Settings.UsesDetectorProfileList == true)
                return new(path, [], MissingListError);

            var source = GeneratorFormUserSettings.FromLoad(legacySettings);
            DetectorProfile detectorProfile = source.GetDetectorProfile();
            try
            {
                detectorProfile.Validate();
            }
            catch (InvalidDataException exception)
            {
                return new(path, [], $"今の検出器「{detectorProfile.Name}」を検出器の一覧 ({FileName}) へ移せませんでした。{exception.Message}");
            }

            var registry = new DetectorProfileRegistry(path, [detectorProfile], null);
            try
            {
                registry.WasStored = FormUserSettingsStore.SaveToPath(path, new DetectorProfileListFile { Detectors = [detectorProfile] });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                registry.LoadError = $"検出器の一覧 ({FileName}) を保存できませんでした。{exception.Message}";
            }

            return registry;
        }

        public bool TryResolve(string? name, out DetectorProfile detectorProfile, out string error)
        {
            detectorProfile = null!;
            // 261103Claude: 登録できる名前は前後に空白を持たないので、入力の前後の空白を除いても別の名前と取り違えない。
            string requested = name?.Trim() ?? string.Empty;
            if (DetectorProfile.GetNameError(requested) is string nameError)
            {
                error = nameError;
                return false;
            }

            // 261103Claude: 完全一致する名前は重複判定の形でも一致するので、同じとみなす名前を 1 回で集めて未登録・重複を判定する。
            string folded = FoldForDuplicateCheck(requested)!;
            DetectorProfile[] matches = _detectors.Where(detector => FoldForDuplicateCheck(detector.Name) == folded).ToArray();
            DetectorProfile? exact = matches.FirstOrDefault(detector => string.Equals(detector.Name, requested, StringComparison.Ordinal));
            if (exact is null)
            {
                error = matches.Length == 0
                    ? $"検出器「{requested}」は登録されていません。"
                    : $"検出器「{requested}」は登録されていません。登録済みの「{string.Join("」「", matches.Select(static detector => detector.Name))}」とは大文字・小文字か全角・半角だけが違います。その検出器を使う場合は、登録した名前のとおりに入力してください。";
                if (LoadError is not null)
                    error += "\r\n" + LoadError;
                return false;
            }

            if (matches.Length > 1)
            {
                error = $"検出器名「{requested}」と同じとみなす名前が一覧に複数あります ({string.Join("、", matches.Select(static detector => $"「{detector.Name}」"))})。{FileName} を直してください。";
                return false;
            }

            try
            {
                exact.Validate();
            }
            catch (InvalidDataException exception)
            {
                error = $"登録済みの検出器「{requested}」の値が不正です。{exception.Message}";
                return false;
            }

            detectorProfile = exact.Clone();
            error = string.Empty;
            return true;
        }

        // 261103Claude: 新しい名前は追加し、同じ名前で値が同じなら何もしない。値が違うときは overwrite が true のときだけ上書きし、
        //   false なら保存せずに違う項目を返す (画面が確認を挟むため)。名前・値が不正なら InvalidDataException、保存できなければ InvalidOperationException。
        public DetectorProfileRegistrationResult Register(DetectorProfile detectorProfile, bool overwrite)
        {
            var candidate = detectorProfile.Clone();
            candidate.Validate();

            string folded = FoldForDuplicateCheck(candidate.Name)!;
            int index = IndexOf(candidate.Name);
            string[] conflicting = _detectors
                .Where((detector, i) => i != index && FoldForDuplicateCheck(detector.Name) == folded)
                .Select(static detector => detector.Name)
                .ToArray();
            if (conflicting.Length > 0)
                throw new InvalidDataException($"検出器名「{candidate.Name}」は、登録済みの「{string.Join("」「", conflicting)}」と大文字・小文字か全角・半角だけが違うため登録できません。");

            if (index < 0)
            {
                Save([.. _detectors, candidate]);
                return new(DetectorProfileRegistrationOutcome.Added, []);
            }

            string[] changedFields = GetChangedFields(_detectors[index], candidate);
            if (changedFields.Length == 0)
                return new(DetectorProfileRegistrationOutcome.Unchanged, []);
            if (!overwrite)
                return new(DetectorProfileRegistrationOutcome.NeedsOverwriteConfirmation, changedFields);

            var detectors = _detectors.ToList();
            detectors[index] = candidate;
            Save(detectors);
            return new(DetectorProfileRegistrationOutcome.Overwritten, changedFields);
        }

        public bool Remove(string name)
        {
            int index = IndexOf(name);
            if (index < 0)
                return false;

            var detectors = _detectors.ToList();
            detectors.RemoveAt(index);
            Save(detectors);
            return true;
        }

        // 261103Claude: 保存できたときだけ手元の一覧を差し替え、保存に失敗した登録が残ったように見えないようにする。
        private void Save(List<DetectorProfile> detectors)
        {
            if (!FormUserSettingsStore.SaveToPath(_path, new DetectorProfileListFile { Detectors = [.. detectors] }))
                throw new InvalidOperationException($"検出器の一覧 ({FileName}) を読み込めなかったため、この起動では保存しません。");

            _detectors = detectors;
            WasStored = true;
            LoadError = null;
        }

        private int IndexOf(string name) =>
            _detectors.FindIndex(detector => string.Equals(detector.Name, name, StringComparison.Ordinal));

        // 261103Claude: 重複の判定だけに使う比較用の形。大文字・小文字と全角・半角 (NFKC) を同じとみなす。不正な名前は null。
        private static string? FoldForDuplicateCheck(string? name) =>
            DetectorProfile.GetNameError(name) is null
                ? name!.Normalize(NormalizationForm.FormKC).ToUpperInvariant()
                : null;

        private static string[] GetChangedFields(DetectorProfile current, DetectorProfile candidate)
        {
            var left = JsonSerializer.SerializeToNode(current)!.AsObject();
            var right = JsonSerializer.SerializeToNode(candidate)!.AsObject();
            return left.Select(static property => property.Key)
                .Union(right.Select(static property => property.Key))
                .Where(key => !JsonNode.DeepEquals(left[key], right[key]))
                .ToArray();
        }
    }
}
