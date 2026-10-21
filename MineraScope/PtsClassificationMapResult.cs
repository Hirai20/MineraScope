using System;

namespace MineraScope
{
    // 260526Claude: 全ブロック鉱物分類マップの結果。各ブロックは Top-1 のみ保持し、BlockCounts や全ラベル確率は持たない。
    // クリック再分類は作成時条件 (ModelPath / BinSize / PtsFilePath) で原点基準ブロックを再読みするため、ここにスペクトルは持たない。
    internal sealed class PtsClassificationMapResult
    {
        public const int UnclassifiedLabelId = -1;
        // 260622Codex: Unknownは、利用可能なスペクトルを分類した結果、学習済み鉱物の埋め込み分布の外と判定したことを意味する。
        public const int UnknownLabelId = -2;

        // 260526Claude: 行優先フラット配列 by * GridWidth + bx。未判定は UnclassifiedLabelId。
        private readonly int[] _top1LabelId;
        private readonly string[] _labelNames;

        public PtsClassificationMapResult(
            string ptsFilePath,
            string modelPath,
            string modelName,
            int binSize,
            int? leadingSweepCount,
            // 260807Codex: ユーザー要求とモデル能力による実適用を区別して保持する。
            bool unknownDetectionRequested,
            // 261001Claude: 実適用の有無ではなく方式を受け取る。マップ要約と .txt が方式名を出すため。
            UnknownDetectionMethod unknownDetection,
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

            // 260612Codex: マップに走査上限を保持し、後の詳細クリックでも同じ読込条件を再現する。
            if (leadingSweepCount is <= 0)
                throw new ArgumentOutOfRangeException(nameof(leadingSweepCount));

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
            LeadingSweepCount = leadingSweepCount;
            // 260807Codex: 表示条件と再現情報の両方を結果オブジェクトへ固定する。
            UnknownDetectionRequested = unknownDetectionRequested;
            UnknownDetection = unknownDetection;
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

        // 260612Codex: nullは全走査、値があれば先頭N回の完了走査を表す。
        public int? LeadingSweepCount { get; }

        // 260807Codex: ユーザーがマップ作成時に未学習検知を要求したかを記録する。
        public bool UnknownDetectionRequested { get; }

        // 261001Claude: どの方式で Unknown を判定したか。保存物が方式を決めるので、結果にも方式のまま載せる。
        public UnknownDetectionMethod UnknownDetection { get; }

        // 260807Codex: 検知器と特徴抽出器が揃い、実際に Unknown 判定を適用したかを示す。
        public bool UnknownDetectionApplied => UnknownDetection != UnknownDetectionMethod.None;

        public int GridWidth { get; }

        public int GridHeight { get; }

        public PtsClassificationMapTimings Timings { get; }

        public int BlockCount => GridWidth * GridHeight;

        // 260526Claude: colorizer はフラット走査するため index アクセサを公開する。
        public int GetLabelIdAt(int flatIndex) => _top1LabelId[flatIndex];

        // 260526Claude: labelId が範囲外/未判定なら空文字を返す。
        public string GetMineralName(int labelId)
            => labelId >= 0 && labelId < _labelNames.Length ? _labelNames[labelId] : string.Empty;

        // 260623Claude: classes.csv 生成用に labelEncoder 順のラベル名一覧を公開する (出力 ID = index + 1)。
        public IReadOnlyList<string> LabelNames => _labelNames;
    }

    // 260527Codex: マップ生成の時間診断を渡し、読込と推論のどちらが支配的かUIで示せるようにする。
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
