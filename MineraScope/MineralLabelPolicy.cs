using System.Text.Json;

namespace MineraScope
{
    // 260917Codex: Composition evidence, never the folder name alone, admits the four 10 mol% feldspar intersections.
    internal static class MineralLabelPolicy
    {
        public const string Version = "feldspar-overlap-v1";
        public const string LegacyVersion = "legacy-single-label";
        public const string MetadataFileName = "labelPolicy.json";
        public const double Tolerance = 1e-6;

        public static bool TryNormalize(IReadOnlyDictionary<string, double>? fractions, out double ab, out double an, out double or)
        {
            ab = an = or = 0;
            if (fractions is null || fractions.Count == 0)
                return false;
            foreach (var pair in fractions)
            {
                if (!double.IsFinite(pair.Value) || pair.Value < -Tolerance)
                    return false;
                switch (pair.Key.ToLowerInvariant())
                {
                    case "albite": case "ab": ab += pair.Value; break;
                    case "anorthite": case "an": an += pair.Value; break;
                    case "orthoclase": case "or": or += pair.Value; break;
                    default: return false;
                }
            }
            double total = ab + an + or;
            if (Math.Abs(total - 1) > Tolerance && Math.Abs(total - 100) > Tolerance)
                return false;
            ab /= total; an /= total; or /= total;
            return true;
        }

        public static string[] GetAcceptableLabels(string source, IReadOnlyDictionary<string, double>? fractions)
        {
            if (source is not ("Plagioclase" or "AlkaliFeldspar")
                || !TryNormalize(fractions, out double ab, out double an, out double or)
                || an > .1 + Tolerance || or > .1 + Tolerance
                || new[] { ab, an, or }.Any(value => Math.Abs(value * 10 - Math.Round(value * 10)) > Tolerance * 10))
                return [source];
            return ["Plagioclase", "AlkaliFeldspar"];
        }

        public static bool IsAcceptablePrediction(string source, string predicted, IReadOnlyDictionary<string, double>? fractions) =>
            GetAcceptableLabels(source, fractions).Contains(predicted, StringComparer.Ordinal);

        // 260917Codex: An absent alternative prototype/class falls back to the source; pseudo-Unknown sampling is unchanged.
        public static int AlternativeIndex(string source, IReadOnlyDictionary<string, double>? fractions, IReadOnlyList<string> classes)
        {
            string[] acceptable = GetAcceptableLabels(source, fractions);
            int original = Enumerable.Range(0, classes.Count).Single(index => classes[index] == source);
            return Enumerable.Range(0, classes.Count).FirstOrDefault(index => index != original && acceptable.Contains(classes[index]), original);
        }

        // 260917Codex: Resolve the sidecar path once and make the legacy fallback explicit.
        public static string ReadTrainingVersion(string folder)
        {
            string path = Path.Combine(folder, MetadataFileName);
            if (!File.Exists(path))
                return LegacyVersion;
            return JsonSerializer.Deserialize<LabelPolicyMetadata>(File.ReadAllText(path))?.LabelPolicyVersion ?? LegacyVersion;
        }

        public static void WriteTrainingMetadata(string folder) => File.WriteAllText(Path.Combine(folder, MetadataFileName),
            JsonSerializer.Serialize(new LabelPolicyMetadata(Version)));
    }

    // 260917Codex: Missing metadata always means legacy training, independent of the evaluation policy.
    internal sealed record LabelPolicyMetadata(string LabelPolicyVersion);
}
