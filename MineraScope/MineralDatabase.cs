using Crystallography;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using xFunc.Maths;

namespace MineraScope
{
    public class SolidSolution
    {
        // 260620Codex: Keep percentage-grid fractions and DTSA-II weights free of floating-point residue.
        private const double FractionSnapTolerance = 1e-12;
        private const double WeightSnapTolerance = 1e-12;

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

        // 260528Claude: 6 端成分 1% で 96M 件の全列挙を避けるため、yield ベースの遅延列挙を提供します。Take で打ち切れば内部の再帰も途中で停止します。
        public IEnumerable<double[]> EnumerateCandidateFractionsLazy(double resolution)
        {
            if (Members.Length == 1)
            {
                yield return [1.0];
                yield break;
            }

            if (resolution <= 0)
                yield break;

            foreach (var ratio in EnumerateRatios(Members.Length, resolution, 1.0))
            {
                if (CapableComposition(ratio))
                    yield return ratio;
            }
        }

        // 260528Claude: bars-and-stars 法で合計 1 になる端成分比率を 1 セット乱択します。constraint・重複チェックは呼び出し側で実施します。
        public double[] SampleRandomFraction(double resolution, Random random)
        {
            if (Members.Length == 1)
                return [1.0];

            if (resolution <= 0)
                throw new ArgumentOutOfRangeException(nameof(resolution), "resolution は 0 より大きい必要があります。");

            int units = (int)Math.Round(1.0 / resolution);
            int slots = units + Members.Length - 1;
            int barCount = Members.Length - 1;

            var positions = new int[barCount];
            var taken = new HashSet<int>();
            for (int i = 0; i < barCount; i++)
            {
                int p;
                do { p = random.Next(slots); } while (!taken.Add(p));
                positions[i] = p;
            }
            Array.Sort(positions);

            var fractions = new double[Members.Length];
            int prev = -1;
            for (int i = 0; i < barCount; i++)
            {
                fractions[i] = (positions[i] - prev - 1) * resolution;
                prev = positions[i];
            }
            fractions[Members.Length - 1] = (slots - prev - 1) * resolution;

            // 260528Claude: 浮動小数誤差で合計が 1 から微妙にずれることがあるので、最後の成分で吸収します。
            double sum = 0;
            for (int i = 0; i < Members.Length - 1; i++)
                sum += fractions[i];
            fractions[Members.Length - 1] = 1.0 - sum;

            return NormalizeEndmemberFractions(fractions, resolution);
        }

        // 260528Claude: 端成分定義順に並べた比率を文字列キーへ正規化します。Math.Round で浮動小数誤差を吸収し、manifest の EndmemberFractions と直接比較できる形にします。
        public string ComposeFractionKey(double[] fractions) =>
            ComposeFractionKey(i => i < fractions.Length ? fractions[i] : 0.0);

        // 260528Claude: manifest 側の Dictionary<string, double> 形式から同じキーを作るオーバーロードです。
        public string ComposeFractionKey(IReadOnlyDictionary<string, double> fractions) =>
            ComposeFractionKey(i => fractions.TryGetValue(Members[i].Name, out double value) ? value : 0.0);

        // 260604Claude: 端成分順の値取得だけを差し替え、キー整形 (Math.Round・F6・'|' 連結) を 1 箇所に集約します。
        private string ComposeFractionKey(Func<int, double> valueAt)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Members.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(Math.Round(valueAt(i), 6).ToString("F6", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // 260507Codex: manifest の endmemberFractions へ保存する端成分比率を鉱物定義順で作ります。
        // 260620Codex: Persist endmember ratios on the integer-percent grid used by the composition UI.
        public Dictionary<string, double> CreateEndmemberFractionMap(double[] fractions, double resolution)
        {
            var normalized = NormalizeEndmemberFractions(fractions, resolution);
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Members.Length && i < normalized.Length; i++)
                result[Members[i].Name] = normalized[i];

            return result;
        }

        // 260620Codex: Snap fractions to integer percent units before they become manifest labels or DTSA-II inputs.
        public double[] NormalizeEndmemberFractions(double[] fractions, double resolution)
        {
            int percentStep = PercentStepFromResolution(resolution);
            var percents = new int[Members.Length];

            for (int i = 0; i < Members.Length && i < fractions.Length; i++)
                percents[i] = SnapFractionPercent(fractions[i], percentStep);

            NormalizePercentSum(percents);
            return percents.Select(static percent => percent / 100.0).ToArray();
        }

        // 260507Codex: DTSA-II に渡す元素重量比を、manifest 予約済みの端成分比率から再構築します。
        public (string ElementName, double Weight)[] CalculateCompositionWeights(double[] fractions)
        {
            var totalWeights = new Dictionary<string, double>();
            for (int i = 0; i < Members.Length && i < fractions.Length; i++)
                AddWeights(totalWeights, Members[i].Elements, fractions[i]);

            return NormalizeWeights(totalWeights);
        }

        // 260416Codex: 元の加算規則をそのまま共有ヘルパーへ寄せて可読性を上げます。
        private static void AddWeights(Dictionary<string, double> totalWeights, (string Element, double Num)[] elements, double ratio)
        {
            foreach (var (element, num) in elements)
            {
                // 260620Codex: Match the dictionary accumulation style used elsewhere in the project.
                double weight = num * ratio * AtomStatic.AtomicWeight(element);
                totalWeights[element] = totalWeights.GetValueOrDefault(element) + weight;
            }
        }

        // 260416Codex: 正規化処理を 1 か所に集めて、返却タプルの形を揃えます。
        private static (string ElementName, double Weight)[] NormalizeWeights(Dictionary<string, double> totalWeights)
        {
            var cleanedWeights = totalWeights
                .Select(entry => (entry.Key, Weight: CleanWeight(entry.Value)))
                .ToArray();
            double totalWeight = cleanedWeights.Sum(entry => entry.Weight);
            if (totalWeight <= 0)
                throw new InvalidOperationException("Composition weight total must be positive.");

            return cleanedWeights
                .Select(entry => (entry.Key, CleanWeight(entry.Weight / totalWeight)))
                .ToArray();
        }

        // 260620Codex: Convert fraction steps to integer mol-percent units so JSON can store clean labels.
        private static int PercentStepFromResolution(double resolution)
        {
            if (resolution <= 0)
                return 1;

            int step = (int)Math.Round(resolution * 100.0, MidpointRounding.AwayFromZero);
            return Math.Max(1, step);
        }

        // 260620Codex: Clamp only after snapping to the requested percent grid to prevent negative DTSA-II inputs.
        private static int SnapFractionPercent(double value, int percentStep)
        {
            if (Math.Abs(value) < FractionSnapTolerance)
                return 0;

            int percent = (int)Math.Round(value * 100.0 / percentStep, MidpointRounding.AwayFromZero) * percentStep;
            return Math.Clamp(percent, 0, 100);
        }

        // 260620Codex: Preserve a total of 100 mol% after independent percent-grid snapping.
        private static void NormalizePercentSum(int[] percents)
        {
            int sum = percents.Sum();
            if (sum == 100 || sum <= 0)
                return;

            int targetIndex = 0;
            for (int i = 1; i < percents.Length; i++)
                if (percents[i] > percents[targetIndex])
                    targetIndex = i;

            int adjusted = percents[targetIndex] + 100 - sum;
            if (adjusted >= 0 && adjusted <= 100)
                percents[targetIndex] = adjusted;
        }

        // 260620Codex: Treat tiny negative residues as zero while surfacing real invalid weights.
        private static double CleanWeight(double value)
        {
            if (Math.Abs(value) < WeightSnapTolerance)
                return 0;

            if (value < 0)
                throw new InvalidOperationException("Composition weight must not be negative.");

            return value;
        }

        // 260528Claude: 全候補を 1 件ずつ yield する遅延列挙です。Take で打ち切るとメモリ・CPU とも O(取得数) で停止し、巨大候補空間でも UI を固めません。
        private static IEnumerable<double[]> EnumerateRatios(int elementNum, double step, double totalMol)
        {
            static double FixZero(double x) => Math.Abs(x) < 1e-10 ? 0.0 : x;

            if (elementNum == 1)
            {
                yield return [FixZero(totalMol)];
                yield break;
            }

            for (double last = 0; last <= (totalMol + 1e-9); last += step)
            {
                if (elementNum == 2)
                {
                    yield return [FixZero(totalMol - last), FixZero(last)];
                    continue;
                }

                foreach (var sub in EnumerateRatios(elementNum - 1, step, totalMol - last))
                {
                    var combined = new double[sub.Length + 1];
                    Array.Copy(sub, combined, sub.Length);
                    combined[sub.Length] = FixZero(last);
                    yield return combined;
                }
            }
        }

        // 260416Codex: 制約式の評価意図が見えやすいように、失敗条件を直接返します。
        public bool CapableComposition(double[] ratios)
        {
            var roundedRatios = new double[ratios.Length];
            for (int i = 0; i < ratios.Length; i++)
                roundedRatios[i] = Math.Round(ratios[i], 10);

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
        // 260416Codex: XML シリアライズで後から設定されるため、nullability のみ明示します。
        public string Name { get; set; } = null!;

        public string FormulaText { get; set; } = null!;

        // 260617Codex: Preserve the hand-maintained seed XML density element during local direct Original saves.
        [XmlElement("density")]
        public double Density { get; set; }

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
