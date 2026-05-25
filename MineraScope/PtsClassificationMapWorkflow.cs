using System;
using System.Threading;

namespace MineraScope
{
    // 260526Claude: PTS 読み取り → 正規化 → バッチ Top-1 推論 → 結果組み立てを担う。重い処理は Task.Run から呼ぶ前提。
    // メモリ: BlockCounts(grid) は本メソッド内だけで保持し、推論後に破棄する。正規化スペクトルはチャンク float[,] を都度作り再利用する。
    internal static class PtsClassificationMapWorkflow
    {
        // 260526Claude: 推論バッチ行数。変更しやすいよう定数で持つ。
        public const int InferenceBatchSize = 512;

        // 260526Claude: BlockCounts の確保上限 (v1)。超えたら確保前に中断する。
        public const long MemoryCapBytes = 512L * 1024 * 1024;

        public static PtsClassificationMapResult Run(
            string filePath,
            int binSize,
            string modelPath,
            string modelName,
            MineralClassificationPredictionService service,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            ArgumentNullException.ThrowIfNull(service);

            if (binSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(binSize));

            using var pts = new PTSFile(filePath);

            // 260526Claude: 大配列確保前にチャンネル数とメモリを検証する。
            int channelCount = pts.UsableChannelCount;
            if (channelCount != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException(
                    $"このPTSの有効チャンネル数は {channelCount} 点です。分類モデルは {SpectrumDataLoader.SpectrumLength} 点の入力に対応しています。");

            if (pts.Width <= 0 || pts.Height <= 0)
                throw new InvalidOperationException("PTSの画像サイズを取得できませんでした。");

            int gridWidth = (pts.Width + binSize - 1) / binSize;
            int gridHeight = (pts.Height + binSize - 1) / binSize;
            long estimatedBytes = (long)gridWidth * gridHeight * channelCount * sizeof(int);
            if (estimatedBytes > MemoryCapBytes)
                throw new InvalidOperationException(
                    $"ビニング格子の推定メモリが {estimatedBytes / (1024 * 1024)}MB で上限 {MemoryCapBytes / (1024 * 1024)}MB を超えます。binを大きくしてください。");

            // 260526Claude: 読み取りは進捗 0..0.5 に割り当てる。
            IProgress<double>? readProgress = progress is null ? null : new ScaledProgress(progress, 0.5, 0.0);
            PtsBinnedSpectrumGrid? grid = pts.TryReadBinnedSpectrumGrid(binSize, readProgress, cancellationToken);
            if (grid is null)
                throw new InvalidOperationException("PTSからスペクトル格子を読み取れませんでした。");

            cancellationToken.ThrowIfCancellationRequested();

            string[] labelNames = service.GetLabelNames(modelPath);

            int blockCount = grid.BlockCount;
            var top1LabelId = new int[blockCount];

            // 260526Claude: チャンク batch を 1 つだけ確保して使い回す（全 float[] は保持しない）。
            var batch = new float[InferenceBatchSize, SpectrumDataLoader.SpectrumLength];
            var rowToBlock = new int[InferenceBatchSize];
            int rowsInChunk = 0;
            int processedBlocks = 0;

            for (int blockY = 0; blockY < grid.GridHeight; blockY++)
            {
                for (int blockX = 0; blockX < grid.GridWidth; blockX++)
                {
                    int flatIndex = blockY * grid.GridWidth + blockX;
                    bool hasSignal = SpectrumDataLoader.NormalizeInto(grid.GetBlockCounts(blockX, blockY), batch, rowsInChunk);
                    if (!hasSignal)
                    {
                        // 260526Claude: ゼロカウントは未判定。推論しない。
                        top1LabelId[flatIndex] = PtsClassificationMapResult.UnclassifiedLabelId;
                    }
                    else
                    {
                        rowToBlock[rowsInChunk] = flatIndex;
                        rowsInChunk++;
                        if (rowsInChunk == InferenceBatchSize)
                        {
                            ClassifyChunk(service, modelPath, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken);
                            rowsInChunk = 0;
                        }
                    }

                    processedBlocks++;
                    if ((processedBlocks & 0x3FF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(0.5 + 0.45 * processedBlocks / blockCount);
                    }
                }
            }

            ClassifyChunk(service, modelPath, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken);

            progress?.Report(1.0);

            return new PtsClassificationMapResult(
                filePath,
                modelPath,
                modelName,
                binSize,
                grid.GridWidth,
                grid.GridHeight,
                top1LabelId,
                labelNames);
        }

        // 260526Claude: 溜まったチャンクを推論し、行→ブロックの対応で結果へ書き戻す。
        // 260526Codex: 呼び出し側で rowsInChunk を明示的に戻し、戻り値の番兵 0 をなくします。
        private static void ClassifyChunk(
            MineralClassificationPredictionService service,
            string modelPath,
            float[,] batch,
            int[] rowToBlock,
            int rowsInChunk,
            int[] top1LabelId,
            CancellationToken cancellationToken)
        {
            if (rowsInChunk == 0)
                return;

            float[,] chunk = rowsInChunk == batch.GetLength(0) ? batch : SliceRows(batch, rowsInChunk);
            var predictions = service.PredictTop1Batch(modelPath, chunk, cancellationToken);
            for (int row = 0; row < rowsInChunk; row++)
                top1LabelId[rowToBlock[row]] = predictions[row];
        }

        // 260526Claude: batch の先頭 rows 行だけを取り出す（最終チャンク用）。float[,] は行優先連続なので線形コピーでよい。
        private static float[,] SliceRows(float[,] batch, int rows)
        {
            int columns = batch.GetLength(1);
            var sliced = new float[rows, columns];
            Array.Copy(batch, sliced, (long)rows * columns);
            return sliced;
        }

        // 260526Claude: 子フェーズの 0..1 進捗を全体進捗の一部区間へ写像する同期アダプタ。
        private sealed class ScaledProgress(IProgress<double> inner, double scale, double offset) : IProgress<double>
        {
            public void Report(double value) => inner.Report(offset + value * scale);
        }
    }
}
