using System;

namespace MineraScope
{
    // 260522Codex: Keeps one clicked PTS spectrum plus the actual binned pixel range used to sum it.
    internal sealed class PtsPixelSpectrum
    {
        // 260520Codex: チャンネル別カウント配列はクラス内に閉じ、外側から変更されないようにします。
        private readonly int[] _counts;

        // 260522Codex: Validate PTS counts and bin metadata before exposing the summed spectrum.
        public PtsPixelSpectrum(
            int x,
            int y,
            int channelCount,
            double energyOffset,
            double energyScale,
            int[] counts,
            int binLeft,
            int binTop,
            int binRight,
            int binBottom,
            int requestedBinSize)
        {
            ArgumentNullException.ThrowIfNull(counts);

            if (x < 0)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (y < 0)
                throw new ArgumentOutOfRangeException(nameof(y));

            if (channelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelCount));

            if (counts.Length != channelCount)
                throw new ArgumentException("PTS pixel spectrum count array length does not match the channel count.", nameof(counts));

            if (binLeft < 0)
                throw new ArgumentOutOfRangeException(nameof(binLeft));

            if (binTop < 0)
                throw new ArgumentOutOfRangeException(nameof(binTop));

            if (binRight < binLeft)
                throw new ArgumentOutOfRangeException(nameof(binRight));

            if (binBottom < binTop)
                throw new ArgumentOutOfRangeException(nameof(binBottom));

            if (requestedBinSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestedBinSize));

            X = x;
            Y = y;
            ChannelCount = channelCount;
            EnergyOffset = energyOffset;
            EnergyScale = energyScale;
            BinLeft = binLeft;
            BinTop = binTop;
            BinRight = binRight;
            BinBottom = binBottom;
            RequestedBinSize = requestedBinSize;
            _counts = counts;
        }

        public int X { get; }

        public int Y { get; }

        public int ChannelCount { get; }

        public double EnergyOffset { get; }

        public double EnergyScale { get; }

        // 260522Codex: Expose the actual clamped bin range used for the summed spectrum.
        public int BinLeft { get; }

        public int BinTop { get; }

        public int BinRight { get; }

        public int BinBottom { get; }

        // 260522Codex: Derived from the clamped bin bounds; the +1 keeps both edges inclusive.
        public int BinnedPixelCount => (BinRight - BinLeft + 1) * (BinBottom - BinTop + 1);

        public int RequestedBinSize { get; }

        // 260520Codex: チャンネル番号を PTS 属性の一次式で keV 軸へ変換します。
        public double GetEnergy(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel));

            return EnergyOffset + channel * EnergyScale;
        }

        // 260520Codex: 指定チャンネルの全フレーム合算カウントを返します。
        public int GetCount(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel));

            return _counts[channel];
        }
    }
}
