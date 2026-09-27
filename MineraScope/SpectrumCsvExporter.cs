using System.Globalization;
using System.Text;

namespace MineraScope
{
    // 260716Claude: 表示中スペクトル1本を「エネルギー,カウント」の2列CSVへ書き出す (FormMain/AnalyzerForm 共用)。
    //               書式は SpectrumPredictionExporter と同じ UTF-8 BOM・CRLF・InvariantCulture。値は数値のみで RFC4180 引用は不要。
    internal static class SpectrumCsvExporter
    {
        private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

        // 260716Claude: energyHeader は単位込みのX列名 (例 "Energy (keV)")。呼び出し側の軸単位に合わせて渡す。
        public static void Write(string path, string energyHeader, IEnumerable<(double Energy, double Counts)> points)
        {
            var sb = new StringBuilder();
            sb.Append($"{energyHeader},Counts\r\n");

            foreach (var (energy, counts) in points)
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"{energy},{counts}\r\n"));

            File.WriteAllText(path, sb.ToString(), Utf8Bom);
        }

        // 260828Codex: 表示中スペクトルの分析文脈と予測結果を、正式 EMSA ではない自己記述 CSV へ一体で保存する。
        public static void WriteDisplayedSpectrum(string path, DisplayedSpectrumExportContext context)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(context);

            PtsPixelSpectrum spectrum = context.Spectrum;
            // 260828Codex: 複数のヘッダーで使う表示文字列は一度だけ組み立てる。
            string rangeText = context.DescribeRange();
            string binningText = context.DescribeBinning();
            var sb = new StringBuilder();
            // 260828Codex: 先頭行で正式 EMSA ではなく MineraScope 独自形式だと判別できるようにする。
            AppendHeader(sb, "#FORMAT", "MineraScope Displayed Spectrum CSV");
            AppendHeader(sb, "#VERSION", "1.0");
            AppendHeader(sb, "#TITLE", $"{Path.GetFileNameWithoutExtension(context.FileName)} [{rangeText}] {binningText}");
            AppendHeader(sb, "#NPOINTS", spectrum.ChannelCount.ToString(CultureInfo.InvariantCulture));
            AppendHeader(sb, "#NCOLUMNS", "2");
            AppendHeader(sb, "#XUNITS", "keV");
            AppendHeader(sb, "#YUNITS", "counts");
            AppendHeader(sb, "#DATATYPE", "XY");
            AppendHeader(sb, "##PTS_FILE", context.FileName);
            AppendHeader(sb, "##RANGE", rangeText);
            AppendHeader(sb, "##BINNING", binningText);
            AppendHeader(sb, "##SWEEP", context.DescribeSweep());
            AppendHeader(sb, "##MODEL", context.ModelName);
            AppendHeader(sb, "##UNKNOWN_DETECTION_REQUESTED", context.UnknownDetectionRequested ? "true" : "false");
            AppendHeader(sb, "##DETECT_UNKNOWN_FOR_PREDICTION", context.DetectUnknownForPrediction ? "true" : "false");

            AppendPredictionHeaders(sb, context.Classification);

            AppendHeader(sb, "#SPECTRUM", string.Empty);
            sb.Append("Energy (keV),Counts\r\n");
            for (int channel = 0; channel < spectrum.ChannelCount; channel++)
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"{spectrum.GetEnergy(channel)},{spectrum.GetCount(channel)}\r\n"));
            AppendHeader(sb, "#ENDOFDATA", string.Empty);

            File.WriteAllText(path, sb.ToString(), Utf8Bom);
        }

        // 260828Codex: 分類前・分類失敗時も適用状態を推測せず、未確定として自己記述ヘッダーへ残す。
        private static void AppendPredictionHeaders(StringBuilder sb, MineralClassificationPredictionResult? classification)
        {
            if (classification is null)
            {
                AppendHeader(sb, "##UNKNOWN_DETECTION_APPLIED", "not_available");
                AppendHeader(sb, "##PREDICTION", "not_available");
                return;
            }

            AppendHeader(sb, "##UNKNOWN_DETECTION_APPLIED", classification.UnknownDetectionApplied ? "true" : "false");
            AppendHeader(sb, "##PREDICTION", classification.DisplayMineralName);
            if (classification.IsUnknown)
            {
                AppendHeader(sb, "##FIRST_CANDIDATE", classification.PredictedMineral);
                AppendHeader(sb, "##FIRST_CANDIDATE_CONFIDENCE_PERCENT", SpectrumPredictionBlockFormatter.FormatPercent(classification.Confidence));
            }
            else
            {
                AppendHeader(sb, "##CONFIDENCE_PERCENT", SpectrumPredictionBlockFormatter.FormatPercent(classification.Confidence));
            }

            AppendHeader(sb, "##CANDIDATE_COLUMNS", "rank,mineral,confidence_percent");
            int rank = 1;
            // 260830Codex: 画面・バッチ出力と同じ丸め基準で、表示対象の候補だけを保存する。
            foreach (var (probability, percent) in SpectrumPredictionBlockFormatter.EnumerateVisibleProbabilities(classification.Probabilities))
            {
                // 260828Codex: ユーザー定義の鉱物名にカンマや引用符があっても、候補3列を壊さない。
                AppendHeader(sb, "##CANDIDATE", $"{rank},{EscapeCsvField(probability.MineralName)},{percent}");
                rank++;
            }

            if (!classification.UnknownDetectionApplied ||
                classification.UnknownScore is not { } unknownScore ||
                classification.UnknownThreshold is not { } unknownThreshold)
                return;

            AppendHeader(sb, "##UNKNOWN_SCORE_TYPE", MineralUnknownDetector.DistanceType);
            // 260828Codex: 画面用の G6 ではなく、判定に使った double を往復可能な精度で保存する。
            AppendHeader(sb, "##UNKNOWN_SCORE", unknownScore.ToString("R", CultureInfo.InvariantCulture));
            AppendHeader(sb, "##UNKNOWN_THRESHOLD", unknownThreshold.ToString("R", CultureInfo.InvariantCulture));
            AppendHeader(sb, "##UNKNOWN_COMPARISON_OPERATOR", classification.IsUnknown ? ">" : "<=");
        }

        // 260828Codex: 候補行の値部分は CSV 3 列なので、鉱物名だけ RFC 4180 に従って保護する。
        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
                return value;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        // 260828Codex: EMSA 風にコロン位置をそろえ、外部由来の値に含まれる改行でヘッダー行を壊さない。
        private static void AppendHeader(StringBuilder sb, string key, string? value)
        {
            string safeValue = value?.Replace('\r', ' ').Replace('\n', ' ') ?? string.Empty;
            sb.Append(key.PadRight(13)).Append(':');
            if (safeValue.Length > 0)
                sb.Append(' ').Append(safeValue);
            sb.Append("\r\n");
        }
    }
}
