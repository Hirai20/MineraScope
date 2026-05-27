using System;

namespace MineraScope
{
    // 260526Claude: 全ブロック鉱物分類マップの結果。各ブロックは Top-1 のみ保持し、BlockCounts や全ラベル確率は持たない。
    // クリック再分類は作成時条件 (ModelPath / BinSize / PtsFilePath) で原点基準ブロックを再読みするため、ここにスペクトルは持たない。
    internal sealed class PtsClassificationMapResult
    {
        public const int UnclassifiedLabelId = -1;

        // 260526Claude: 行優先フラット配列 by * GridWidth + bx。未判定は UnclassifiedLabelId。
        private readonly int[] _top1LabelId;
        private readonly string[] _labelNames;

        public PtsClassificationMapResult(
            string ptsFilePath,
            string modelPath,
            string modelName,
            int binSize,
            int gridWidth,
            int gridHeight,
            int[] top1LabelId,
            string[] labelNames,
            PtsClassificationMapTimings timings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ptsFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            ArgumentNullException.ThrowIfNull(top1LabelId);
            ArgumentNullException.ThrowIfNull(labelNames);
            ArgumentNullException.ThrowIfNull(timings);

            if (binSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(binSize));

            if (gridWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(gridWidth));

            if (gridHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(gridHeight));

            if (top1LabelId.Length != gridWidth * gridHeight)
                throw new ArgumentException("Top-1 label array length does not match grid dimensions.", nameof(top1LabelId));

            PtsFilePath = ptsFilePath;
            ModelPath = modelPath;
            ModelName = modelName ?? string.Empty;
            BinSize = binSize;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            Timings = timings;
            _top1LabelId = top1LabelId;
            _labelNames = labelNames;
        }

        public string PtsFilePath { get; }

        public string ModelPath { get; }

        public string ModelName { get; }

        public int BinSize { get; }

        public int GridWidth { get; }

        public int GridHeight { get; }

        public PtsClassificationMapTimings Timings { get; }

        public int BlockCount => GridWidth * GridHeight;

        // 260526Claude: colorizer はフラット走査するため index アクセサを公開する。
        public int GetLabelIdAt(int flatIndex) => _top1LabelId[flatIndex];

        // 260526Claude: labelId が範囲外/未判定なら空文字を返す。
        public string GetMineralName(int labelId)
            => labelId >= 0 && labelId < _labelNames.Length ? _labelNames[labelId] : string.Empty;
    }

    // 260527Codex: Carries map-generation timing diagnostics so UI can show whether reading or inference dominates.
    internal sealed record PtsClassificationMapTimings(
        TimeSpan ModelPreparation,
        TimeSpan ReadAndAggregate,
        TimeSpan NormalizeAndPack,
        TimeSpan Inference,
        TimeSpan Total,
        int TileCount,
        int BatchSize,
        long TileMemoryBudgetBytes);
}
