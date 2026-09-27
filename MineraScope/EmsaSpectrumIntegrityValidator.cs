using System;
using System.Globalization;
using System.IO;

namespace MineraScope
{
    // 260901Codex: Validate completed DTSA-II EMSA files before a Pending entry is recovered or selected for training.
    internal static class EmsaSpectrumIntegrityValidator
    {
        // 260901Codex: Header comparisons allow only text-format rounding while keeping pool physics mismatches visible.
        private const double HeaderTolerance = 1e-6;

        // 260901Codex: LIVETIME is intentionally ignored because current DTSA-II output stores dose there; manifest live time remains authoritative.
        public static bool TryValidate(
            string filePath,
            SemEdxCondition expectedCondition,
            out string failureReason)
        {
            ArgumentNullException.ThrowIfNull(expectedCondition);
            if (!File.Exists(filePath))
            {
                failureReason = "出力ファイルが見つかりませんでした。";
                return false;
            }

            bool spectrumStarted = false;
            bool endOfDataFound = false;
            int valueCount = 0;
            double? numberOfPoints = null;
            double? beamEnergy = null;
            double? channelWidth = null;
            double? zeroOffset = null;
            // 260901Codex: Elevation and azimuth are directly comparable detector-geometry headers in DTSA-II EMSA output.
            double? elevation = null;
            double? azimuth = null;

            try
            {
                foreach (string sourceLine in File.ReadLines(filePath))
                {
                    string line = sourceLine.Trim();
                    if (line.Length == 0)
                        continue;

                    if (line.StartsWith('#'))
                    {
                        int separatorIndex = line.IndexOf(':');
                        string key = (separatorIndex >= 0 ? line[..separatorIndex] : line)
                            .TrimStart('#')
                            .Trim();
                        string value = separatorIndex >= 0 ? line[(separatorIndex + 1)..].Trim() : string.Empty;

                        if (string.Equals(key, "SPECTRUM", StringComparison.OrdinalIgnoreCase))
                        {
                            spectrumStarted = true;
                            continue;
                        }

                        if (string.Equals(key, "ENDOFDATA", StringComparison.OrdinalIgnoreCase))
                        {
                            endOfDataFound = true;
                            spectrumStarted = false;
                            continue;
                        }

                        if (!TryCaptureHeaderNumber(key, value, "NPOINTS", ref numberOfPoints, out failureReason)
                            || !TryCaptureHeaderNumber(key, value, "BEAMKV", ref beamEnergy, out failureReason)
                            || !TryCaptureHeaderNumber(key, value, "XPERCHAN", ref channelWidth, out failureReason)
                            || !TryCaptureHeaderNumber(key, value, "OFFSET", ref zeroOffset, out failureReason)
                            || !TryCaptureHeaderNumber(key, value, "ELEVANGLE", ref elevation, out failureReason)
                            || !TryCaptureHeaderNumber(key, value, "AZIMANGLE", ref azimuth, out failureReason))
                            return false;

                        continue;
                    }

                    if (!spectrumStarted || endOfDataFound)
                    {
                        failureReason = "#SPECTRUM と #ENDOFDATA の外側に数値データがあります。";
                        return false;
                    }

                    string numericText = line.TrimEnd(',').Trim();
                    // 260901Codex: Match SpectrumDataLoader's float domain so integrity success guarantees the numeric loader can consume the value.
                    if (!float.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out float spectrumValue)
                        || !float.IsFinite(spectrumValue)
                        || spectrumValue < 0)
                    {
                        failureReason = $"無効なスペクトル値があります: {numericText}";
                        return false;
                    }

                    valueCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failureReason = $"スペクトルファイルを読み込めませんでした: {ex.Message}";
                return false;
            }

            if (!endOfDataFound)
            {
                failureReason = "#ENDOFDATA がありません。";
                return false;
            }

            if (valueCount != SpectrumDataLoader.SpectrumLength)
            {
                failureReason = $"スペクトル長が {valueCount} 点です。{SpectrumDataLoader.SpectrumLength} 点必要です。";
                return false;
            }

            var detector = expectedCondition.GetDetectorProfile();
            if (detector.ChannelCount != SpectrumDataLoader.SpectrumLength)
            {
                failureReason = $"manifestの検出器チャネル数が {detector.ChannelCount} 点で、学習入力長と一致しません。";
                return false;
            }

            // 260901Codex: Legacy EMSA can omit optional headers; compare every available physical header and rely on manifest plus channel validity when absent.
            if (!HeaderMatches(numberOfPoints, SpectrumDataLoader.SpectrumLength))
            {
                failureReason = "#NPOINTS が学習入力長と一致しません。";
                return false;
            }

            if (!HeaderMatches(beamEnergy, expectedCondition.BeamEnergy))
            {
                failureReason = "#BEAMKV がmanifestの加速電圧と一致しません。";
                return false;
            }

            if (!HeaderMatches(channelWidth, detector.ChannelWidth))
            {
                failureReason = "#XPERCHAN がmanifestの検出器軸と一致しません。";
                return false;
            }

            if (!HeaderMatches(zeroOffset, detector.ZeroOffset))
            {
                failureReason = "#OFFSET がmanifestの検出器軸と一致しません。";
                return false;
            }

            // 260901Codex: Compare geometry when present, while retaining the same missing-header compatibility boundary as the axis fields.
            if (!HeaderMatches(elevation, detector.Elevation))
            {
                failureReason = "#ELEVANGLE がmanifestの検出器配置と一致しません。";
                return false;
            }

            if (!HeaderMatches(azimuth, detector.Azimuth))
            {
                failureReason = "#AZIMANGLE がmanifestの検出器配置と一致しません。";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        // 260901Codex: Parse only the requested header, leaving absent optional headers as null for legacy compatibility.
        private static bool TryCaptureHeaderNumber(
            string actualKey,
            string text,
            string requestedKey,
            ref double? destination,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (!string.Equals(actualKey, requestedKey, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value))
            {
                failureReason = $"#{requestedKey}を数値として読み込めません。";
                return false;
            }

            destination = value;
            return true;
        }

        // 260901Codex: Missing legacy headers are not guessed; present headers must agree with the manifest condition.
        private static bool HeaderMatches(double? actual, double expected) =>
            actual is null || Math.Abs(actual.Value - expected) <= HeaderTolerance;
    }
}
