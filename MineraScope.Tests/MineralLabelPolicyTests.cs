using Tensorflow;
using Tensorflow.NumPy;
using static Tensorflow.Binding;

namespace MineraScope.Tests
{
    // 260930Codex: Check the four composition intersections and the actual graph loss used by ordinary training.
    internal static class MineralLabelPolicyTests
    {
        private static Dictionary<string, double> Composition(double ab, double an, double or) =>
            new() { ["Albite"] = ab, ["Anorthite"] = an, ["Orthoclase"] = or };

        public static void Run()
        {
            foreach (var (ab, an, or) in new[] { (1d, 0d, 0d), (.9, .1, 0), (.9, 0, .1), (.8, .1, .1) })
                foreach (string source in new[] { "Plagioclase", "AlkaliFeldspar" })
                {
                    var composition = Composition(ab, an, or);
                    TestAssert.SequenceEqual(new[] { "Plagioclase", "AlkaliFeldspar" }, MineralLabelPolicy.GetAcceptableLabels(source, composition), "Only the four intersections admit both labels.");
                    TestAssert.True(MineralLabelPolicy.IsAcceptablePrediction(source, "Plagioclase", composition), "Plagioclase is acceptable.");
                    TestAssert.True(MineralLabelPolicy.IsAcceptablePrediction(source, "AlkaliFeldspar", composition), "AlkaliFeldspar is acceptable.");
                }
            TestAssert.SequenceEqual(new[] { "Plagioclase" }, MineralLabelPolicy.GetAcceptableLabels("Plagioclase", Composition(.8, .2, 0)), "Outside overlap remains single-label.");
            TestAssert.SequenceEqual(new[] { "Plagioclase" }, MineralLabelPolicy.GetAcceptableLabels("Plagioclase", Composition(.9, .05, .05)), "Off-grid composition remains single-label.");
            TestAssert.SequenceEqual(new[] { "Quartz" }, MineralLabelPolicy.GetAcceptableLabels("Quartz", Composition(.8, .1, .1)), "Other minerals remain single-label.");
            TestAssert.SequenceEqual(new[] { "Plagioclase" }, MineralLabelPolicy.GetAcceptableLabels("Plagioclase", null), "Unknown composition remains single-label.");
            TestAssert.Equal(2, MineralLabelPolicy.GetAcceptableLabels("Plagioclase", Composition(80, 10, 10)).Length, "Mole-percent metadata is accepted.");
            TestAssert.Equal(MineralLabelPolicy.LegacyVersion, MineralLabelPolicy.ReadTrainingVersion(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), "Old models retain legacy identity.");
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MineralDatabaseOriginal.xml"));
            foreach (var (name, constraint) in new[] { ("Plagioclase", "Orthoclase <= 0.1"), ("AlkaliFeldspar", "Anorthite <= 0.1") })
            {
                var solution = document.Descendants("SolidSolution").Single(element => element.Element("Name")?.Value == name);
                TestAssert.True(solution.Element("Constraints")!.Elements().Any(element => element.Value == constraint), "The shipped database matches the overlap bounds.");
            }
        }

        // 260930Codex: The singleton graph loss must equal sparse CE; two accepted logits reduce loss without forcing equal probabilities.
        public static void RunNativeSmoke()
        {
            TensorFlowExecutor.Run(() =>
            {
                var graph = tf.Graph();
                graph.as_default();
                try
                {
                    float[,] values = { { 4, 1, -2 }, { 1, 4, -2 }, { -4, -4, 4 } };
                    var logits = tf.constant(values);
                    var source = tf.constant(new[] { 0, 0, 0 });
                    var alternate = tf.constant(new[] { 1, 1, 1 });
                    var ordinary = tf.nn.sparse_softmax_cross_entropy_with_logits(source, logits);
                    var singleton = PartialLabelLoss.PerSample(logits, source, source, 3);
                    var dual = PartialLabelLoss.PerSample(logits, source, alternate, 3);
                    var ordinaryGradient = tf.gradients(tf.reduce_sum(ordinary), new[] { logits })[0];
                    var singletonGradient = tf.gradients(tf.reduce_sum(singleton), new[] { logits })[0];
                    var dualGradient = tf.gradients(tf.reduce_sum(dual), new[] { logits })[0];
                    using var session = tf.Session(graph);
                    using var ce = session.run(ordinary);
                    using var single = session.run(singleton);
                    using var partial = session.run(dual);
                    using var ceGradient = session.run(ordinaryGradient);
                    using var singleGradient = session.run(singletonGradient);
                    using var partialGradient = session.run(dualGradient);
                    TestAssert.SequenceEqual(ce.ToArray<float>(), single.ToArray<float>(), "Singleton loss matches sparse CE.");
                    TestAssert.SequenceEqual(ceGradient.ToArray<float>(), singleGradient.ToArray<float>(), "Singleton gradient matches sparse CE.");
                    float[] losses = partial.ToArray<float>();
                    TestAssert.True(losses.All(float.IsFinite) && losses[0] < .1f && losses[1] < .1f && losses[2] > 7f, "Dual loss uses combined probability mass.");
                    TestAssert.True(partialGradient.ToArray<float>().All(float.IsFinite), "Dual gradient remains finite.");
                }
                finally { graph.Exit(); }
                return true;
            });
        }
    }
}