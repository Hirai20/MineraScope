using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineraScope
{
    // 260626Claude: 低エネルギー(カーボンコンタミ)マスクの設定。DTSA シミュと実測で食い違う C 領域を
    //   学習・推論の両方から無効化するための前処理を表す。モデルが自分の前処理を持ち歩けるよう、
    //   学習時に preprocessing.json へ保存し、予測時はモデルフォルダから読んで同じ前処理を自動適用する。
    //   既定 None は完全 no-op。preprocessing.json を持たない既存モデルは従来どおりマスク無しで扱う。
    // 261016Codex: 記録なしの旧モデルとの互換性を保ち、破損した前処理記録は公開版でも拒否する。
    internal sealed record SpectrumPreprocessing(int MaskChannelCount, LowEnergyMaskOrder MaskOrder)
    {
        // 260626Claude: preprocessing.json のファイル名と、エネルギー軸・既定マスク境界の定数。
        public const string FileName = "preprocessing.json";
        private const double TrainingMaskCutoffEv = 350.0;
        // 260930Claude: 書き込みと読み込みが同じ版を指すよう、記録の版番号を1か所に置きます。
        private const int CurrentSchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // 260626Claude: マスク無しの既定。全 normalize 経路の no-op 入口。
        public static SpectrumPreprocessing None { get; } = new(0, LowEnergyMaskOrder.Pre);

        public bool HasLowEnergyMask => MaskChannelCount > 0;

        public bool MaskBeforeNormalize => MaskOrder == LowEnergyMaskOrder.Pre;

        // 260626Claude: この検証ブランチでは学習時に常に低エネルギーマスクを掛ける(env トグルは作らない)。
        //   オン/オフは Git のブランチ/savepoint で管理する。
        public static SpectrumPreprocessing ForTraining() =>
            FromCutoffEv(TrainingMaskCutoffEv, LowEnergyMaskOrder.Pre);

        // 260626Claude: <=cutoffEv のチャンネル数を求める。350eV / 10eV-per-ch なら ch0..35 の 36ch。
        public static SpectrumPreprocessing FromCutoffEv(double cutoffEv, LowEnergyMaskOrder order)
        {
            if (cutoffEv <= 0)
                return None;

            // 260803Codex: 既定軸の逆変換を使い、従来の floor(350 / 10) + 1 と同じ 36 ch を得ます。
            int channels = (int)Math.Floor(SpectrumAxis.Default.ChannelAt(cutoffEv)) + 1;
            return new SpectrumPreprocessing(channels, order);
        }

        // 260626Claude: 先頭 MaskChannelCount チャンネル(<=指定eV)を 0 にする。配列長を超えないようクランプ。
        public void ZeroLeadingChannels(float[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (!HasLowEnergyMask)
                return;

            int count = Math.Min(MaskChannelCount, values.Length);
            for (int i = 0; i < count; i++)
                values[i] = 0f;
        }

        // 260626Claude: 学習成果物の隣に前処理を記録する。labelEncoder.json / modelType.txt と同じフォルダ。
        public void WriteToModelFolder(string modelFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(modelFolder);

            var dto = new PreprocessingFileDto(
                CurrentSchemaVersion,
                HasLowEnergyMask
                    ? new LowEnergyMaskDto(MaskChannelCount, MaskOrder.ToString().ToLowerInvariant())
                    : null);
            File.WriteAllText(Path.Combine(modelFolder, FileName), JsonSerializer.Serialize(dto, JsonOptions));
        }

        // 260626Claude: 予測時はここからモデルの前処理を復元する。
        //   既存モデル(記録無し)は None = マスク無しで従来どおり判定される。
        // 260930Claude: 「記録が無い」と「記録はあるが読めない」を分けます。以前はどちらも None へ落ちたため、
        //   マスクありで学習したモデルの記録が壊れると、マスク無しのスペクトルで推論を続けていました。
        public static SpectrumPreprocessing LoadFromModelFolder(string modelFolder)
        {
            if (string.IsNullOrWhiteSpace(modelFolder))
                return None;

            string path = Path.Combine(modelFolder, FileName);
            return File.Exists(path) ? ReadFile(path) : None;
        }

        // 260930Claude: 記録を解釈する唯一の場所。未対応の版・欠けた項目・未知の order は None へ落とさず拒否します。
        //   未知の order を Pre とみなすと、将来 order を増やしたときに旧版が黙って別の前処理で推論します。
        // 260930Codex: 既存のJSONの大文字・小文字を区別しない契約を保ち、不正なレコードを拒否する。
        // 260930Claude: 読み書きで同じ DTO を使うので、項目名を変えても書き手と読み手がずれません。
        //   大文字小文字の吸収と項目名の綴りは JsonOptions が引き続き受け持ちます。
        public static SpectrumPreprocessing ReadFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            PreprocessingFileDto? record;
            try
            {
                record = JsonSerializer.Deserialize<PreprocessingFileDto>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                throw new InvalidDataException($"モデルの前処理記録を読み込めません: {path}", exception);
            }

            if (record is null || record.Version != CurrentSchemaVersion)
                throw new InvalidDataException($"モデルの前処理記録が不正です: {path}");

            // 明示的な null はマスク無しの記録なので、記録が無い場合と同じ None になります。
            if (record.LowEnergyMask is not { } mask)
                return None;

            InvalidDataException Invalid() => new($"モデルの低エネルギーマスク記録が不正です: {path}");
            if (mask.CutoffChannels < 1 || mask.CutoffChannels >= SpectrumAxis.Default.ChannelCount)
                throw Invalid();

            return mask.Order?.ToLowerInvariant() switch
            {
                "pre" => new SpectrumPreprocessing(mask.CutoffChannels, LowEnergyMaskOrder.Pre),
                "post" => new SpectrumPreprocessing(mask.CutoffChannels, LowEnergyMaskOrder.Post),
                _ => throw Invalid()
            };
        }

        // 260626Claude: ログ用の一行表現。train/predict debug log にどの前処理を使ったか残すため。
        public string Describe() =>
            HasLowEnergyMask
                ? $"lowEnergyMask=on channels=0-{MaskChannelCount - 1} order={MaskOrder.ToString().ToLowerInvariant()}"
                : "lowEnergyMask=off";

        // 260930Claude: 書き手は常に両方を書くので、項目が欠けた記録は別物として拒否します。
        //   明示的な null（マスク無し）と区別する必要があるため、既定値へ落とさず JsonRequired で弾きます。
        private sealed record PreprocessingFileDto(
            [property: JsonRequired] int Version,
            [property: JsonRequired] LowEnergyMaskDto? LowEnergyMask);

        private sealed record LowEnergyMaskDto(int CutoffChannels, string? Order);
    }

    // 260626Claude: マスクを max 正規化の前(pre)に掛けるか後(post)に掛けるか。pre が既定、post は ablation 用。
    internal enum LowEnergyMaskOrder
    {
        Pre,
        Post
    }
}
