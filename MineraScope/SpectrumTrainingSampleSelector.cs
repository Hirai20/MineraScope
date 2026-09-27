using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MineraScope
{
    // 260901Codex: Select a reproducible subset that covers the manifest composition space before repeating a composition.
    internal static class SpectrumTrainingSampleSelector
    {
        public static IReadOnlyList<SpectrumTrainingSample> SelectCompositionStratified(
            IReadOnlyList<SpectrumTrainingSample> candidates,
            int targetCount,
            int randomState = 42)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            if (targetCount < 0 || targetCount > candidates.Count)
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            if (targetCount == 0)
                return [];

            string[] componentNames = candidates
                .SelectMany(sample => sample.EndmemberFractions.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var groups = candidates
                .GroupBy(sample => CreateCompositionKey(sample.EndmemberFractions, componentNames), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new CompositionGroup(
                    CreateCompositionVector(group.First().EndmemberFractions, componentNames),
                    group.OrderBy(sample => sample.Source?.SimulationId ?? int.MaxValue)
                        .ThenBy(sample => sample.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToList();

            var random = new Random(randomState);
            foreach (var group in groups)
                Shuffle(group.Samples, random);

            int distinctTarget = Math.Min(targetCount, groups.Count);
            int[] coveredGroups = SelectCompositionCoverage(groups, distinctTarget, random);
            var selected = new List<SpectrumTrainingSample>(targetCount);
            var selectedCounts = new int[groups.Count];
            foreach (int groupIndex in coveredGroups)
            {
                selected.Add(groups[groupIndex].Samples[0]);
                selectedCounts[groupIndex] = 1;
            }

            while (selected.Count < targetCount)
            {
                int[] availableGroups = Enumerable.Range(0, groups.Count)
                    .Where(index => selectedCounts[index] < groups[index].Samples.Count)
                    .ToArray();
                if (availableGroups.Length == 0)
                    throw new InvalidOperationException("The composition-stratified selector exhausted its source samples.");

                Shuffle(availableGroups, random);
                foreach (int groupIndex in availableGroups)
                {
                    selected.Add(groups[groupIndex].Samples[selectedCounts[groupIndex]++]);
                    if (selected.Count == targetCount)
                        break;
                }
            }

            Shuffle(selected, random);
            return selected;
        }

        private static int[] SelectCompositionCoverage(
            IReadOnlyList<CompositionGroup> groups,
            int targetCount,
            Random random)
        {
            if (targetCount == 0)
                return [];

            int firstIndex = random.Next(groups.Count);
            var selected = new List<int>(targetCount) { firstIndex };
            var isSelected = new bool[groups.Count];
            isSelected[firstIndex] = true;
            var minimumDistances = new double[groups.Count];
            for (int i = 0; i < groups.Count; i++)
                minimumDistances[i] = SquaredDistance(groups[i].Composition, groups[firstIndex].Composition);

            int[] tieOrder = Enumerable.Range(0, groups.Count).ToArray();
            Shuffle(tieOrder, random);
            var tiePriority = new int[groups.Count];
            for (int i = 0; i < tieOrder.Length; i++)
                tiePriority[tieOrder[i]] = i;

            while (selected.Count < targetCount)
            {
                int nextIndex = -1;
                double bestDistance = double.NegativeInfinity;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (isSelected[i])
                        continue;

                    bool hasBetterDistance = minimumDistances[i] > bestDistance;
                    bool winsTie = nextIndex >= 0
                        && minimumDistances[i].Equals(bestDistance)
                        && tiePriority[i] < tiePriority[nextIndex];
                    if (nextIndex >= 0 && !hasBetterDistance && !winsTie)
                        continue;

                    nextIndex = i;
                    bestDistance = minimumDistances[i];
                }

                selected.Add(nextIndex);
                isSelected[nextIndex] = true;

                for (int i = 0; i < groups.Count; i++)
                {
                    if (isSelected[i])
                        continue;

                    double distance = SquaredDistance(groups[i].Composition, groups[nextIndex].Composition);
                    minimumDistances[i] = Math.Min(minimumDistances[i], distance);
                }
            }

            return selected.ToArray();
        }

        private static string CreateCompositionKey(
            IReadOnlyDictionary<string, double> fractions,
            IReadOnlyList<string> componentNames) =>
            string.Join("|", componentNames.Select(name =>
                ReadFraction(fractions, name).ToString("G17", CultureInfo.InvariantCulture)));

        private static double[] CreateCompositionVector(
            IReadOnlyDictionary<string, double> fractions,
            IReadOnlyList<string> componentNames) =>
            componentNames.Select(name => ReadFraction(fractions, name)).ToArray();

        private static double ReadFraction(IReadOnlyDictionary<string, double> fractions, string componentName)
        {
            if (fractions.TryGetValue(componentName, out double value))
                return value;

            foreach (var pair in fractions)
                if (string.Equals(pair.Key, componentName, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;

            return 0;
        }

        private static double SquaredDistance(IReadOnlyList<double> left, IReadOnlyList<double> right)
        {
            double sum = 0;
            for (int i = 0; i < left.Count; i++)
            {
                double difference = left[i] - right[i];
                sum += difference * difference;
            }

            return sum;
        }

        private static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private sealed record CompositionGroup(
            double[] Composition,
            List<SpectrumTrainingSample> Samples);
    }
}
