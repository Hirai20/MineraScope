using System;
using System.Buffers.Binary;
using System.IO;

namespace MineraScope
{
    // 260613Claude: 独自バイナリ .eds から 2048ch の int32 カウント列を取り出すリーダー。
    // 形式はサンプル .eds 群から復元したもので、byte 454 の int32 が ch 数 (=2048)、
    // その直後 byte 458 から ch 数ぶんの int32 LE カウントが並ぶ。energy 軸は 10 eV/ch
    // (Si K ピークが ch175=1.75keV に出ることで確認) で .msa の XUNITS=eV 表示と揃う。
    internal static class EdsSpectrumReader
    {
        // 260613Claude: グラフ X 軸を .msa (XUNITS=eV, XPERCHAN≈10) と同じ eV スケールに合わせる。
        // 260809Claude: この 2 つは .eds 形式側の定数なので、モデル軸 (SpectrumAxis.Default) とは意図的に結合しない。
        //   下の HasChannelCountAnchor はファイルが自己申告する ch 数が ChannelCount と一致することを検証条件にしているため、
        //   モデル軸を可変にした時点で結合していると、既存の 2048ch .eds が全て読めなくなる (PtsParameter と同じ理由)。
        public const double EnergyPerChannelEv = 10.0;

        private const int ChannelCount = 2048;

        // 260613Claude: 既知サンプルでは ch 数フィールドが byte 454 に固定。直後がカウント列。
        private const int ChannelCountFieldOffset = 454;

        // 260613Claude: 固定オフセットが外れたファイルに備え、ヘッダー前半だけ走査して anchor を探す。
        private const int HeaderScanLimit = 512;

        public static bool IsEdsFile(string? path) =>
            !string.IsNullOrEmpty(path)
            && Path.GetExtension(path).Equals(".eds", StringComparison.OrdinalIgnoreCase);

        // 260613Claude: 読み込めない/形式不一致は例外にせず null を返し、呼び出し側の判定スキップに任せる。
        // 260901Codex: 既存呼び出しでは、内部読込の I/O 例外を従来どおり null に変換します。
        public static int[]? TryReadCounts(string filePath)
        {
            try
            {
                return ReadCounts(filePath);
            }
            // 260903Codex: 従来どおり読込失敗を null に変換しつつ、同じ処理の catch を一つにまとめます。
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // 260901Codex: 分類学習だけで一時的な I/O 失敗を再試行できるよう、例外を保持する内部経路を分けます。
        internal static int[]? ReadCounts(string filePath) => TryParseCounts(File.ReadAllBytes(filePath));

        // 260613Claude: ch 数フィールドの位置を確定し、その直後から 2048 個の非負 int32 を取り出す。
        // 260903Codex: ファイル読込を行う公開 overload と区別できる名前にします。
        private static int[]? TryParseCounts(ReadOnlySpan<byte> bytes)
        {
            int dataOffset = ResolveSpectrumOffset(bytes);
            if (dataOffset < 0)
                return null;

            var counts = new int[ChannelCount];
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(dataOffset + channel * 4, 4));
                // 260613Claude: カウントは非負。負値が出たらレイアウト不一致とみなして判定対象から除外する。
                if (value < 0)
                    return null;

                counts[channel] = value;
            }

            return counts;
        }

        private static int ResolveSpectrumOffset(ReadOnlySpan<byte> bytes)
        {
            if (HasChannelCountAnchor(bytes, ChannelCountFieldOffset))
                return ChannelCountFieldOffset + 4;

            int limit = Math.Min(HeaderScanLimit, bytes.Length - 4);
            for (int offset = 0; offset <= limit; offset++)
                if (HasChannelCountAnchor(bytes, offset))
                    return offset + 4;

            return -1;
        }

        // 260613Claude: 指定位置の int32 が ch 数で、かつカウント列がファイル内に収まるかを確認する。
        private static bool HasChannelCountAnchor(ReadOnlySpan<byte> bytes, int fieldOffset)
        {
            if (fieldOffset < 0 || fieldOffset + 4 > bytes.Length)
                return false;

            int dataOffset = fieldOffset + 4;
            if (dataOffset + ChannelCount * 4 > bytes.Length)
                return false;

            return BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(fieldOffset, 4)) == ChannelCount;
        }
    }
}
