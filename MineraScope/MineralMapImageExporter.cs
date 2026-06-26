using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
            string suffix = SweepSuffix(map.LeadingSweepCount);

            WriteClassesCsv(Path.Combine(outputDirectory, "classes.csv"), labelNames);
            WriteRgbMapPng(Path.Combine(outputDirectory, $"pred_rgb_{suffix}.png"), mapImage, map, width, height);
            WriteLabelTiff(
                Path.Combine(outputDirectory, $"pred_label_{suffix}.tif"),
                BuildLabelMap(map, width, height),
                width,
                height);
            WriteMetadataJson(Path.Combine(outputDirectory, $"metadata_{suffix}.json"), map, width, height);
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
