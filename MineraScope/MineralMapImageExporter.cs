using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MineraScope
{
    // 260623Claude: 鉱物マッピングの定量評価用に、現在の視野を ImageJ で扱える画像群へ書き出す。
    // BSE(8bit grayscale TIFF) / RGB予測マップ(PNG) / クラスIDラベル(8bit TIFF) / classes.csv / metadata.json を、
    // すべてフル解像度 (PTS native W×H) で同一寸法・同一座標に揃える。ラベルは RGB の逆算ではなく
    // top1LabelId を直接 uint8 へ再マップして保存する (定量評価が目的)。
    internal static class MineralMapImageExporter
    {
        // 260623Claude: 固定 ID スキーム。unknown 検出ありモデルとなしモデルの両方で同じ意味を保つ。
        // 0=background(信号なし) / 1..N=鉱物(labelEncoder 順 +1) / 255=Unknown(分布外)。
        // unknown を出さないモデルでは 255 が単に出現しないだけで、スキームは共通。
        public const byte BackgroundId = 0;
        public const byte UnknownId = 255;

        // 260623Claude: 鉱物 ID は 1..254 に収める (255 は Unknown 予約)。これを超えるモデルは別スキームが要る。
        public const int MaxMineralClassCount = UnknownId - 1;

        // 260623Claude: 予測マップの出力寸法。ROI を引く外部 BSE 電子像 (JEOL View0xx IMG1.tif) と同寸法・同視野に固定する。
        // JEOL の View 電子像/元素マップは視野によらず 1024×768。PTS 埋め込み SEM ではなく外部 BSE に重ねる運用なので、
        // 別解像度の BSE に合わせるときはここだけ変える。
        public const int TargetWidth = 1024;
        public const int TargetHeight = 768;

        // 260623Claude: モデルの top1 ラベル ID (番兵込み) を出力用 uint8 クラス ID へ写す。
        public static byte MapLabelId(int modelLabelId)
        {
            if (modelLabelId == PtsClassificationMapResult.UnknownLabelId)
                return UnknownId;
            // 260623Claude: 未判定 (-1) や想定外の負値は背景に倒す。
            if (modelLabelId < 0)
                return BackgroundId;

            int id = modelLabelId + 1;
            if (id > MaxMineralClassCount)
                throw new InvalidOperationException(
                    $"鉱物クラス数が多すぎます (labelId={modelLabelId})。8bit ラベル画像は最大 {MaxMineralClassCount} クラスまで対応します。");
            return (byte)id;
        }

        // 260623Claude: ブロック解像度 (gridW×gridH) の分類結果を、同一視野の出力寸法 (width×height) へ最近傍スケールした
        // 行優先 uint8 ラベルへ変換する。外部 BSE は PTS 埋め込み SEM と解像度が違うので bin 倍ではなくグリッド→目標で割り付ける。
        // pred[y*width+x] = MapLabelId(top1LabelId[yToBlock[y]*gridW + xToBlock[x]])。
        public static byte[] BuildLabelMap(PtsClassificationMapResult map, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(map);
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            int gridWidth = map.GridWidth;
            int[] xToBlock = BuildAxisBlockMap(width, gridWidth);
            int[] yToBlock = BuildAxisBlockMap(height, map.GridHeight);
            var labels = new byte[checked(width * height)];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                int blockRowOffset = yToBlock[y] * gridWidth;
                for (int x = 0; x < width; x++)
                    labels[rowOffset + x] = MapLabelId(map.GetLabelIdAt(blockRowOffset + xToBlock[x]));
            }

            return labels;
        }

        // 260623Claude: クラス ID 配列をそのまま 8bit indexed TIFF として保存する (恒等グレーパレット = サンプル値が ID)。
        public static void WriteLabelTiff(string path, byte[] labels, int width, int height)
            => WriteGray8Tiff(path, labels, width, height);

        // 260623Claude: 表示用 RGB 予測マップを出力寸法 (width×height) へ最近傍スケールして保存する (PNG)。見た目確認専用。
        public static void WriteRgbMapPng(string path, MineralMapImage image, PtsClassificationMapResult map, int width, int height)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(map);
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            int gridWidth = image.Width;
            int[] xToBlock = BuildAxisBlockMap(width, gridWidth);
            int[] yToBlock = BuildAxisBlockMap(height, map.GridHeight);
            var palette = image.Palette;
            var pixels = new byte[checked(width * height * 3)];
            for (int y = 0; y < height; y++)
            {
                int blockRowOffset = yToBlock[y] * gridWidth;
                int rowOffset = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    int display = (int)image.Values[blockRowOffset + xToBlock[x]];
                    (byte r, byte g, byte b) = palette[display];
                    int p = rowOffset + x * 3;
                    pixels[p] = r;
                    pixels[p + 1] = g;
                    pixels[p + 2] = b;
                }
            }

            WriteRgb24Image(path, pixels, width, height, ImageFormat.Png);
        }

        // 260717Codex: Render the current on-screen legend as a publication-friendly white-background PNG.
        public static void WriteLegendPng(string path, MineralMapImage image, int totalBlockCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(image);

            const int padding = 12;
            const int rowHeight = 28;
            const int swatchSize = 18;
            const int textGap = 8;
            // 260717Codex: Windows Forms supplies this system font for the lifetime of the process.
            Font font = SystemFonts.MessageBoxFont!;
            int textWidth;
            using (var measurementBitmap = new Bitmap(1, 1))
            {
                // 260717Codex: Measure at the same DPI used by the final legend so long mineral names are not clipped.
                measurementBitmap.SetResolution(144, 144);
                using Graphics measurement = Graphics.FromImage(measurementBitmap);
                textWidth = image.Legend.Count == 0
                    ? 0
                    : (int)Math.Ceiling(image.Legend.Max(entry =>
                        measurement.MeasureString(FormatLegendText(entry, totalBlockCount), font).Width));
            }

            int width = Math.Max(320, padding * 2 + swatchSize + textGap + textWidth);
            int height = Math.Max(rowHeight, padding * 2 + image.Legend.Count * rowHeight);
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(144, 144);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            for (int i = 0; i < image.Legend.Count; i++)
            {
                MineralMapLegendEntry entry = image.Legend[i];
                int top = padding + i * rowHeight;
                var swatch = new Rectangle(padding, top + (rowHeight - swatchSize) / 2, swatchSize, swatchSize);
                using (var brush = new SolidBrush(entry.Color))
                    graphics.FillRectangle(brush, swatch);
                graphics.DrawRectangle(Pens.Gray, swatch);
                using var textBrush = new SolidBrush(Color.Black);
                graphics.DrawString(
                    FormatLegendText(entry, totalBlockCount),
                    font,
                    textBrush,
                    padding + swatchSize + textGap,
                    top + (rowHeight - font.Height) / 2f);
            }

            bitmap.Save(path, ImageFormat.Png);
        }

        // 260717Codex: Export one CSV row per displayed legend category with exact RGB, count, percentage, and assignment source.
        public static void WriteLegendCsv(string path, MineralMapImage image, int totalBlockCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(image);

            var sb = new StringBuilder();
            sb.AppendLine("display_order,category,mineral_name,hex,r,g,b,block_count,percentage,assignment");
            for (int i = 0; i < image.Legend.Count; i++)
            {
                MineralMapLegendEntry entry = image.Legend[i];
                double percentage = CalculatePercentage(entry.BlockCount, totalBlockCount);
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(EscapeCsv(GetLegendCategory(entry))).Append(',')
                    .Append(EscapeCsv(entry.MineralName)).Append(',')
                    .Append(ToHex(entry.Color)).Append(',')
                    .Append(entry.Color.R.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.Color.G.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.Color.B.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(entry.BlockCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(percentage.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .AppendLine(entry.Assignment.ToString());
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // 260717Codex: Preserve the displayed palette, map conditions, and every mineral aggregated into Other for reproducibility.
        public static void WriteLegendJson(
            string path,
            MineralMapImage image,
            PtsClassificationMapResult map)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(map);

            var document = new
            {
                version = 1,
                ptsFile = Path.GetFileName(map.PtsFilePath),
                modelName = map.ModelName,
                binSize = map.BinSize,
                leadingSweepCount = map.LeadingSweepCount,
                gridWidth = map.GridWidth,
                gridHeight = map.GridHeight,
                totalBlockCount = map.BlockCount,
                legend = image.Legend.Select((entry, index) => new
                {
                    displayOrder = index,
                    category = GetLegendCategory(entry),
                    mineralName = entry.MineralName,
                    color = new { hex = ToHex(entry.Color), r = entry.Color.R, g = entry.Color.G, b = entry.Color.B },
                    blockCount = entry.BlockCount,
                    percentage = CalculatePercentage(entry.BlockCount, map.BlockCount),
                    assignment = entry.Assignment.ToString()
                }),
                otherMembers = image.OtherMembers.Select(entry => new
                {
                    mineralName = entry.MineralName,
                    blockCount = entry.BlockCount,
                    percentage = CalculatePercentage(entry.BlockCount, map.BlockCount)
                })
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(document, options), new UTF8Encoding(false));
        }

        // 260623Claude: 出力軸 (targetSize 画素) の各座標が属するブロック index を最近傍で割り付ける。FOV が同一であることが前提。
        private static int[] BuildAxisBlockMap(int targetSize, int gridSize)
        {
            var map = new int[targetSize];
            for (int i = 0; i < targetSize; i++)
                map[i] = (int)Math.Min(gridSize - 1L, (long)i * gridSize / targetSize);
            return map;
        }

        // 260623Claude: id,name の対応表。0=background と 255=Unknown を常に含めるので、両モデル型で同じ読み取りができる。
        public static void WriteClassesCsv(string path, IReadOnlyList<string> labelNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(labelNames);

            var sb = new StringBuilder();
            sb.AppendLine("id,name");
            sb.Append(BackgroundId.ToString(CultureInfo.InvariantCulture)).AppendLine(",background");
            for (int i = 0; i < labelNames.Count; i++)
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(',').AppendLine(EscapeCsv(labelNames[i]));
            sb.Append(UnknownId.ToString(CultureInfo.InvariantCulture)).Append(',').AppendLine(MineralUnknownDetector.UnknownDisplayName);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // 260623Claude: ROI を後から重ねるための寸法・座標・条件を記録する。BSE/ラベルは同一 width/height/原点。
        public static void WriteMetadataJson(
            string path,
            PtsClassificationMapResult map,
            int width,
            int height)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(map);

            var metadata = new
            {
                ptsFile = Path.GetFileName(map.PtsFilePath),
                modelName = map.ModelName,
                width,
                height,
                binSize = map.BinSize,
                gridWidth = map.GridWidth,
                gridHeight = map.GridHeight,
                origin = new { x = 0, y = 0 },
                leadingSweepCount = map.LeadingSweepCount,
                // 260623Claude: ROI は同一視野・同寸法の外部 BSE 電子像 (JEOL View0xx IMG1.bmp 等) 上で作成し、このラベルへ適用する想定。
                bse = "external electron image (same field of view, same width/height); not emitted by the app.",
                labelImage = new
                {
                    dtype = "uint8",
                    background = (int)BackgroundId,
                    unknown = (int)UnknownId,
                    mineralIdOffset = 1,
                    note = "pred[y,x] = MapLabelId(top1LabelId[ floor(y*gridHeight/height)*gridWidth + floor(x*gridWidth/width) ]); minerals are labelEncoder index + 1.",
                },
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(metadata, options), new UTF8Encoding(false));
        }

        // 260623Claude: 表示中のマップ視野を、外部 BSE と同寸法 (TargetWidth×TargetHeight) の予測画像群として書き出す。
        // ROI を引く BSE はユーザーの外部電子像を使うため、アプリは BSE を出力しない (寸法だけ合わせる)。
        // スイープ依存ファイル (label/rgb/metadata) は名前にスイープ数を埋め込み、同フォルダへ別スイープを重ねても上書きしない。
        // classes.csv はスイープ非依存なので共通名でよい。
        public static void ExportCurrentView(
            string outputDirectory,
            PtsClassificationMapResult map,
            MineralMapImage mapImage,
            IReadOnlyList<string> labelNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(mapImage);

            Directory.CreateDirectory(outputDirectory);
            int width = TargetWidth;
            int height = TargetHeight;
            // 260717Codex: Include every analysis condition that can otherwise overwrite a different map in the same folder.
            string exportStem = BuildExportStem(map);

            // 260717Codex: Retain the legacy common file and add a condition-specific copy for non-overwriting reproducibility.
            WriteClassesCsv(Path.Combine(outputDirectory, "classes.csv"), labelNames);
            WriteClassesCsv(Path.Combine(outputDirectory, $"classes_{exportStem}.csv"), labelNames);
            WriteRgbMapPng(Path.Combine(outputDirectory, $"pred_rgb_{exportStem}.png"), mapImage, map, width, height);
            WriteLabelTiff(
                Path.Combine(outputDirectory, $"pred_label_{exportStem}.tif"),
                BuildLabelMap(map, width, height),
                width,
                height);
            WriteMetadataJson(Path.Combine(outputDirectory, $"metadata_{exportStem}.json"), map, width, height);
            WriteLegendPng(Path.Combine(outputDirectory, $"legend_{exportStem}.png"), mapImage, map.BlockCount);
            WriteLegendCsv(Path.Combine(outputDirectory, $"legend_{exportStem}.csv"), mapImage, map.BlockCount);
            WriteLegendJson(Path.Combine(outputDirectory, $"legend_{exportStem}.json"), mapImage, map);
        }

        // 260717Codex: Build a deterministic filename stem from PTS, model, binning, and sweep conditions.
        private static string BuildExportStem(PtsClassificationMapResult map)
        {
            string ptsName = SanitizeFileNamePart(Path.GetFileNameWithoutExtension(map.PtsFilePath), "pts");
            string modelName = SanitizeFileNamePart(map.ModelName, "model");
            string binSize = map.BinSize.ToString("D2", CultureInfo.InvariantCulture);
            return $"{ptsName}_{modelName}_bin{binSize}_{SweepSuffix(map.LeadingSweepCount)}";
        }

        // 260717Codex: Replace only characters Windows disallows while retaining readable PTS and model names.
        private static string SanitizeFileNamePart(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            string sanitized = new(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            sanitized = sanitized.Trim().TrimEnd('.');
            return sanitized.Length == 0 ? fallback : sanitized;
        }

        // 260623Claude: スイープ依存ファイルのサフィックス (例: sweep05)。全スイープ読みは sweepAll。0 埋め2桁、超過時は桁数が増える。
        public static string SweepSuffix(int? leadingSweepCount)
            => leadingSweepCount.HasValue
                ? $"sweep{leadingSweepCount.Value.ToString("D2", CultureInfo.InvariantCulture)}"
                : "sweepAll";

        // 260623Claude: 行優先 uint8 を 8bit indexed TIFF (恒等グレーパレット) として保存。ImageJ では 8bit grayscale としてサンプル値=ID で読める。
        private static void WriteGray8Tiff(string path, byte[] values, int width, int height)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (values.Length != (long)width * height)
                throw new ArgumentException("画素数が width×height と一致しません。", nameof(values));

            using var bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            ColorPalette palette = bitmap.Palette;
            for (int i = 0; i < 256; i++)
                palette.Entries[i] = Color.FromArgb(i, i, i);
            bitmap.Palette = palette;

            CopyRows(bitmap, values, width, height, bytesPerPixel: 1, PixelFormat.Format8bppIndexed);
            bitmap.Save(path, ImageFormat.Tiff);
        }

        // 260623Claude: 行優先 BGR... ではなく RGB バイト列を 24bppRgb へ書き込む (GDI は BGR 順なので入れ替える)。
        private static void WriteRgb24Image(string path, byte[] rgb, int width, int height, ImageFormat format)
        {
            if (rgb.Length != (long)width * height * 3)
                throw new ArgumentException("画素数が width×height×3 と一致しません。", nameof(rgb));

            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var rowBuffer = new byte[width * 3];
                for (int y = 0; y < height; y++)
                {
                    int srcOffset = y * width * 3;
                    for (int x = 0; x < width; x++)
                    {
                        int s = srcOffset + x * 3;
                        int d = x * 3;
                        rowBuffer[d] = rgb[s + 2];     // B
                        rowBuffer[d + 1] = rgb[s + 1]; // G
                        rowBuffer[d + 2] = rgb[s];     // R
                    }
                    Marshal.Copy(rowBuffer, 0, data.Scan0 + y * data.Stride, rowBuffer.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            bitmap.Save(path, format);
        }

        // 260623Claude: LockBits の Stride を尊重して行ごとにコピーする (8bpp の各行末パディングを跨がない)。
        private static void CopyRows(Bitmap bitmap, byte[] values, int width, int height, int bytesPerPixel, PixelFormat format)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, format);
            try
            {
                int rowBytes = width * bytesPerPixel;
                for (int y = 0; y < height; y++)
                    Marshal.Copy(values, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        // 260717Codex: Match the on-screen legend's mineral-name and whole-map percentage format.
        private static string FormatLegendText(MineralMapLegendEntry entry, int totalBlockCount)
            => string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1:F2}%",
                entry.MineralName,
                CalculatePercentage(entry.BlockCount, totalBlockCount));

        // 260717Codex: Use all map blocks as the shared denominator for UI and exported legends.
        private static double CalculatePercentage(int blockCount, int totalBlockCount)
            => totalBlockCount > 0 ? blockCount * 100.0 / totalBlockCount : 0;

        // 260717Codex: Give fixed aggregate rows explicit machine-readable category names.
        private static string GetLegendCategory(MineralMapLegendEntry entry)
        {
            if (entry.IsOther)
                return "Other";
            if (entry.IsUnclassified)
                return "Unclassified";
            return entry.Assignment == MineralColorAssignment.Fixed ? "Unknown" : "Mineral";
        }

        // 260717Codex: Record the exact opaque RGB color in a common publication-tool format.
        private static string ToHex(Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        // 260623Claude: 鉱物名にカンマや引用符が含まれても CSV を壊さないよう最小限のクォートを行う。
        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
