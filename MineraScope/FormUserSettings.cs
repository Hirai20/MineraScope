// 261106Codex: 公開版の設定項目を保ち、検出器一覧の移行を共用する。
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineraScope
{
    // 260728Claude: 設定ファイルの持ち回りを 4 層で守る。
    //   層1 書き込み: 一時ファイルへ書いて物理ディスクまで flush し、File.Replace で原子的に差し替える。
    //        Replace は差し替え前の内容を .bak へ自動退避するので、直前の正常値が必ず 1 世代残る。
    //   層2 読み込み: 本体 → .bak → 工場出荷既定値 の多段フォールバック。本体が壊れても前回値までは戻れる。
    //   層3 信頼度  : 「読めなかったので既定値」のときだけ、その run での保存を禁じる (正常値を既定値で潰さない)。
    //   層4 版数    : Version を持ち将来のスキーマ変更を検出する。version 欄が無い既存ファイルは暗黙 v1 として読む。
    internal static class FormUserSettingsStore
    {
        // 260728Claude: 現行スキーマ。既存ファイルには version 欄が無く 0 になるが、中身は v1 と同じなので受け入れる。
        //   将来スキーマを変えるときはここを上げ、TryRead に移行処理を足す。
        private const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // 260728Claude: 層3 の不変条件「実データを復元できなかったファイルは上書きしない」は、ファイル単位の
        //   性質でありフォーム単位ではない。各フォームが bool を持って自分の Save を守る形だと、
        //   新しい保存経路が増えるたびに守り忘れでデータ喪失が復活する。ここで一元的に拒否する。
        private static readonly HashSet<string> UnsafeToOverwrite = new(StringComparer.OrdinalIgnoreCase);

        // 260728Claude: 読み込み警告もここへ溜め、UI が立ち上がってから FormMain が 1 回だけ引き取る。
        //   フォームごとに internal プロパティで上へ渡す形だと、生成順序に依存して取りこぼす。
        private static readonly List<string> PendingWarnings = [];

        // 260728Claude: 溜まった警告を取り出して空にする (FormMain_Load が 1 回だけ呼ぶ)。
        public static string[] DrainWarnings()
        {
            lock (PendingWarnings)
            {
                string[] warnings = [.. PendingWarnings];
                PendingWarnings.Clear();
                return warnings;
            }
        }

        // 260717Codex: アプリ設定のルートを、鉱物色パレットのストアと直接共有する。
        private static string GetPath(string fileName) =>
            Path.Combine(DefaultStoragePaths.SettingsFolder, fileName);

        // 260727Claude: 壊れた設定ファイルで起動できなくなるのを避ける。従来は JsonException がそのまま
        //   Form のコンストラクタ経由で Program.Main まで抜け、ウィンドウが 1 枚も出ずにプロセスが死んでいた
        //   (保存中断で切れた FormMainSettings.json で実測)。
        // 260728Claude: 層2。本体が読めなければ .bak を試し、それも駄目なときだけ工場出荷既定値へ落ちる。
        public static UserSettingsLoad<T> Load<T>(string fileName)
            where T : class, IVersionedUserSettings, new() =>
            LoadFromPath<T>(GetPath(fileName));

        // 261103Claude: 設定フォルダー以外 (テストの一時フォルダー) でも同じ 4 層の規則で読めるよう、パスを受け取る形を共用する。
        //   readOptions は、手書きの綴り誤りを既定値で黙って読み飛ばさないよう未知の項目を拒否したいファイル (検出器の一覧) が渡す。
        // 261104Claude: GUI が警告を引き取るのは起動時の 1 回だけなので、起動後に読み直す側は queueWarning を false にして、理由を結果の ReadError で受け取る。
        public static UserSettingsLoad<T> LoadFromPath<T>(string path, JsonSerializerOptions? readOptions = null, bool queueWarning = true)
            where T : class, IVersionedUserSettings, new()
        {
            string fileName = Path.GetFileName(path);
            string backupPath = DurableJsonFile.GetBackupPath(path);

            // 260728Claude: 本体も退避も無い = 初回起動。既定値が正解なので保存も許可する。
            if (!File.Exists(path) && !File.Exists(backupPath))
                return new UserSettingsLoad<T>(new T(), UserSettingsLoadOutcome.Missing);

            if (TryRead(path, readOptions, out T? primary, out string? primaryError))
                return new UserSettingsLoad<T>(primary!, UserSettingsLoadOutcome.Loaded);

            if (TryRead(backupPath, readOptions, out T? recovered, out _))
                return Warn(
                    new UserSettingsLoad<T>(recovered!, UserSettingsLoadOutcome.RecoveredFromBackup) { ReadError = primaryError },
                    $"{fileName} を読み込めなかったため、前回保存された内容 ({Path.GetFileName(backupPath)}) から復元しました。\r\n\r\n{primaryError}",
                    queueWarning);

            // 260728Claude: ここへ来たら実データは戻せない。壊れた本体は可能なら証跡として退避し、
            //   このファイルを「上書き禁止」に登録する (ロックが解けた後の終了時保存で正常値を潰さないため)。
            UnsafeToOverwrite.Add(path);
            string readError = primaryError + DurableJsonFile.Quarantine(path);
            return Warn(
                new UserSettingsLoad<T>(new T(), UserSettingsLoadOutcome.FellBackToDefaults) { ReadError = readError },
                $"{fileName} と退避コピーのどちらも読み込めなかったため、既定値で開始します。\r\n" +
                $"設定が失われるのを防ぐため、この起動では {fileName} を保存しません。\r\n\r\n" +
                readError,
                queueWarning);
        }

        // 260728Claude: 警告を溜めてから結果を返す。呼び出し側が警告を持ち回らずに済むようにする。
        private static UserSettingsLoad<T> Warn<T>(UserSettingsLoad<T> result, string warning, bool queueWarning)
            where T : class, IVersionedUserSettings, new()
        {
            if (!queueWarning)
                return result;

            lock (PendingWarnings)
                PendingWarnings.Add(warning);
            return result;
        }

        // 260728Claude: 1 ファイルぶんの読み取り。存在しない・壊れている・新しすぎる のいずれも false を返し、
        //   呼び出し側が次の候補へ進めるようにする。
        private static bool TryRead<T>(string path, JsonSerializerOptions? readOptions, out T? settings, out string? error)
            where T : class, IVersionedUserSettings, new()
        {
            settings = null;
            if (!File.Exists(path))
            {
                error = $"{Path.GetFileName(path)} がありません。";
                return false;
            }

            try
            {
                T? parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(path), readOptions);
                if (parsed is null)
                {
                    error = $"{Path.GetFileName(path)} の内容を読み取れませんでした。";
                    return false;
                }

                // 260728Claude: 層4。version 欄が無い既存ファイルは 0 になるが現行スキーマと同義なので通す。
                //   逆に現行より新しい版は解釈できないので、読み取り失敗として .bak 側へ回す。
                if (parsed.Version > CurrentVersion)
                {
                    error = $"{Path.GetFileName(path)} はこの版より新しい形式 (version {parsed.Version}) です。";
                    return false;
                }

                settings = parsed;
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        // 260727Claude: 一時ファイルへ書いてから置換する。従来の File.WriteAllText は既存ファイルをその場で
        //   truncate するため、終了時保存 (FormMain_FormClosing) の最中に強制終了・クラッシュすると
        //   途中まで書かれた JSON が残り、次回起動が不能になっていた。これがその破損の発生源。
        // 260728Claude: 実処理は DurableJsonFile。ここは「上書きしてよいか」の判定と版数の付与だけを担う。
        public static void Save<T>(string fileName, T settings)
            where T : class, IVersionedUserSettings =>
            SaveToPath(GetPath(fileName), settings);

        // 261103Claude: 保存を拒否したかを返し、明示の登録操作 (検出器の一覧) が保存されなかったことを利用者へ伝えられるようにする。
        public static bool SaveToPath<T>(string path, T settings)
            where T : class, IVersionedUserSettings
        {
            // 260728Claude: 読み込みで実データを復元できなかったファイルは、この run では書かない。
            //   呼び出し側ごとの bool ではなくここで拒否するので、新しい保存経路が増えても守り忘れが起きない。
            if (UnsafeToOverwrite.Contains(path))
                return false;

            settings.Version = CurrentVersion;
            DurableJsonFile.Replace(path, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
    }

    // 260728Claude: 層4。全設定クラスが版数を持ち、読み取り時に解釈可能かを判定できるようにする。
    internal interface IVersionedUserSettings
    {
        int Version { get; set; }
    }

    // 260728Claude: 読み込みがどの層で成立したか。呼び出し側はこれで「既定値を残すか」「保存してよいか」を決める。
    internal enum UserSettingsLoadOutcome
    {
        Missing,
        Loaded,
        RecoveredFromBackup,
        FellBackToDefaults
    }

    // 260728Claude: 読み込み結果。値だけでなく信頼度を返すのがこの設計の要点で、
    //   「読めなかったので既定値」と「ファイルが無いので既定値」を呼び出し側が区別できるようにする。
    //   上書き可否と警告文はストアが自分で持つため、ここには載せない。
    internal readonly record struct UserSettingsLoad<T>(T Settings, UserSettingsLoadOutcome Outcome)
    {
        // 260728Claude: 保存済みの値が手に入ったか。false なら Designer 既定値をそのまま残す。
        public bool HasStoredValues =>
            Outcome is UserSettingsLoadOutcome.Loaded or UserSettingsLoadOutcome.RecoveredFromBackup;

        // 261104Claude: 本体を読めなかった理由 (退避先があればそのパスも含む)。起動後に読み直す側が画面へ出すために持つ。
        public string? ReadError { get; init; }
    }

    // 260507Codex: FormMain では共通パス欄だけを保存し、解析ログや入力 spectrum 表示は保存しません。
    internal sealed class FormMainUserSettings : IVersionedUserSettings
    {
        public int Version { get; set; }
        public string ModelPath { get; set; } = string.Empty;
        public string SelectedModelName { get; set; } = string.Empty;
        public string EdxOutputPath { get; set; } = string.Empty;
        public string DtsaPath { get; set; } = string.Empty;
    }

    // 260716Claude: AnalyzerForm ではマッピングモデルの選択だけを保存し、PTS 表示やマップ結果は保存しません。
    // 260807Codex: マッピングモデルの選択と未学習検知設定を保存し、PTS 表示やマップ結果は保存しない。
    internal sealed class AnalyzerFormUserSettings : IVersionedUserSettings
    {
        public int Version { get; set; }
        public string SelectedMappingModelName { get; set; } = string.Empty;
        // 260807Codex: 旧設定ファイルで欠落している場合も初回既定 OFF を保つ。
        public bool MapUnknownDetectionEnabled { get; set; }
    }

    // 260507Codex: GeneratorForm では生成・EDX・学習設定だけを保存し、鉱物詳細やログは保存しません。
    internal sealed class GeneratorFormUserSettings : IVersionedUserSettings
    {
        // 261105Claude: 保存済みの値が無ければ初回既定値を使う。GUI なしの起動と一覧への移行が同じ規則になるよう 1 か所に置く。
        public static GeneratorFormUserSettings FromLoad(UserSettingsLoad<GeneratorFormUserSettings> load) =>
            load.HasStoredValues ? load.Settings : CreateInitialDefaults();

        // 260901Codex: 非対話実行の初回パスでも、Bの20ジョブ枠を含むGeneratorForm Designerと同じ有効な値を使う。
        public static GeneratorFormUserSettings CreateInitialDefaults() =>
            new()
            {
                DetectorName = "test",
                DetectorProfile = MineraScope.DetectorProfile.CreateLegacyTest("test"),
                ModelName = string.Empty,
                TargetSpectrumCount = 1000,
                ParallelCount = 20,
                Resolution = 10,
                Epochs = 500,
                BatchSize = 16,
                UnknownDistanceScale = 1.5,
                EarlyStopping = 10,
                ValidationSplit = 20,
                ProbeCurrent = 0.5,
                LiveTime = 120,
                MeasurementTimePresetBEnabled = true,
                BeamEnergy = 20,
                // 261021Codex: 初期設定も指定した固定膜厚だけを持つ。
                CarbonThickness = 0.02
            };

        public int Version { get; set; }
        public string DetectorName { get; set; } = string.Empty;
        // 261103Claude: 検出器の値の正本は DetectorProfiles.json へ移した。旧形式の値は一覧への移行にだけ使い、一覧ができた後は書かない。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DetectorProfile? DetectorProfile { get; set; }
        // 261103Claude: true は検出器の値を一覧から引く形式で保存した設定。一覧が無くなっても、名前だけの旧形式とみなして既定値を移し直さない。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UsesDetectorProfileList { get; set; }
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
        // 260901Codex: null は B 導入前の設定を表し、旧設定だけを従来の単一測定時間へ戻せるようにします。
        public bool? MeasurementTimePresetBEnabled { get; set; }
        public double BeamEnergy { get; set; }
        public double CarbonThickness { get; set; }

        // 260916Claude (Phase 5): 学習方式は未指定なら従来分類として読み込む。
        public string? TrainingMethod { get; set; }

        public DetectorProfile GetDetectorProfile() =>
            MineraScope.DetectorProfile.CreateWithDefaults(DetectorProfile, DetectorName);

        // 261103Claude: 一覧から引く検出器名。旧形式では DetectorProfile.Name が正本だったので、移行と同じ規則で名前を決める。
        public string GetSelectedDetectorName() =>
            DetectorProfile is null ? DetectorName : GetDetectorProfile().Name;
    }
}
