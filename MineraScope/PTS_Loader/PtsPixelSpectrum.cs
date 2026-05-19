using System;

namespace MineraScope
{
    // 260520Codex: PTS の1ピクセル分だけの EDX カウントを保持し、クリック表示のメモリ使用量を抑えます。
    internal sealed class PtsPixelSpectrum
    {
        // 260520Codex: チャンネル別カウント配列はクラス内に閉じ、外側から変更されないようにします。
        private readonly int[] _counts;

        // 260520Codex: PTSFile から取り出したチャンネル配列を検査して保持します。
        public PtsPixelSpectrum(
            int x,
            int y,
            int channelCount,
            double energyOffset,
            double energyScale,
            int[] counts)
        {
            if (x < 0)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (y < 0)
                throw new ArgumentOutOfRangeException(nameof(y));

            if (channelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelCount));

            if (counts.Length != channelCount)
                throw new ArgumentException("PTS pixel spectrum count array length does not match the channel count.", nameof(counts));

            X = x;
            Y = y;
            ChannelCount = channelCount;
            EnergyOffset = energyOffset;
            EnergyScale = energyScale;
            _counts = counts;
        }

        public int X { get; }

        public int Y { get; }

        public int ChannelCount { get; }

        public double EnergyOffset { get; }

        public double EnergyScale { get; }

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
