using System;
using System.Diagnostics;
using System.Threading;

namespace MineraScope
{
    // 260527Codex: Builds PTS mineral maps by reading bounded block-count tiles and classifying each tile in batches.
    internal static class PtsClassificationMapWorkflow
    {
        // 260527Codex: Use a larger inference batch to reduce TensorFlow call overhead while keeping input memory modest.
        public const int InferenceBatchSize = 8192;

        // 260527Codex: This budget applies only to the temporary int[counts] tile, not to the full map result.
        public const long TileMemoryBudgetBytes = 1024L * 1024 * 1024;

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

            var totalTimer = Stopwatch.StartNew();
            long modelPreparationTicks = 0;
            long readAndAggregateTicks = 0;
            long normalizeAndPackTicks = 0;
            long inferenceTicks = 0;

            using var pts = new PTSFile(filePath);

            int channelCount = pts.UsableChannelCount;
            if (channelCount != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException(
                    $"このPTSの有効チャンネル数は {channelCount} 点です。分類モデルは {SpectrumDataLoader.SpectrumLength} 点の入力に対応しています。");

            if (pts.Width <= 0 || pts.Height <= 0)
                throw new InvalidOperationException("PTSの画像サイズを取得できませんでした。");

            int gridWidth = (pts.Width + binSize - 1) / binSize;
            int gridHeight = (pts.Height + binSize - 1) / binSize;
            long blockCountLong = (long)gridWidth * gridHeight;
            if (blockCountLong > int.MaxValue)
                throw new InvalidOperationException("ビニング格子が大きすぎて確保できません。binを大きくしてください。");

            var stageTimer = Stopwatch.StartNew();
            string[] labelNames = service.GetLabelNames(modelPath);
            modelPreparationTicks += stageTimer.ElapsedTicks;

            int blockCount = (int)blockCountLong;
            var top1LabelId = new int[blockCount];
            int tileBlockRows = CalculateTileBlockRows(gridWidth, channelCount, gridHeight);
            int tileCount = (gridHeight + tileBlockRows - 1) / tileBlockRows;

            // 260526Claude: チャンク batch を 1 つだけ確保して使い回す（全 float[] は保持しない）。
            var batch = new float[InferenceBatchSize, SpectrumDataLoader.SpectrumLength];
            var rowToBlock = new int[InferenceBatchSize];
            int rowsInChunk = 0;
            int processedBlocks = 0;

            for (int tileIndex = 0, startBlockY = 0; startBlockY < gridHeight; tileIndex++, startBlockY += tileBlockRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int rowsInTile = Math.Min(tileBlockRows, gridHeight - startBlockY);
                double tileOffset = (double)tileIndex / tileCount;
                double tileScale = 1.0 / tileCount;
                IProgress<double>? readProgress = progress is null ? null : new ScaledProgress(progress, tileScale * 0.6, tileOffset);
                stageTimer.Restart();
                PtsBinnedSpectrumGrid? grid = pts.TryReadBinnedSpectrumGridRows(
                    binSize,
                    startBlockY,
                    rowsInTile,
                    readProgress,
                    cancellationToken);
                readAndAggregateTicks += stageTimer.ElapsedTicks;

                if (grid is null)
                    throw new InvalidOperationException("PTSからスペクトル格子を読み取れませんでした。");

                int tileBlockCount = grid.BlockCount;
                int processedTileBlocks = 0;
                long tileInferenceTicksStart = inferenceTicks;
                stageTimer.Restart();
                for (int localBlockY = 0; localBlockY < grid.GridHeight; localBlockY++)
                {
                    int globalBlockY = startBlockY + localBlockY;
                    for (int blockX = 0; blockX < grid.GridWidth; blockX++)
                    {
                        int flatIndex = globalBlockY * gridWidth + blockX;
                        bool hasSignal = SpectrumDataLoader.NormalizeInto(grid.GetBlockCounts(blockX, localBlockY), batch, rowsInChunk);
                        if (!hasSignal)
                        {
                            top1LabelId[flatIndex] = PtsClassificationMapResult.UnclassifiedLabelId;
                        }
                        else
                        {
                            rowToBlock[rowsInChunk] = flatIndex;
                            rowsInChunk++;
                            if (rowsInChunk == InferenceBatchSize)
                            {
                                inferenceTicks += ClassifyChunk(service, modelPath, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken);
                                rowsInChunk = 0;
                            }
                        }

                        processedBlocks++;
                        processedTileBlocks++;
                        if ((processedBlocks & 0x3FF) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress?.Report(tileOffset + tileScale * (0.6 + 0.35 * processedTileBlocks / tileBlockCount));
                        }
                    }
                }

                inferenceTicks += ClassifyChunk(service, modelPath, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken);
                normalizeAndPackTicks += Math.Max(0, stageTimer.ElapsedTicks - (inferenceTicks - tileInferenceTicksStart));
                rowsInChunk = 0;
                progress?.Report((double)(tileIndex + 1) / tileCount);
            }

            progress?.Report(1.0);
            totalTimer.Stop();
            var timings = new PtsClassificationMapTimings(
                ElapsedTime(modelPreparationTicks),
                ElapsedTime(readAndAggregateTicks),
                ElapsedTime(normalizeAndPackTicks),
                ElapsedTime(inferenceTicks),
                totalTimer.Elapsed,
                tileCount,
                InferenceBatchSize,
                TileMemoryBudgetBytes);

            return new PtsClassificationMapResult(
                filePath,
                modelPath,
                modelName,
                binSize,
                gridWidth,
                gridHeight,
                top1LabelId,
                labelNames,
                timings);
        }

        // 260527Codex: Derive the tallest tile that stays under the temporary count-array budget.
        private static int CalculateTileBlockRows(int gridWidth, int channelCount, int gridHeight)
        {
            long bytesPerBlockRow = Math.Max(1, (long)gridWidth * channelCount * sizeof(int));
            long rows = Math.Max(1, TileMemoryBudgetBytes / bytesPerBlockRow);
            return (int)Math.Clamp(rows, 1, gridHeight);
        }

        // 260526Claude: 溜まったチャンクを推論し、行→ブロックの対応で結果へ書き戻す。
        // 260526Codex: 呼び出し側で rowsInChunk を明示的に戻し、戻り値の番兵 0 をなくします。
        private static long ClassifyChunk(
            MineralClassificationPredictionService service,
            string modelPath,
            float[,] batch,
            int[] rowToBlock,
            int rowsInChunk,
            int[] top1LabelId,
            CancellationToken cancellationToken)
        {
            if (rowsInChunk == 0)
                return 0;

            float[,] chunk = rowsInChunk == batch.GetLength(0) ? batch : SliceRows(batch, rowsInChunk);
            var inferenceTimer = Stopwatch.StartNew();
            var predictions = service.PredictTop1Batch(modelPath, chunk, cancellationToken);
            long elapsedTicks = inferenceTimer.ElapsedTicks;
            for (int row = 0; row < rowsInChunk; row++)
                top1LabelId[rowToBlock[row]] = predictions[row];

            return elapsedTicks;
        }

        // 260527Codex: Convert Stopwatch ticks to TimeSpan without assuming Stopwatch frequency equals TimeSpan ticks.
        private static TimeSpan ElapsedTime(long stopwatchTicks)
            => TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

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
