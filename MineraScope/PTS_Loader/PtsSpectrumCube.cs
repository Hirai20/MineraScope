using System;

namespace MineraScope
{
    // 260518Codex: PTS マッピング全体の EDX カウントを1次元配列で保持し、クリック座標から高速に取り出します。
    internal sealed class PtsSpectrumCube
    {
        // 260518Codex: カウント配列は座標・チャンネル検査後だけアクセスできるよう内部に閉じます。
        private readonly int[] _counts;

        // 260518Codex: PTSFile で読み取った寸法とカウント配列の整合性を検査して保持します。
        public PtsSpectrumCube(
            int width,
            int height,
            int channelCount,
            double energyOffset,
            double energyScale,
            int[] counts)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            if (channelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelCount));

            long expectedLength = (long)width * height * channelCount;
            if (counts.Length != expectedLength)
                throw new ArgumentException("PTS spectrum cube count array length does not match its dimensions.", nameof(counts));

            Width = width;
            Height = height;
            ChannelCount = channelCount;
            EnergyOffset = energyOffset;
            EnergyScale = energyScale;
            _counts = counts;
        }

        public int Width { get; }

        public int Height { get; }

        public int ChannelCount { get; }

        public double EnergyOffset { get; }

        public double EnergyScale { get; }

        // 260518Codex: チャンネル番号を PTS 属性の一次式で keV 軸へ変換します。
        public double GetEnergy(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel));

            return EnergyOffset + channel * EnergyScale;
        }

        // 260518Codex: クリックされた1ピクセルとチャンネル番号に対応する合算カウントを返します。
        public int GetCount(int x, int y, int channel)
        {
            if (x < 0 || x >= Width)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(y));

            if (channel < 0 || channel >= ChannelCount)
                throw new ArgumentOutOfRangeException(nameof(channel));

            long index = ((long)y * Width + x) * ChannelCount + channel;
            return _counts[(int)index];
        }
    }
}
