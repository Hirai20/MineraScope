using System;

namespace MineraScope
{
    // 260526Claude: 非重複ブロックビニングで作った全ブロックの全チャンネルカウントを保持するマップ生成の中間データ。
    // BlockCounts は大きくなり得るため、推論後に破棄される想定（result には残さない）。
    internal sealed class PtsBinnedSpectrumGrid
    {
        // 260526Claude: フラット配列 ((blockY * GridWidth) + blockX) * ChannelCount + channel。
        private readonly int[] _blockCounts;

        public PtsBinnedSpectrumGrid(
            int gridWidth,
            int gridHeight,
            int binSize,
            int channelCount,
            int[] blockCounts)
        {
            ArgumentNullException.ThrowIfNull(blockCounts);

            if (gridWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(gridWidth));

            if (gridHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(gridHeight));

            if (binSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(binSize));

            if (channelCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(channelCount));

            if ((long)blockCounts.Length != (long)gridWidth * gridHeight * channelCount)
                throw new ArgumentException("Block count array length does not match the grid dimensions.", nameof(blockCounts));

            GridWidth = gridWidth;
            GridHeight = gridHeight;
            BinSize = binSize;
            ChannelCount = channelCount;
            _blockCounts = blockCounts;
        }

        public int GridWidth { get; }

        public int GridHeight { get; }

        public int BinSize { get; }

        public int ChannelCount { get; }

        public int BlockCount => GridWidth * GridHeight;

        // 260526Claude: 指定ブロックのチャンネル別カウントをコピーせず参照で返す。
        public ReadOnlySpan<int> GetBlockCounts(int blockX, int blockY)
        {
            if ((uint)blockX >= (uint)GridWidth)
                throw new ArgumentOutOfRangeException(nameof(blockX));

            if ((uint)blockY >= (uint)GridHeight)
                throw new ArgumentOutOfRangeException(nameof(blockY));

            int start = ((blockY * GridWidth) + blockX) * ChannelCount;
            return _blockCounts.AsSpan(start, ChannelCount);
        }
    }
}
