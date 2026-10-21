using System;
using System.Diagnostics;
using System.Threading;

namespace MineraScope
{
    // 260527Codex: ブロック件数を制限したタイル単位でPTSを読み、各タイルをバッチ分類して鉱物マップを作る。
    internal static class PtsClassificationMapWorkflow
    {
        // 260527Codex: 入力メモリを抑えつつ、大きな推論バッチでTensorFlow呼び出しの負荷を減らす。
        public const int InferenceBatchSize = 8192;

        // 260527Codex: この上限は一時的なint[counts]タイルだけに適用し、マップ全体の結果には適用しない。
        public const long TileMemoryBudgetBytes = 1024L * 1024 * 1024;

        public static PtsClassificationMapResult Run(
            string filePath,
            int binSize,
            string modelPath,
            string modelName,
            int? leadingSweepCount,
            // 260807Codex: 分析条件として要求された未学習検知設定をワークフローへ渡す。
            bool detectUnknown,
            MineralClassificationPredictionService service,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            ArgumentNullException.ThrowIfNull(service);

            if (binSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(binSize));

            // 260612Codex: ワークフローの契約を、PTSFileの走査上限の意味に合わせる。
            if (leadingSweepCount is <= 0)
                throw new ArgumentOutOfRangeException(nameof(leadingSweepCount));

            // 260604Codex: ネイティブプロセス中断をまたぎ、マップ進捗とTensorFlowバッチログを対応付ける。
            long mapRunId = TensorFlowPredictionDebugLog.NextMapRunId();
            // 260807Codex: モデル読込前なので、開始ログにはユーザー要求だけを記録する。
            TensorFlowPredictionDebugLog.Write(
                "map-run-start",
                $"mapRun={mapRunId} bin={binSize} sweeps={FormatSweepLimit(leadingSweepCount)} unknownRequested={detectUnknown} model={TensorFlowPredictionDebugLog.Clean(modelName)} file={TensorFlowPredictionDebugLog.Clean(Path.GetFileName(filePath))}");

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

            TensorFlowPredictionDebugLog.Write(
                "map-grid-ready",
                $"mapRun={mapRunId} width={pts.Width} height={pts.Height} gridWidth={gridWidth} gridHeight={gridHeight} channelCount={channelCount} blocks={blockCountLong}");

            var stageTimer = Stopwatch.StartNew();
            string[] labelNames = service.GetLabelNames(modelPath);
            // 260807Codex: モデル読込後に検知器と特徴抽出器の両方が揃った場合だけ実適用とする。
            // 261001Claude: 方式の決定はserviceの保存物が持つ規則へ寄せる。
            UnknownDetectionMethod unknownDetection = service.DescribeUnknownDetection(detectUnknown);
            bool unknownApplied = unknownDetection != UnknownDetectionMethod.None;
            modelPreparationTicks += stageTimer.ElapsedTicks;
            TensorFlowPredictionDebugLog.Write(
                "map-labels-ready",
                $"mapRun={mapRunId} labels={labelNames.Length} unknownApplied={unknownApplied} elapsedMs={ElapsedTime(modelPreparationTicks).TotalMilliseconds:F0}");

            // 260626Claude: 分類モデルの前処理を読み、学習時と同じ低エネルギーマスクをマップ全画素にも自動適用する。
            //   preprocessing.json が無い既存モデルは None = マスク無しで従来どおり。
            var preprocessing = SpectrumPreprocessing.LoadFromModelFolder(ModelArtifactPaths.GetClassificationFolder(modelPath));
            TensorFlowPredictionDebugLog.Write("map-preprocessing", $"mapRun={mapRunId} {preprocessing.Describe()}");

            int blockCount = (int)blockCountLong;
            var top1LabelId = new int[blockCount];
            int tileBlockRows = CalculateTileBlockRows(gridWidth, channelCount, gridHeight);
            int tileCount = (gridHeight + tileBlockRows - 1) / tileBlockRows;
            TensorFlowPredictionDebugLog.Write(
                "map-tiles-ready",
                $"mapRun={mapRunId} tileBlockRows={tileBlockRows} tileCount={tileCount} batchSize={InferenceBatchSize}");

            // 260526Claude: チャンク batch を 1 つだけ確保して使い回す（全 float[] は保持しない）。
            var batch = new float[InferenceBatchSize, SpectrumDataLoader.SpectrumLength];
            var rowToBlock = new int[InferenceBatchSize];
            int rowsInChunk = 0;
            int processedBlocks = 0;
            // 260604Codex: クラッシュ後のログ照合用に、マップ内のTensorFlow予測チャンクへ番号を付ける。
            int chunkIndex = 0;

            for (int tileIndex = 0, startBlockY = 0; startBlockY < gridHeight; tileIndex++, startBlockY += tileBlockRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int rowsInTile = Math.Min(tileBlockRows, gridHeight - startBlockY);
                TensorFlowPredictionDebugLog.Write(
                    "map-tile-start",
                    $"mapRun={mapRunId} tile={tileIndex} startBlockY={startBlockY} rowsInTile={rowsInTile}");
                double tileOffset = (double)tileIndex / tileCount;
                double tileScale = 1.0 / tileCount;
                IProgress<double>? readProgress = progress is null ? null : new ScaledProgress(progress, tileScale * 0.6, tileOffset);
                stageTimer.Restart();
                PtsBinnedSpectrumGrid? grid = pts.TryReadBinnedSpectrumGridRows(
                    binSize,
                    startBlockY,
                    rowsInTile,
                    leadingSweepCount,
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
                        bool hasSignal = SpectrumDataLoader.NormalizeInto(grid.GetBlockCounts(blockX, localBlockY), batch, rowsInChunk, preprocessing);
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
                                // 260807Codex: 実際に適用可能と確定した条件を各推論チャンクへ渡す。
                                inferenceTicks += ClassifyChunk(service, modelPath, unknownApplied, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken, mapRunId, ++chunkIndex, tileIndex);
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

                // 260604Codex: 空のタイル末尾では、診断用チャンクIDを消費しない。
                // 260807Codex: 最終チャンクも同じ実適用条件で分類する。
                if (rowsInChunk > 0)
                    inferenceTicks += ClassifyChunk(service, modelPath, unknownApplied, batch, rowToBlock, rowsInChunk, top1LabelId, cancellationToken, mapRunId, ++chunkIndex, tileIndex);
                normalizeAndPackTicks += Math.Max(0, stageTimer.ElapsedTicks - (inferenceTicks - tileInferenceTicksStart));
                rowsInChunk = 0;
                TensorFlowPredictionDebugLog.Write(
                    "map-tile-end",
                    $"mapRun={mapRunId} tile={tileIndex} processedTileBlocks={processedTileBlocks} processedBlocks={processedBlocks}");
                progress?.Report((double)(tileIndex + 1) / tileCount);
            }

            progress?.Report(1.0);
            totalTimer.Stop();
            TensorFlowPredictionDebugLog.Write(
                "map-run-end",
                $"mapRun={mapRunId} chunks={chunkIndex} totalMs={totalTimer.Elapsed.TotalMilliseconds:F0}");
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
                leadingSweepCount,
                // 260807Codex: 要求値と実適用値を分けて結果へ残す。
                detectUnknown,
                unknownDetection,
                gridWidth,
                gridHeight,
                top1LabelId,
                labelNames,
                timings);
        }

        // 260527Codex: 一時カウント配列の上限内に収まる、最大のタイル高さを求める。
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
            // 260807Codex: 共有予測サービスの状態を変えず、チャンク単位の適用条件を明示する。
            bool detectUnknown,
            float[,] batch,
            int[] rowToBlock,
            int rowsInChunk,
            int[] top1LabelId,
            CancellationToken cancellationToken,
            long mapRunId,
            int chunkIndex,
            int tileIndex)
        {
            if (rowsInChunk == 0)
                return 0;

            // 260604Codex: ネイティブランタイムが中断する前に、TF.NETへ入る正確なマップチャンクを記録する。
            TensorFlowPredictionDebugLog.Write(
                "map-chunk-start",
                $"mapRun={mapRunId} chunk={chunkIndex} tile={tileIndex} rows={rowsInChunk}");
            float[,] chunk = rowsInChunk == batch.GetLength(0) ? batch : SliceRows(batch, rowsInChunk);
            var inferenceTimer = Stopwatch.StartNew();
            // 260608Claude: PredictTop1Batch の未使用 debugScope 引数撤去に追従。
            // 260807Codex: マップ作成時に確定した実適用条件をバッチ予測へ渡す。
            var predictions = service.PredictTop1Batch(modelPath, chunk, cancellationToken, detectUnknown);
            long elapsedTicks = inferenceTimer.ElapsedTicks;
            for (int row = 0; row < rowsInChunk; row++)
                top1LabelId[rowToBlock[row]] = predictions[row];

            TensorFlowPredictionDebugLog.Write(
                "map-chunk-end",
                $"mapRun={mapRunId} chunk={chunkIndex} tile={tileIndex} rows={rowsInChunk} elapsedMs={ElapsedTime(elapsedTicks).TotalMilliseconds:F0}");
            return elapsedTicks;
        }

        // 260527Codex: Stopwatchの周波数がTimeSpanのtickと同じと仮定せず、経過tickをTimeSpanへ変換する。
        private static TimeSpan ElapsedTime(long stopwatchTicks)
            => TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

        // 260612Codex: 全走査か先頭部分かを、マップ診断に明示する。
        private static string FormatSweepLimit(int? leadingSweepCount)
            => leadingSweepCount.HasValue
                ? leadingSweepCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "all";

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
