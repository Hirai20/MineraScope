using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace MineraScope
{
    // 260430Codex: 予測された端成分比率から化学式文字列を組み立てる責務を分離します。
    internal static class MineralFormulaGenerator
    {
        // 260430Codex: 鉱物 DB の定義順に沿って、予測比率から化学式を生成します。
        public static string Generate(
            Dictionary<string, float> predictedRatios,
            string targetMineralName,
            string assemblyPath)
        {
            if (predictedRatios == null || predictedRatios.Count == 0)
                return "";

            string xmlPath = Path.Combine(assemblyPath, "MineralDatabase.xml");
            if (!File.Exists(xmlPath))
                return "";

            XmlSerializer xml = new(typeof(SolidSolution[]));
            using var fs = new FileStream(xmlPath, FileMode.Open);
            var solidSolutions = xml.Deserialize(fs) as SolidSolution[];
            if (solidSolutions == null)
                return "";

            var targetGroup = solidSolutions.FirstOrDefault(solution => solution.Name == targetMineralName);
            if (targetGroup == null)
                return "";

            var targetElements = GetTargetElements(targetGroup);
            var mineralDefinitions = new Dictionary<string, (string Element, double Count)[]>();
            foreach (var member in targetGroup.Members)
            {
                string safeXmlName = member.Name.Trim();
                mineralDefinitions.TryAdd(
                    safeXmlName,
                    member.Elements.Select(element => (element.Item1, element.Item2)).ToArray());
            }

            var finalAtoms = new Dictionary<string, double>();
            foreach (var kvp in predictedRatios)
            {
                string endmemberName = kvp.Key.Trim();
                double ratio = kvp.Value;
                if (!mineralDefinitions.TryGetValue(endmemberName, out var atoms))
                    continue;

                foreach (var atom in atoms)
                {
                    finalAtoms.TryAdd(atom.Element, 0);
                    finalAtoms[atom.Element] += atom.Count * ratio;
                }
            }

            StringBuilder builder = new();
            foreach (var element in targetElements)
            {
                double value = finalAtoms.TryGetValue(element, out double foundValue) ? foundValue : 0;
                builder.Append($"{element}{value:F2}");
            }

            return builder.ToString();
        }

        // 260430Codex: 固溶体メンバーの元素順を保ったまま重複なしで並べます。
        private static List<string> GetTargetElements(SolidSolution targetGroup)
        {
            var targetElements = new List<string>();
            int maxElementCount = targetGroup.Members.Max(member => member.Elements.Length);

            for (int i = 0; i < maxElementCount; i++)
            {
                foreach (var member in targetGroup.Members)
                {
                    if (i >= member.Elements.Length)
                        continue;

                    string symbol = member.Elements[i].Item1;
                    if (!targetElements.Contains(symbol))
                        targetElements.Add(symbol);
                }
            }

            return targetElements;
        }
    }
}
