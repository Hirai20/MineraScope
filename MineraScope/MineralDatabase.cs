using Crystallography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using xFunc.Maths;

namespace MineraScope
{
    public class SolidSolution
    {
        // 260416Codex: XML シリアライズで後から設定されるため、nullability のみ明示します。
        public string Name { get; set; } = null!;

        public string Formula { get; set; } = null!;

        public Mineral[] Members { get; set; } = null!;

        public string[] Constraints { get; set; } = null!;

        public SolidSolution() { }

        public SolidSolution(string name, string formula, Mineral[] members, string[] constraints)
        {
            Name = name;
            Formula = formula;
            Members = members;
            Constraints = constraints;
        }

        public override string ToString() => Name;

        // 260416Codex: resolution == 0 で null を返す既存契約を維持したまま、重複した集計処理を整理します。
        public (string FileName, (string ElementName, double Weight)[] Compositions)[] Divide(double resolution, int targetCount)
        {
            var results = new List<(string FileName, (string ElementName, double Weight)[])>();

            if (Members.Length == 1)
            {
                var member = Members[0];
                results.Add((Name, CalculateNormalizedWeights(member.Elements, 1.0)));
                return results.ToArray();
            }

            if (resolution == 0)
            {
                return null!;
            }

            var allRatios = GetRatios(Members.Length, resolution, 1);
            var validRatios = allRatios.Where(r => CapableComposition(r.ToArray())).ToList();

            if (targetCount < validRatios.Count)
            {
                var random = new Random();
                validRatios = validRatios.OrderBy(_ => random.Next()).Take(targetCount).ToList();
            }

            foreach (var ratio in validRatios) // totalMol は常に 1
            {
                var totalWeights = new Dictionary<string, double>();
                var fileNameBuilder = new StringBuilder();

                for (int i = 0; i < Members.Length; i++)
                {
                    AddWeights(totalWeights, Members[i].Elements, ratio[i]);
                    fileNameBuilder.Append(Members[i].Name);
                    fileNameBuilder.Append(ratio[i].ToString("F3"));
                }

                results.Add((fileNameBuilder.ToString(), NormalizeWeights(totalWeights)));
            }

            return results.ToArray();
        }

        // 260416Codex: 単一メンバー時も複数メンバー時も同じ正規化ロジックを通すようにします。
        private static (string ElementName, double Weight)[] CalculateNormalizedWeights((string Element, double Num)[] elements, double ratio)
        {
            var totalWeights = new Dictionary<string, double>();
            AddWeights(totalWeights, elements, ratio);
            return NormalizeWeights(totalWeights);
        }

        // 260416Codex: 元の加算規則をそのまま共有ヘルパーへ寄せて可読性を上げます。
        private static void AddWeights(Dictionary<string, double> totalWeights, (string Element, double Num)[] elements, double ratio)
        {
            foreach (var (element, num) in elements)
            {
                double weight = num * ratio * AtomStatic.AtomicWeight(element);

                if (totalWeights.ContainsKey(element))
                {
                    totalWeights[element] += weight;
                }
                else
                {
                    totalWeights.Add(element, weight);
                }
            }
        }

        // 260416Codex: 正規化処理を 1 か所に集めて、返却タプルの形を揃えます。
        private static (string ElementName, double Weight)[] NormalizeWeights(Dictionary<string, double> totalWeights)
        {
            double totalWeight = totalWeights.Values.Sum();
            return totalWeights.Select(entry => (entry.Key, entry.Value / totalWeight)).ToArray();
        }

        // 260416Codex: 既存ロジックは維持しつつ、分岐を浅くして読みやすくします。
        public static List<List<double>> GetRatios(int elementNum, double step, double totalMol)
        {
            static double FixZero(double x) => Math.Abs(x) < 1e-10 ? 0.0 : x;

            var result = new List<List<double>>();

            if (elementNum == 1)
            {
                result.Add(new List<double> { 1.0 });
                return result;
            }

            for (double last = 0; last <= (totalMol + 1e-9); last += step)
            {
                if (elementNum == 2)
                {
                    result.Add(new List<double>([FixZero(totalMol - last), FixZero(last)]));
                    continue;
                }

                var resultTmp = GetRatios(elementNum - 1, step, totalMol - last);
                for (int i = 0; i < resultTmp.Count; i++)
                {
                    resultTmp[i].Add(FixZero(last));
                    result.Add(resultTmp[i]);
                }
            }

            return result;
        }

        // 260416Codex: 制約式の評価意図が見えやすいように、失敗条件を直接返します。
        public bool CapableComposition(double[] ratios)
        {
            var roundedRatios = new double[ratios.Length];
            for (int i = 0; i < ratios.Length; i++)
            {
                roundedRatios[i] = Math.Round(ratios[i], 10);
            }

            return !Array.Exists(Constraints, constraint => !Judge(constraint, roundedRatios));
        }

        /// <summary>
        /// Constraintに適しているかを判定。ratiosはMembersの各比率を表す。数式が不正の場合や比率が不正の場合はTrueを返す。
        /// </summary>
        /// <param name="constraint"></param>
        /// <param name="ratios"></param>
        /// <returns></returns>
        // 260416Codex: 配列長の比較を直接使い、例外時は従来どおり true を返します。
        private bool Judge(string constraint, double[] ratios)
        {
            if (ratios.Length != Members.Length)
            {
                return true;
            }

            try
            {
                for (int i = 0; i < Members.Length; i++)
                {
                    constraint = constraint.Replace(Members[i].Name, ratios[i].ToString());
                }

                return new Processor().Solve(constraint).Bool;
            }
            catch
            {
                return true;
            }
        }
    }

    public class Mineral
    {
        // 260416Codex: XML シリアライズで後から設定されるため、nullability のみ明示します。
        public string Name { get; set; } = null!;

        public string FormulaText { get; set; } = null!;

        public (string Element, double Num)[] Elements { get; set; } = null!;

        public Mineral() { } // XML を自動生成するために必要

        public Mineral(string name, string formula)
        {
            Name = name;
            FormulaText = formula;
            Elements = CompositionParser.ParseComposition(formula);
        }

        public override string ToString() => Name;
    }
}
