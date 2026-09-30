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
            // 260727Claude: 回帰モデルフォルダの解決結果を run 内でキャッシュする。従来は 1 スペクトルごとに
            //   Directory.GetDirectories が走り、444 点なら 444 回の走査になっていた。modelPath は run 中不変。
            var regressionFolderCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            // 260623Claude: 親フォルダを1つ深くして *_Regression を直接選んだ場合は、分類を通さず回帰モデルだけで予測する（実測ずれ評価用）。
            string? regressionMineral = IsRegressionModelFolder(modelPath) ? ResolveRegressionMineralName(modelPath) : null;
            bool regressionOnly = regressionMineral is not null;
            string verb = regressionOnly ? "回帰" : "分類";

            // 260626Claude: モデルの前処理を読み、学習時と同じ低エネルギーマスクを予測へ自動適用する。
            //   回帰のみは選択フォルダ自体、分類経路は分類サブフォルダの preprocessing.json を正とする(同一 run なら回帰側と一致)。
            string preprocessingFolder = regressionOnly ? modelPath : ModelArtifactPaths.GetClassificationFolder(modelPath);
            var preprocessing = SpectrumPreprocessing.LoadFromModelFolder(preprocessingFolder);
            _log($"前処理: {preprocessing.Describe()}");

            _log(regressionOnly
                ? $"回帰のみで予測を開始します（{files.Count} 件、{regressionMineral}）。"
                : $"分類を開始します（{files.Count} 件）。");

            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(regressionOnly
                    ? PredictRegressionOnly(regressionService, modelPath, regressionMineral!, files[i], preprocessing)
                    : PredictOne(classificationService, regressionService, modelPath, files[i], preprocessing, regressionFolderCache));

                if ((i + 1) % ProgressInterval == 0 && i + 1 != files.Count)
                    _log($"{verb}中 {i + 1}/{files.Count}");
            }

            int failed = items.Count(item => !item.IsSuccess);
            _log(failed == 0
                ? $"{verb}が完了しました（{files.Count} 件）。"
                : $"{verb}が完了しました（{files.Count} 件中 {failed} 件失敗）。");

            return new SpectrumPredictionBatch(modelName, items);
        }

        // 260623Claude: 選択フォルダ自体が回帰モデルか判定する。modelType.txt を正とし、無い旧モデルは componentIndex.json + 分類サブフォルダ無しで代替判定する。
        private static bool IsRegressionModelFolder(string modelPath)
        {
            string modelTypePath = Path.Combine(modelPath, ModelArtifactPaths.ModelTypeFileName);
            if (File.Exists(modelTypePath)
                && File.ReadAllText(modelTypePath).Trim().Equals("regression", StringComparison.OrdinalIgnoreCase))
                return true;

            return File.Exists(Path.Combine(modelPath, ModelArtifactPaths.ComponentIndexFileName))
                && !Directory.Exists(ModelArtifactPaths.GetClassificationFolder(modelPath));
        }

        // 260623Claude: 化学組成式生成に渡す鉱物名を mineralName.txt から取り、無ければフォルダ名から _Regression を外して使う。
        private static string ResolveRegressionMineralName(string modelPath)
        {
            string namePath = Path.Combine(modelPath, ModelArtifactPaths.MineralNameFileName);
            if (File.Exists(namePath))
            {
                string name = File.ReadAllText(namePath).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            string folder = Path.GetFileName(modelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            const string suffix = ModelArtifactPaths.RegressionFolderSuffix;
            return folder.EndsWith(suffix, StringComparison.Ordinal) ? folder[..^suffix.Length] : folder;
        }

        // 260623Claude: 分類をスキップし、選択した回帰モデル1つだけでスペクトルの端成分比率と組成式を求める。
        private SpectrumPredictionItem PredictRegressionOnly(
            MineralRegressionPredictionService regressionService,
            string regressionModelPath,
            string mineralName,
            string filePath,
            SpectrumPreprocessing preprocessing)
        {
            float[]? spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath, preprocessing);
            if (spectrum is null)
                return Failed(filePath, "スペクトルを読み込めませんでした。");

            try
            {
                // 260930Codex: This service is created for this batch, so do not hash the same regressor for every file.
                var regression = regressionService.Predict(regressionModelPath, spectrum, verifyModelFiles: false);
                var ratios = regression.Components.ToDictionary(component => component.ComponentName, component => component.Ratio);
                string formula = MineralFormulaGenerator.Generate(ratios, mineralName, _assemblyPath);
                return new SpectrumPredictionItem(filePath, null, Path.GetFileName(regressionModelPath), regression.Components, formula, null);
            }
            catch (Exception ex)
            {
                return Failed(filePath, $"回帰に失敗しました: {ex.Message}");
            }
        }

        private SpectrumPredictionItem PredictOne(
            MineralClassificationPredictionService classificationService,
            MineralRegressionPredictionService regressionService,
            string modelPath,
            string filePath,
            SpectrumPreprocessing preprocessing,
            Dictionary<string, string[]> regressionFolderCache)
        {
            float[]? spectrum = SpectrumDataLoader.LoadNormalizedSpectrum(filePath, preprocessing);
            if (spectrum is null)
                return Failed(filePath, "スペクトルを読み込めませんでした。");

            MineralClassificationPredictionResult classification;
            try
            {
                // 260930Codex: This fresh service loads the saved model once for the entire multi-file operation.
                classification = classificationService.Predict(modelPath, spectrum, verifyModelFiles: false);
            }
            catch (Exception ex)
            {
                return Failed(filePath, $"分類に失敗しました: {ex.Message}");
            }

            // 260622Codex: Even Unknown results keep regression for the closed-set top-1 candidate so borderline known spectra retain endmember estimates.
            var (regressionModelName, endmembers, formula) =
                PredictRegression(regressionService, modelPath, classification.PredictedMineral, spectrum, regressionFolderCache);

            return new SpectrumPredictionItem(filePath, classification, regressionModelName, endmembers, formula, null);
        }

        // 260620Claude: 予測鉱物に対応する *_Regression があれば端成分比率と組成式を求める。無ければ空のまま返す（分類は成功扱い）。
        private (string? ModelName, IReadOnlyList<MineralComponentRatio> Endmembers, string Formula) PredictRegression(
            MineralRegressionPredictionService regressionService,
            string modelPath,
            string predictedMineral,
            float[] spectrum,
            Dictionary<string, string[]> regressionFolderCache)
        {
            // 260620Claude: 回帰モデルの解決は "{鉱物名}*_Regression" の命名規約に従う。
            // 260727Claude: 旧コメントが参照していた DeepLearning.RunPrediction は削除済みで、この経路が唯一の正本。
            //   走査結果は鉱物名ごとにキャッシュする。複数候補の警告も鉱物ごとに 1 回だけ出る (以前は該当ファイル数だけ重複していた)。
            if (!regressionFolderCache.TryGetValue(predictedMineral, out string[]? candidates))
            {
                candidates = Directory.GetDirectories(modelPath, ModelArtifactPaths.GetRegressionSearchPattern(predictedMineral));
                regressionFolderCache[predictedMineral] = candidates;
                if (candidates.Length > 1)
                    _log($"回帰モデルが複数見つかりました。先頭を使用します: {Path.GetFileName(candidates[0])}");
            }

            if (candidates.Length == 0)
                return (null, [], string.Empty);

            string regressionPath = candidates[0];
            try
            {
                // 260930Codex: Keep one loaded regressor revision throughout this batch.
                var regression = regressionService.Predict(regressionPath, spectrum, verifyModelFiles: false);
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
