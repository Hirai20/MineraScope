using System;
using System.IO;
using System.Text.Json;

namespace MineraScope
{
    // 260626Claude: 低エネルギー(カーボンコンタミ)マスクの設定。DTSA シミュと実測で食い違う C 領域を
    //   学習・推論の両方から無効化するための前処理を表す。モデルが自分の前処理を持ち歩けるよう、
    //   学習時に preprocessing.json へ保存し、予測時はモデルフォルダから読んで同じ前処理を自動適用する。
    //   既定 None は完全 no-op。preprocessing.json を持たない既存モデルは従来どおりマスク無しで扱う。
    internal sealed record SpectrumPreprocessing(int MaskChannelCount, LowEnergyMaskOrder MaskOrder)
    {
        // 260626Claude: preprocessing.json のファイル名と、エネルギー軸・既定マスク境界の定数。
        public const string FileName = "preprocessing.json";
        private const double EnergyPerChannelEv = 10.0;
        private const double TrainingMaskCutoffEv = 350.0;

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

            int channels = (int)Math.Floor(cutoffEv / EnergyPerChannelEv) + 1;
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
                1,
                HasLowEnergyMask
                    ? new LowEnergyMaskDto(MaskChannelCount, MaskOrder.ToString().ToLowerInvariant())
                    : null);
            File.WriteAllText(Path.Combine(modelFolder, FileName), JsonSerializer.Serialize(dto, JsonOptions));
        }

        // 260626Claude: 予測時はここからモデルの前処理を復元する。記録が無い/壊れているときは安全側の None。
        //   既存モデル(記録無し)は None = マスク無しで従来どおり判定される。
        public static SpectrumPreprocessing LoadFromModelFolder(string modelFolder)
        {
            if (string.IsNullOrWhiteSpace(modelFolder))
                return None;

            string path = Path.Combine(modelFolder, FileName);
            if (!File.Exists(path))
                return None;

            try
            {
                var dto = JsonSerializer.Deserialize<PreprocessingFileDto>(File.ReadAllText(path), JsonOptions);
                if (dto?.LowEnergyMask is not { } mask || mask.CutoffChannels <= 0)
                    return None;

                var order = string.Equals(mask.Order, "post", StringComparison.OrdinalIgnoreCase)
                    ? LowEnergyMaskOrder.Post
                    : LowEnergyMaskOrder.Pre;
                return new SpectrumPreprocessing(mask.CutoffChannels, order);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return None;
            }
        }

        // 260626Claude: ログ用の一行表現。train/predict debug log にどの前処理を使ったか残すため。
        public string Describe() =>
            HasLowEnergyMask
                ? $"lowEnergyMask=on channels=0-{MaskChannelCount - 1} order={MaskOrder.ToString().ToLowerInvariant()}"
                : "lowEnergyMask=off";

        private sealed record PreprocessingFileDto(int Version, LowEnergyMaskDto? LowEnergyMask);

        private sealed record LowEnergyMaskDto(int CutoffChannels, string Order);
    }

    // 260626Claude: マスクを max 正規化の前(pre)に掛けるか後(post)に掛けるか。pre が既定、post は ablation 用。
    internal enum LowEnergyMaskOrder
    {
        Pre,
        Post
    }
}
