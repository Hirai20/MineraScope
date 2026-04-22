using System;
using System.Linq;
using System.Text;

namespace MineraScope
{
    // 260416Codex: DTSA-II 用スクリプト生成を Form の外へ分離します。
    internal sealed class SimulationScriptGenerator
    {
        // 260416Codex: スクリプト生成で使う共通の密度定数を 1 か所にまとめます。
        private const double DefaultDensity = 3;

        // 260416Codex: UI 状態に触れず、実行 DTO から Python スクリプト文字列を組み立てます。
        public string Generate(SimulationPropety property, int? parallelIndex = null)
        {
            StringBuilder builder = new();
            string mineralGroupName = property.MineralGropName;
            (string ElementName, double Weight)[][] atoms = property.Atoms1;
            string[] outputFiles = property.OutputFile;

            // 260416Codex: import と実行条件を先に並べ、スクリプト全体の流れを追いやすくします。
            builder.AppendLine("import dtsa2.mcSimulate3 as mc\r\nimport os\r\noutput_dir = r\"" + property.OutPutFolder + "\"");
            builder.AppendLine("det = findDetector(\"" + property.DetectorName + "\")");
            builder.AppendLine("e0 = " + property.BeamEnergy);
            builder.AppendLine("dose =" + property.ProbeCurrent + " *" + property.LiveTime);
            builder.AppendLine("cThickness = " + property.CarbonCoatThickness);
            builder.AppendLine("carbonCoating = epq.MaterialFactory.createPureElement(epq.Element.C)");

            // 260416Codex: 計画済みデータから組成配列と出力ファイル名をそのまま整形します。
            builder.AppendLine("Weights = [");
            foreach (var atom in atoms)
            {
                string joined = string.Join(", ", atom.Select(x => x.Weight.ToString("F3")));
                builder.AppendLine($"  [{joined}],");
            }

            builder.AppendLine("]");
            builder.AppendLine("FileNames = [");
            foreach (string fileName in outputFiles)
            {
                string joined = string.Join(", ", fileName);
                builder.AppendLine($" \"{mineralGroupName}_ {joined}\",");
            }

            builder.AppendLine("]");
            builder.AppendLine("for i in range(" + property.Count + "):");
            builder.Append("\tfor idx, (");

            // 260416Codex: 元素順を使い回し、重量ローカル変数と EPQ material 定義を揃えます。
            string[] elementNames = atoms[0].Select(x => x.ElementName).ToArray();
            builder.Append(string.Join(", ", elementNames.Select(static element => $"{element}_weight")));
            builder.AppendLine(") in enumerate(Weights):");
            foreach (string name in elementNames)
            {
                builder.AppendLine($"\r\n\t\t{ToLowerInvariant(name)} = {name}_weight");
            }

            builder.Append($"\r\n\t\t{mineralGroupName} = epq.Material(epq.Composition([");
            foreach (string name in elementNames)
            {
                builder.Append($"epq.Element.{name}, ");
            }

            builder.Remove(builder.Length - 2, 2);
            builder.Append("],[");
            foreach (string name in elementNames)
            {
                builder.Append(ToLowerInvariant(name) + ",");
            }

            builder.Remove(builder.Length - 1, 1);
            builder.Append("] ),");
            builder.AppendLine("epq.ToSI.gPerCC(" + DefaultDensity + "))");

            // 260416Codex: 出力ファイル名規則と保存処理も generator 側へ寄せます。
            builder.AppendLine("\t\tprint(\"Starting simulation {0}(mix {1})...\".format(i + 1, idx + 1))");
            builder.AppendLine("\t\tsd = mc.coatedSubstrate(carbonCoating, cThickness," + mineralGroupName + " , det,dose = dose)");
            builder.AppendLine($"\t\toutName = \"{{}}_{{}}run_{{}}_{parallelIndex}.emsa\".format(FileNames[idx],idx+1,i+1)");
            builder.AppendLine("\t\toutput_file = os.path.join(output_dir, outName)");
            builder.AppendLine("\t\tsd.rename(FileNames[idx])");
            builder.AppendLine("\r\n\t\tprint(\"Simulation {0} saved to {1}\".format(i + 1, output_file))");
            builder.AppendLine("\t\tsd.save(output_file)");
            builder.AppendLine("exit()");
            return builder.ToString();
        }

        // 260416Codex: スクリプト専用の小文字化ルールを generator 内に閉じ込めます。
        private static string ToLowerInvariant(string value) => value.ToLowerInvariant();
    }
}
