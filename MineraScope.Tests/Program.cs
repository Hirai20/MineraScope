namespace MineraScope.Tests
{
    // 260907Codex: Run every bounded suite and return failure to the shell; no TensorFlow, DTSA-II, or UI process is started.
    internal static class Program
    {
        // 260907Codex: Optionally compare equal-weight plans against an explicitly supplied pre-change assembly, without invoking its application entry point.
        public static int Main(string[] args)
        {
            // 261021Codex: 利用者のモデルを使わず、定量省略と既存出力の保持を実学習で確認する。
            if (args.Length == 1 && args[0] == "--training-sample-smoke")
            {
                RegressionPromotionTests.RunNativeSmoke();
                Console.WriteLine("PASS: Regression omission and safe model promotion");
                return 0;
            }
            // 261021Codex: 通常モデルの合成データだけで分類＋OCNの作成・保存・推論を検証する。
            if (args.Length == 1 && args[0] == "--product-ocn-smoke")
            {
                ProductOcnTests.RunNativeSmoke();
                return 0;
            }
            // 261021Codex: プロセス間共有の合成テスト専用入口。
            if (args.Length == 3 && args[0] == "--concurrency-child")
                return ConcurrencyTests.RunChild(args[1], args[2]);
            // 261016Codex: コントロールだけを構築し、フォームは表示せず選択維持を確認する。
            if (args.Length == 1 && args[0] == "--model-selection-smoke")
            {
                ModelSelectionTests.Run();
                Console.WriteLine("PASS: Model selection survives unavailable preferred names");
                return 0;
            }
            // 261016Codex: 通常分類の学習・採用重みの保存・再読込を合成入力で検証する。
            if (args.Length == 1 && args[0] == "--classification-persistence-smoke")
            {
                ClassificationPersistenceTests.RunNativeSmoke();
                Console.WriteLine("PASS: Trained classifier persistence and reload");
                return 0;
            }
            // 261016Codex: 直接学習の件数不足拒否を、専用の合成入力で確認する。
            if (args.Length == 1 && args[0] == "--classification-count-smoke")
            {
                TrainingSampleRequirementsTests.RunNativeSmoke();
                Console.WriteLine("PASS: Classification count refusal preserves existing output");
                return 0;
            }
            // 261016Codex: 無信号の扱いは、合成ファイルだけを使う任意のネイティブ検証で確認する。
            if (args.Length == 1 && args[0] == "--spectrum-signal-smoke")
            {
                SpectrumSignalTests.RunNativeSmoke();
                Console.WriteLine("PASS: Empty spectrum refusal and normal input compatibility");
                return 0;
            }
            // 260930Codex: Opt-in verification uses synthetic saved models to exercise same-path overwrites.
            if (args.Length == 1 && args[0] == "--model-refresh-smoke")
            {
                ModelRefreshTests.RunNativeSmoke();
                Console.WriteLine("PASS: Overwritten model refresh and unchanged cache reuse");
                return 0;
            }
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
                // 261021Codex: 研究用実験を含めず、保存済みheadの整合性と通常OCNの契約を検証する。
                ("Saved OCN artifact integrity", SavedOcnArtifactTests.Run),
                ("Product OCN contracts", ProductOcnTests.Run),
                // 261021Codex: 別プロセス間の生成・読み取り共有とJSON公開を検証する。
                ("Cross-process spectrum pool concurrency", ConcurrencyTests.Run),
                // 260930Codex: Filesystem failure checks verify promotion and backup cleanup without TensorFlow.
                ("Model promotion and backup cleanup", ModelPromotionTests.Run),
                // 260930Codex: Catalog refresh hides GUID-qualified work folders while retaining ordinary model names.
                ("Model catalog staging folders", ModelCatalogTests.Run),
                // 260930Codex: Ordinary defaults and legacy settings are checked without native TensorFlow.
                ("Classification learning-rate settings", ClassificationLearningRateTests.Run),
                // 261016Codex: 前処理記録の互換性と破損拒否を通常テストへ追加する。
                ("Model preprocessing metadata integrity", SpectrumPreprocessingTests.Run),
                // 261016Codex: 分類の開始条件はネイティブ学習なしでも検証する。
                ("Classification training prerequisites", TrainingSampleRequirementsTests.Run),
                // 261021Claude: 保存先の深さはネイティブ学習なしで検査する。
                ("Model save path length", GeneratedPathPolicyTests.Run),
                // 261016Codex: モデルの開始前検証をネイティブ学習なしで確認する。
                ("Batch classifier preflight", BatchPredictionTests.Run),
                // 260930Codex: Filesystem checks do not load the native runtime.
                ("Saved model content identity", ModelRefreshTests.Run),
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
