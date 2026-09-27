using System.Globalization;

namespace MineraScope
{
    // 260828Codex: 表示した生スペクトル、分析条件、分類結果を単一 CSV へ渡す共有契約として保持する。
    internal sealed record DisplayedSpectrumExportContext(
        PtsPixelSpectrum Spectrum,
        string FileName,
        int? LeadingSweepCount,
        string ModelName,
        bool UnknownDetectionRequested,
        bool DetectUnknownForPrediction,
        MineralClassificationPredictionResult? Classification)
    {
        // 260828Codex: 画面表示と保存物で同じ座標範囲表記を使う。
        public string DescribeRange()
            => string.Create(CultureInfo.InvariantCulture, $"X {Spectrum.BinLeft}-{Spectrum.BinRight}, Y {Spectrum.BinTop}-{Spectrum.BinBottom}");

        // 260828Codex: 画面表示と保存物のビニング表記を乗算記号へ統一する。
        public string DescribeBinning()
            => string.Create(CultureInfo.InvariantCulture, $"{Spectrum.RequestedBinSize}×{Spectrum.RequestedBinSize}");

        // 260828Codex: sweep の指定が無い場合は全 sweep を使ったことを明示する。
        public string DescribeSweep()
            => LeadingSweepCount?.ToString(CultureInfo.InvariantCulture) ?? "全数";
    }
}
