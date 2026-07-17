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
    }
}
