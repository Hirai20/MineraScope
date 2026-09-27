using System.Globalization;

namespace MineraScope
{
    // 260803Codex: EDX エネルギー軸を 1 つの検証済み不変値として扱います。
    public readonly record struct SpectrumAxis
    {
        public int ChannelCount { get; }
        public double ZeroOffset { get; }
        public double ChannelWidth { get; }

        // 260803Codex: 現行の 2048 ch・0 eV offset・10 eV/ch を唯一の既定値として集約します。
        public static SpectrumAxis Default { get; } = new(2048, 0.0, 10.0);

        // 260803Codex: 不正な軸が後段の配列長やエネルギー計算へ流れないよう、生成時に拒否します。
        public SpectrumAxis(int channelCount, double zeroOffset, double channelWidth)
        {
            if (channelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelCount), "Channel count must be positive.");
            if (!double.IsFinite(zeroOffset))
                throw new ArgumentOutOfRangeException(nameof(zeroOffset), "Zero offset must be finite.");
            if (!double.IsFinite(channelWidth) || channelWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelWidth), "Channel width must be finite and positive.");

            ChannelCount = channelCount;
            ZeroOffset = zeroOffset;
            ChannelWidth = channelWidth;
        }

        // 260803Codex: E(i) = ZeroOffset + ChannelWidth * i を軸の単一実装にします。
        public double EnergyAt(int channel)
        {
            if ((uint)channel >= (uint)ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel));

            return ZeroOffset + ChannelWidth * channel;
        }

        // 260803Codex: eV から端数を保ったチャンネル座標へ逆変換します。
        public double ChannelAt(double energyEv) => (energyEv - ZeroOffset) / ChannelWidth;

        // 260803Codex: 診断ログで軸の 3 要素を一行比較できる invariant 表現です。
        public string Describe() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"ChannelCount={ChannelCount} ZeroOffset={ZeroOffset:G17}eV ChannelWidth={ChannelWidth:G17}eV/ch");
    }
}
