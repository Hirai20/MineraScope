namespace MineraScope.Tests
{
    // 260907Codex: Run every bounded suite and return failure to the shell; no TensorFlow, DTSA-II, or UI process is started.
    internal static class Program
    {
        // 260907Codex: Optionally compare equal-weight plans against an explicitly supplied pre-change assembly, without invoking its application entry point.
        public static int Main(string[] args)
        {
            // 260930Codex: Opt-in native checks exercise the classification optimizer from identical weights.
            if (args.Length == 1 && args[0] == "--classification-learning-rate-smoke")
            {
                ClassificationLearningRateTests.RunNativeSmoke();
                Console.WriteLine("PASS: Classification learning-rate graph updates");
                return 0;
            }
            // 260930Codex: Run the TensorFlow loss and gradient check separately from ordinary tests.
            if (args.Length == 1 && args[0] == "--feldspar-loss-smoke")
            {
                MineralLabelPolicyTests.RunNativeSmoke();
                Console.WriteLine("PASS: Feldspar partial-label loss and gradient");
                return 0;
            }
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--compare-baseline"))
            {
                Console.Error.WriteLine("Usage: MineraScope.Tests [--compare-baseline <original MineraScope.dll>]");
                return 2;
            }

            List<(string Name, Action Run)> suites =
            [
                // 260930Codex: Ordinary defaults and legacy settings are checked without native TensorFlow.
                ("Classification learning-rate settings", ClassificationLearningRateTests.Run),
                // 260930Codex: Check four feldspar compositions without loading TensorFlow.
                ("Feldspar overlap label policy", MineralLabelPolicyTests.Run),
                ("Composition and time allocation", PlannerTests.Run),
                ("Pool preview, reuse, cancellation, and recovery", PoolTests.Run),
                ("Training completeness and provenance", TrainingPoolTests.Run),
                // 260907Codex: Assert preparation equivalence, bounded JSON reads, integrity, and cancellation independently of TensorFlow.
                ("Training preparation performance and compatibility", PreparationTests.Run)
            ];
            if (args.Length == 2)
                suites.Add(("Historical equal-plan compatibility", () => BaselineCompatibilityTests.Run(args[1])));
            int failed = 0;
            foreach (var suite in suites)
                try
                {
                    suite.Run();
                    Console.WriteLine($"PASS: {suite.Name}");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL: {suite.Name}\n{exception}");
                }

            Console.WriteLine($"{suites.Count - failed}/{suites.Count} suites passed.");
            return failed == 0 ? 0 : 1;
        }
    }

    // 260907Codex: Keep assertion failures actionable without introducing a third-party test runner.
    internal static class TestAssert
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException($"{message}\nExpected: {string.Join(", ", expected)}\nActual: {string.Join(", ", actual)}");
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}.");
        }
    }
}
