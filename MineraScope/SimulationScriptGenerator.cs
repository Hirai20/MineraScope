using System.Globalization;
using System.Linq;
using System.Text;

namespace MineraScope
{
    // 260507Codex: manifest 予約済み spectrum を、指定ファイル名そのままで保存する DTSA-II スクリプトに変換します。
    internal sealed class SimulationScriptGenerator
    {
        private const double DefaultDensity = 3;

        // 260507Codex: 1 spectrum につき 1 ファイルを出力し、ファイル名へ端成分比率を埋め込まない形にします。
        public string Generate(SimulationProperty property, int? parallelIndex = null)
        {
            StringBuilder builder = new();
            (string ElementName, double Weight)[][] atoms = property.Atoms1;
            string[] outputFiles = property.OutputFiles;
            // 260622Claude: ばらつき幅が正のときだけカーボン膜厚を spectrum ごとに ±x% 振る (0 なら従来どおり基準値固定)。
            bool jitterCarbon = property.CarbonCoatThicknessJitterPercent > 0;

            builder.AppendLine("import dtsa2.mcSimulate3 as mc");
            builder.AppendLine("import os");
            // 260528Codex: DTSA-II の保存完了を外側の進捗表示へ即時通知できるよう stdout を明示 flush します。
            builder.AppendLine("import sys");
            if (jitterCarbon)
                builder.AppendLine("import random");
            builder.AppendLine("output_dir = " + ToPythonString(property.OutputFolder));
            builder.AppendLine("det = findDetector(" + ToPythonString(property.DetectorName) + ")");
            builder.AppendLine("e0 = " + ToInvariantString(property.BeamEnergy));
            builder.AppendLine("dose = " + ToInvariantString(property.ProbeCurrent) + " * " + ToInvariantString(property.LiveTime));
            builder.AppendLine("cThickness = " + ToInvariantString(property.CarbonCoatThickness));
            if (jitterCarbon)
                builder.AppendLine("cJitter = " + ToInvariantString(property.CarbonCoatThicknessJitterPercent / 100));
            builder.AppendLine("carbonCoating = epq.MaterialFactory.createPureElement(epq.Element.C)");
            builder.AppendLine("Weights = [");

            foreach (var atom in atoms)
            {
                string joined = string.Join(", ", atom.Select(x => ToInvariantString(x.Weight)));
                builder.AppendLine($"  [{joined}],");
            }

            builder.AppendLine("]");
            builder.AppendLine("FileNames = [");
            foreach (string fileName in outputFiles)
                builder.AppendLine("  " + ToPythonString(fileName) + ",");

            builder.AppendLine("]");
            builder.Append("for idx, (");

            string[] elementNames = atoms[0].Select(x => x.ElementName).ToArray();
            builder.Append(string.Join(", ", elementNames.Select(static element => $"{element}_weight")));
            builder.AppendLine(") in enumerate(Weights):");

            foreach (string name in elementNames)
            {
                // 260508Codex: 単なる ToLowerInvariant wrapper を避け、Python 変数名化をその場で読み取れるようにします。
                builder.AppendLine($"\t{name.ToLowerInvariant()} = {name}_weight");
            }

            builder.Append("\tmaterial = epq.Material(epq.Composition([");
            foreach (string name in elementNames)
            {
                builder.Append($"epq.Element.{name}, ");
            }

            builder.Remove(builder.Length - 2, 2);
            builder.Append("], [");
            foreach (string name in elementNames)
                builder.Append(name.ToLowerInvariant() + ", ");

            builder.Remove(builder.Length - 2, 2);
            builder.AppendLine("]), epq.ToSI.gPerCC(" + ToInvariantString(DefaultDensity) + "))");
            builder.AppendLine("\tprint(\"Starting simulation {0}...\".format(idx + 1))");
            if (jitterCarbon)
            {
                // 260622Claude: spectrum ごとに基準膜厚へ ±cJitter の一様乱数を掛け、負値は 0 に丸める。
                builder.AppendLine("\teffectiveThickness = max(0.0, cThickness * (1.0 + random.uniform(-cJitter, cJitter)))");
                builder.AppendLine("\tsd = mc.coatedSubstrate(carbonCoating, effectiveThickness, material, det, dose = dose)");
            }
            else
                builder.AppendLine("\tsd = mc.coatedSubstrate(carbonCoating, cThickness, material, det, dose = dose)");
            builder.AppendLine("\toutput_file = os.path.join(output_dir, FileNames[idx])");
            builder.AppendLine("\tsd.rename(FileNames[idx])");
            builder.AppendLine("\tsd.save(output_file)");
            builder.AppendLine("\tprint(\"MINERASCOPE_SPECTRUM_SAVED|{0}|{1}\".format(idx + 1, output_file))");
            builder.AppendLine("\tsys.stdout.flush()");
            builder.AppendLine("exit()");
            return builder.ToString();
        }

        // 260507Codex: Python の文字列リテラルへ安全に埋め込めるよう最低限のエスケープを行います。
        private static string ToPythonString(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // 260507Codex: DTSA-II スクリプト内の数値はカルチャに依存しない小数点表記にします。
        private static string ToInvariantString(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);

    }
}
