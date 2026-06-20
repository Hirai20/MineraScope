namespace MineraScope
{
    // 260620Claude: ドロップされた複数スペクトルを選択モデルで一括分類し、構造化結果(SpectrumPredictionBatch)を返す。
    //               画面表示・CSV・report はこの結果から作るため、ここではログ整形をせず素の値だけ詰める。
    internal sealed class SpectrumBatchPredictionWorkflow
    {
        // 260620Claude: 進捗ログを間引く間隔。数百件でもログが溢れないよう一定間隔だけ出す。
        private const int ProgressInterval = 25;

        private readonly string _assemblyPath;
        private readonly Action<string> _log;

        public SpectrumBatchPredictionWorkflow(string assemblyPath, Action<string> log)
        {
            _assemblyPath = assemblyPath;
            _log = log;
        }

        // 260620Claude: 推論は分類/回帰サービス内で専用スレッドへ集約されるため、収集ループ全体を Task.Run へ逃がして UI を塞がない。
        public Task<SpectrumPredictionBatch> RunAsync(
            string modelPath,
            string modelName,
            IReadOnlyList<string> files,
            CancellationToken cancellationToken = default)
            => Task.Run(() => Run(modelPath, modelName, files, cancellationToken), cancellationToken);

        private SpectrumPredictionBatch Run(
            string modelPath,
            string modelName,
            IReadOnlyList<string> files,
            CancellationToken cancellationToken)
        {
            // 260620Claude: サービスは1インスタンスを使い回し、モデルをパス単位で1回だけロードさせる。
            var classificationService = new MineralClassificationPredictionService();
            var regressionService = new MineralRegressionPredictionService();
            var items = new List<SpectrumPredictionItem>(files.Count);

            _log($"分類を開始します（{files.Count} 件）。");

            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(PredictOne(classificationService, regressionService, modelPath, files[i]));

                if ((i + 1) % ProgressInterval == 0 && i + 1 != files.Count)
                    _log($"分類中 {i + 1}/{files.Count}");
            }

            int failed = items.Count(item => !item.IsSuccess);
            _log(failed == 0
                ? $"分類が完了しました（{files.Count} 件）。"
                : $"分類が完了しました（{files.Count} 件中 {failed} 件失敗）。");

            return new SpectrumPredictionBatch(modelName, items);
        }

        private SpectrumPredictionItem PredictOne(
            MineralClassificationPredictionService classificationService,
            MineralRegressionPredictionService regressionService,
            string modelPath,
            string filePath)
        {
            float[]? spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath);
            if (spectrum is null)
                return Failed(filePath, "スペクトルを読み込めませんでした。");

            MineralClassificationPredictionResult classification;
            try
            {
                classification = classificationService.Predict(modelPath, spectrum);
            }
            catch (Exception ex)
            {
                return Failed(filePath, $"分類に失敗しました: {ex.Message}");
            }

            var (regressionModelName, endmembers, formula) =
                PredictRegression(regressionService, modelPath, classification.PredictedMineral, spectrum);

            return new SpectrumPredictionItem(filePath, classification, regressionModelName, endmembers, formula, null);
        }

        // 260620Claude: 予測鉱物に対応する *_Regression があれば端成分比率と組成式を求める。無ければ空のまま返す（分類は成功扱い）。
        private (string? ModelName, IReadOnlyList<MineralComponentRatio> Endmembers, string Formula) PredictRegression(
            MineralRegressionPredictionService regressionService,
            string modelPath,
            string predictedMineral,
            float[] spectrum)
        {
            // 260620Claude: 回帰モデルの解決は単発判定 (DeepLearning.RunPrediction) と同じ命名規約に揃える。
            string[] candidates = Directory.GetDirectories(modelPath, $"{predictedMineral}*_Regression");
            if (candidates.Length == 0)
                return (null, [], string.Empty);

            if (candidates.Length > 1)
                _log($"回帰モデルが複数見つかりました。先頭を使用します: {Path.GetFileName(candidates[0])}");

            string regressionPath = candidates[0];
            try
            {
                var regression = regressionService.Predict(regressionPath, spectrum);
                var ratios = regression.Components.ToDictionary(component => component.ComponentName, component => component.Ratio);
                string formula = MineralFormulaGenerator.Generate(ratios, predictedMineral, _assemblyPath);
                return (Path.GetFileName(regressionPath), regression.Components, formula);
            }
            catch (Exception ex)
            {
                _log($"回帰予測をスキップしました（{Path.GetFileName(regressionPath)}）: {ex.Message}");
                return (Path.GetFileName(regressionPath), [], string.Empty);
            }
        }

        private static SpectrumPredictionItem Failed(string filePath, string error) =>
            new(filePath, null, null, [], string.Empty, error);
    }
}
