using Crystallography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MineraScope;
using xFunc.Maths;
using xFunc.Maths.Expressions;


namespace MineraScope
{

    public class SolidSolution
    {
        public string Name { get; set; }
        public string Formula { get; set; }
        public Mineral[] Members { get; set; }
        public string[] Constraints { get; set; }

        public SolidSolution() { }
        public SolidSolution(string name,string formula, Mineral[] members, string[] constraints)
        {
            Name = name;
            Formula = formula;
            Members = members;
            Constraints = constraints;
        }
        public override string ToString() => Name;

        public (string FileName, (string ElementName, double Weight)[] Compositions)[] Divide(double resolution, int targetCount)
        {
            var results = new List<(string FileName, (string ElementName, double Weight)[])>();

            if (Members.Length == 1)
            {
                var member = Members[0];

                var dic = new Dictionary<string, double>();
                double totalWeight = 0;

                foreach (var (element, num) in member.Elements)
                {

                    double weight = num * AtomStatic.AtomicWeight(element);

                    if (dic.ContainsKey(element)) dic[element] += weight;
                    else dic.Add(element, weight);

                    totalWeight += weight;
                }
                // 重さを正規化 
                var normalizedWeight = dic.Select(e => (e.Key, e.Value / totalWeight)).ToArray();

                // ファイル名は鉱物名そのもの (例: "Quartz")
                results.Add((Name, normalizedWeight));
                return results.ToArray();

            }
            else
            {
                if (resolution == 0)
                    return null;
                else
                {
                    var allRatios = GetRatios(Members.Length, resolution,1);
                    var validRatios = allRatios.Where(r=>CapableComposition(r.ToArray())).ToList();
                    if (targetCount < validRatios.Count )
                    {
                        var rnd = new Random();
                        validRatios = validRatios.OrderBy(x => rnd.Next()).Take(targetCount).ToList();
                    }
                    foreach (var ratio in validRatios) //totalMolは常に１
                    {
                            string filename = "";
                            //var comp = new List<(string ElementName, double Weight)>();
                            double totalWeight = 0;
                            //compを計算
                            var dic = new Dictionary<string, double>();
                            var sb = new StringBuilder();

                            for (int i = 0; i < Members.Length; i++)
                            {
                                foreach (var (element, num) in Members[i].Elements)
                                {
                                    double weight = num * ratio[i] * AtomStatic.AtomicWeight(element);
                                    if (dic.ContainsKey(element))
                                        dic[element] += weight;
                                    else
                                        dic.Add(element, weight);
                                    totalWeight += weight;
                                }
                                sb.Append(Members[i].Name);
                                sb.Append(ratio[i].ToString("F3"));
                            }
                            filename = sb.ToString();
                            var normalized = dic.Select(e => (e.Key, e.Value / totalWeight)).ToArray();
                            results.Add((filename, normalized));
                        }
                    }
                }
                return results.ToArray();
            }
        
        public static List<List<double>> GetRatios(int elementNum, double step, double totalMol)
        {
            double FixZero(double x) => Math.Abs(x) < 1e-10 ? 0.0 : x;
            var result = new List<List<double>>();
            //if (total < step * 0.5)
            //    return [[.. Enumerable.Repeat(0.0, elementNum)]];

            if (elementNum == 1)
            {
                result.Add(new List<double> { 1.0 });
                return result;
            }
            for (double last = 0; last <= totalMol + 1e-9; last += step)
            {
                
                if (elementNum == 2)
                    result.Add(new List<double>([FixZero(totalMol - last), FixZero(last)]));

                else
                {
                    var resultTmp = GetRatios(elementNum - 1, step,totalMol-last );
                    for (int i = 0; i < resultTmp.Count; i++)
                    {
                        resultTmp[i].Add(FixZero(last));
                        result.Add(resultTmp[i]);
                    }
                }
            }
            return result;
        }
        public bool CapableComposition(double[] ratios)
        {
            var roundedRatios = new double[ratios.Length];
            for (int i = 0; i < ratios.Length; i++)
            {
                roundedRatios[i] = Math.Round(ratios[i], 10);
            }
            foreach (var constraint in Constraints)
            {
                if (Judge(constraint, roundedRatios) == false)
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Constraintに適しているかを判定。ratiosはMembersの各比率を表す。数式が不正の場合や比率が不正の場合はTrueを返す。
        /// </summary>
        /// <param name="constraint"></param>
        /// <param name="ratios"></param>
        /// <returns></returns>
        private bool Judge(string constraint, double[] ratios)
        {
            if (ratios.Count() != Members.Length)
                return true;

            try
            {
                for (int i = 0; i < Members.Length; i++)
                    constraint = constraint.Replace(Members[i].Name, ratios[i].ToString());

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
        public string Name { get; set; }

        public string FormulaText { get; set; }

        public (string Element, double Num)[] Elements { get; set; }
        public Mineral() { } //XMLを自動生成するために必要
        public Mineral(
            string name,
            string formula
        )
        {
            Name = name;
            FormulaText = formula;
            Elements = CompositionParser.ParseComposition(formula);
        }
        public override string ToString() => Name;
    }
}



