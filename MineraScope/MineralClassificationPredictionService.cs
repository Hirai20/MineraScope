using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: Runs the saved AllMinerals classification model against an already normalized spectrum.
    // 260608Claude: predict()→Apply 解決後の per-call 詳細トレース(predict-single/batch-*, labels-*, model-cache-hit)を撤去し、tf-lock/model-load 等の診断要所だけ残す。
    internal sealed class MineralClassificationPredictionService
    {
        private string? _loadedClassificationPath;
        private IModel? _classificationModel;
        private string[]? _labelNames;
        // 260622Codex: Optional open-set detector state is cached with the loaded classifier model.
        private DenseClassificationFeatureExtractor? _featureExtractor;
        private MineralUnknownDetector? _unknownDetector;
        // 260604Codex: Track the thread that loaded the cached model so TF.NET ThreadLocal context moves are visible.
        private int _modelLoadManagedThreadId;
        private uint _modelLoadNativeThreadId;

        // 260606Claude: 同期版(SpectrumBatchPredictionWorkflow のバッチ用)。検証と dispatch は PredictAsync に集約し、呼び出し元 BG スレッドをブロックして結果を得る。
        // 260727Claude: 旧コメントの参照先 RunPrediction は削除済み。
        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum)
            => PredictAsync(modelPath, normalizedSpectrum).GetAwaiter().GetResult();

        // 260606Claude: UI 単発分類用。Task.Run の代わりに専用スレッドへ投げ、UI を塞がず await できるようにする。
        // 260807Claude: detectUnknown は呼び出しごとの引数にする。検知器はモデルキャッシュと同じ寿命の共有フィールドなので、
        //   ここへ状態として持たせるとマップの設定が点分析へ波及する。既定 true で従来の呼び出しは無変更。
        public Task<MineralClassificationPredictionResult> PredictAsync(string modelPath, float[] normalizedSpectrum, bool detectUnknown = true)
        {
            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = GetClassificationPath(modelPath);
            return TensorFlowExecutor.RunAsync(() => RunLockedWithCacheReset(() => PredictCore(classificationPath, normalizedSpectrum, detectUnknown), "single"));
        }

        // 260622Codex: 検知器を持たないモデルでは未学習検知を要求されても適用できない。
        // 260807Claude: マップは「実際に適用したか」を結果へ記録するため、モデル読込後 (GetLabelNames 後) にこの値を見る。
        // 260807Codex: ApplyUnknownDetector と同じ条件で、実際に検知を適用できる状態だけを公開する。
        public bool HasUnknownDetector => _unknownDetector is not null && _featureExtractor is not null;

        // 260606Claude: TF 本体(load/np.array/Apply/numpy/ToArray/dispose)。必ず専用スレッド上で実行し、戻すのは managed な結果のみ。
        private MineralClassificationPredictionResult PredictCore(string classificationPath, float[] normalizedSpectrum, bool detectUnknown)
        {
            EnsureModelLoaded(classificationPath);

            var spectrumReshaped = np.array(normalizedSpectrum).reshape(new Shape(1, SpectrumDataLoader.SpectrumLength));
            try
            {
                // 260605Claude: predict() は DataAdapter/DataHandler/prefetch worker を毎回構築してスレッドと GC をリークさせる。素のフォワードパスである Apply() に切り替える。
                var prediction = _classificationModel!.Apply(spectrumReshaped, training: false);
                try
                {
                    var predictionArray = prediction.numpy();
                    try
                    {
                        var probabilities = predictionArray.ToArray<float>();
                        // 260622Codex: closed-set の top-1 を保ちつつ、任意の未学習距離判定を付加する。
                        // 260807Codex: 呼び出し単位の設定で open-set 評価だけを省略し、closed-set の分類結果は維持する。
                        // 260827Claude: 適用可否は HasUnknownDetector と同じ条件なので、条件式を二重に書かず共有し、
                        //   その結果を予測結果へ記録する。下流で距離の有無から逆算させない。
                        bool unknownApplied = detectUnknown && HasUnknownDetector;
                        MineralUnknownDetectionResult? unknown = unknownApplied
                            ? _unknownDetector!.Evaluate(_featureExtractor!.Transform(normalizedSpectrum))
                            : null;
                        return BuildPredictionResult(probabilities, unknown, unknownApplied);
                    }
                    finally
                    {
                        TensorFlowRuntimeGate.DisposeIfPossible(predictionArray);
                    }
                }
                finally
                {
                    TensorFlowRuntimeGate.DisposeIfPossible(prediction);
                }
            }
            finally
            {
                TensorFlowRuntimeGate.DisposeIfPossible(spectrumReshaped);
            }
        }

        // 260807Codex: マップ作成条件ごとに未学習検知を切り替えられるよう、共有サービスへ状態を保持せず引数で渡す。
        public int[] PredictTop1Batch(string modelPath, float[,] batch, CancellationToken cancellationToken, bool detectUnknown)
        {
            int rows = batch.GetLength(0);
            if (batch.GetLength(1) != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            if (rows == 0)
                return [];

            string classificationPath = GetClassificationPath(modelPath);
            // 260606Claude: マップの chunk 推論も専用スレッドへ集約する。workflow スレッドはここでブロックし、合間の PTS 読み取り/正規化は並列のまま。
            // 260807Codex: TensorFlow 専用スレッド上の本体まで呼び出し単位の設定を伝える。
            return TensorFlowExecutor.Run(() => RunLockedWithCacheReset(() => PredictTop1BatchCore(classificationPath, batch, rows, cancellationToken, detectUnknown), "batch"));
        }

        // 260606Claude: バッチ TF 本体。必ず専用スレッド上で実行し、戻すのは top1 ラベル index の int[] のみ。
        // 260807Codex: closed-set 推論後の Unknown 上書きだけを条件付きにする。
        private int[] PredictTop1BatchCore(string classificationPath, float[,] batch, int rows, CancellationToken cancellationToken, bool detectUnknown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelLoaded(classificationPath);

            var batchArray = np.array(batch);
            try
            {
                // 260605Claude: predict() の臨時設備リークを避けるため、マップ側も Apply() で素のフォワードパスにする。
                var prediction = _classificationModel!.Apply(batchArray, training: false);
                try
                {
                    var predictionArray = prediction.numpy();
                    try
                    {
                        var probabilities = predictionArray.ToArray<float>();
                        var results = BuildTop1BatchResult(probabilities, rows);
                        // 260807Codex: OFF のマップでは top-1 ラベルを Unknown に上書きしない。
                        if (detectUnknown)
                            ApplyUnknownDetector(batch, rows, results);
                        return results;
                    }
                    finally
                    {
                        TensorFlowRuntimeGate.DisposeIfPossible(predictionArray);
                    }
                }
                finally
                {
                    TensorFlowRuntimeGate.DisposeIfPossible(prediction);
                }
            }
            finally
            {
                TensorFlowRuntimeGate.DisposeIfPossible(batchArray);
            }
        }

        public string[] GetLabelNames(string modelPath)
        {
            string classificationPath = GetClassificationPath(modelPath);
            // 260606Claude: load_model がここで走り得るので、ラベル取得も専用スレッドに乗せてモデル初期化スレッドを 1 本へ固定する。
            return TensorFlowExecutor.Run(() => RunLockedWithCacheReset(() => GetLabelNamesCore(classificationPath), "labels"));
        }

        private string[] GetLabelNamesCore(string classificationPath)
        {
            EnsureModelLoaded(classificationPath);
            return (string[])_labelNames!.Clone();
        }

        // 260622Codex: Preserve closed-set details and add nullable open-set metadata for callers that understand it.
        private MineralClassificationPredictionResult BuildPredictionResult(float[] probabilities, MineralUnknownDetectionResult? unknown, bool unknownDetectionApplied)
        {
            if (probabilities.Length != _labelNames!.Length)
                throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

            var orderedResults = new List<MineralClassificationProbability>(probabilities.Length);
            for (int i = 0; i < probabilities.Length; i++)
                orderedResults.Add(new MineralClassificationProbability(_labelNames[i], probabilities[i]));
            orderedResults.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            var best = orderedResults[0];
            string? nearestKnown = unknown is { } detection
                && detection.NearestLabelId >= 0
                && detection.NearestLabelId < _labelNames.Length
                    ? _labelNames[detection.NearestLabelId]
                    : null;
            // 260830Codex: 任意の未学習検知メタデータは名前付き引数で意味を明示する。
            return new MineralClassificationPredictionResult(
                best.MineralName,
                best.Confidence,
                orderedResults,
                IsUnknown: unknown?.IsUnknown ?? false,
                UnknownScore: unknown?.Score,
                UnknownThreshold: unknown?.Threshold,
                NearestKnownMineral: nearestKnown,
                UnknownDetectionApplied: unknownDetectionApplied);
        }

        private int[] BuildTop1BatchResult(float[] probabilities, int rows)
        {
            int labelCount = _labelNames!.Length;
            if (probabilities.Length != (long)rows * labelCount)
                throw new InvalidDataException("分類モデルの出力数と labelEncoder.json が一致しません。");

            var results = new int[rows];
            for (int r = 0; r < rows; r++)
            {
                int offset = r * labelCount;
                int best = 0;
                float bestValue = probabilities[offset];
                for (int j = 1; j < labelCount; j++)
                {
                    float value = probabilities[offset + j];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = j;
                    }
                }
                results[r] = best;
            }
            return results;
        }

        // 260622Codex: PTS map batches keep the fast top-1 path and overwrite only rows outside the known embedding distribution.
        private void ApplyUnknownDetector(float[,] batch, int rows, int[] results)
        {
            if (_unknownDetector is null || _featureExtractor is null)
                return;

            var embeddings = _featureExtractor.Transform(batch, rows);
            var detector = _unknownDetector;
            // 260810Claude: 行ごとに独立した数値計算 (TF 呼び出しなし) なので並列化する。書き込み先も行ごとに異なる。
            Parallel.For(0, rows, () => new double[detector.EmbeddingDim], (row, _, diff) =>
            {
                if (detector.Evaluate(embeddings, row, diff).IsUnknown)
                    results[row] = PtsClassificationMapResult.UnknownLabelId;
                return diff;
            }, _ => { });
        }

        private static string GetClassificationPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
                throw new DirectoryNotFoundException("モデルフォルダが見つかりません。");

            // 260727Claude: 分類フォルダ名は ModelArtifactPaths を単一の基準にする。
            return ModelArtifactPaths.GetClassificationFolder(modelPath);
        }

        // 260606Claude: 呼び出し元は必ず TensorFlowExecutor 経由なので action は専用スレッド上で動く。lock は単一スレッドで非競合となり、段階移行の tripwire として残す(第2パッチで撤去予定)。
        private T RunLockedWithCacheReset<T>(Func<T> action, string operation)
        {
            // 260604Codex: The lock proves serialization but not thread affinity, so log both wait and enter.
            TensorFlowPredictionDebugLog.Write("tf-lock-wait", $"operation={operation}");
            lock (TensorFlowRuntimeGate.SyncRoot)
            {
                TensorFlowPredictionDebugLog.Write("tf-lock-enter", $"operation={operation}");
                try
                {
                    return action();
                }
                catch
                {
                    TensorFlowPredictionDebugLog.Write("tf-managed-exception", $"operation={operation}");
                    ResetModelCache();
                    throw;
                }
                finally
                {
                    TensorFlowPredictionDebugLog.Write("tf-lock-exit", $"operation={operation}");
                }
            }
        }

        private void EnsureModelLoaded(string classificationPath)
        {
            if (_classificationModel is not null && _loadedClassificationPath == classificationPath)
                return;

            if (!Directory.Exists(classificationPath))
                throw new DirectoryNotFoundException($"分類モデル {ModelArtifactPaths.ClassificationFolderName} が見つかりません。");

            string encoderPath = Path.Combine(classificationPath, ModelArtifactPaths.LabelEncoderFileName);
            if (!File.Exists(encoderPath))
                throw new FileNotFoundException("labelEncoder.json が見つかりません。", encoderPath);

            Dictionary<string, int>? encoder = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(encoderPath));
            if (encoder is not { Count: > 0 })
                throw new InvalidDataException("labelEncoder.json の内容を読み取れませんでした。");

            string[] labelNames = BuildLabelNames(encoder);

            // 260604Codex: clear_session/load_model are native-state boundaries and must be visible in the crash log.
            TensorFlowPredictionDebugLog.Write("model-load-start", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()}");
            ResetModelCache();
            TensorFlowPredictionDebugLog.Write("model-clear-session-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            keras.backend.clear_session();
            TensorFlowPredictionDebugLog.Write("model-clear-session-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            TensorFlowPredictionDebugLog.Write("model-load-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            _classificationModel = keras.models.load_model(classificationPath);
            _labelNames = labelNames;
            // 260622Codex: Load optional open-set metadata after the classifier weights are available.
            LoadUnknownDetector(classificationPath, labelNames.Length);
            _loadedClassificationPath = classificationPath;
            _modelLoadManagedThreadId = Environment.CurrentManagedThreadId;
            _modelLoadNativeThreadId = TensorFlowPredictionDebugLog.CurrentNativeThreadId;
            TensorFlowPredictionDebugLog.Write("model-load-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()} labels={labelNames.Length}");
        }

        // 260622Codex: Unknown detection is optional, so stale or missing artifacts disable only the open-set score.
        private void LoadUnknownDetector(string classificationPath, int labelCount)
        {
            _featureExtractor = null;
            _unknownDetector = null;
            try
            {
                _featureExtractor = DenseClassificationFeatureExtractor.FromModel(_classificationModel!);
                if (MineralUnknownDetector.TryLoad(classificationPath, labelCount, _featureExtractor.EmbeddingDim, out var detector, out string message))
                {
                    _unknownDetector = detector;
                    TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=loaded threshold={detector!.Threshold:G9}");
                    return;
                }

                TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=disabled reason={TensorFlowPredictionDebugLog.Clean(message)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _featureExtractor = null;
                _unknownDetector = null;
                TensorFlowPredictionDebugLog.Write("unknown-detector-load", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} status=disabled reason={TensorFlowPredictionDebugLog.Clean(ex.Message)}");
            }
        }

        private static string[] BuildLabelNames(Dictionary<string, int> encoder)
        {
            int count = encoder.Count;
            var names = new string[count];
            var assigned = new bool[count];
            foreach (var (name, index) in encoder)
            {
                if (index < 0 || index >= count || assigned[index])
                    throw new InvalidDataException("labelEncoder.json のラベル index が不正です（負値・重複・欠番）。");

                names[index] = name;
                assigned[index] = true;
            }
            return names;
        }

        private void ResetModelCache()
        {
            // 260604Codex: Cache drops can invalidate the model/thread relationship, so log them even when no model exists.
            TensorFlowPredictionDebugLog.Write("model-cache-reset", $"{GetModelDebugInfo()}");
            TensorFlowRuntimeGate.DisposeIfPossible(_classificationModel);
            _loadedClassificationPath = null;
            _classificationModel = null;
            _labelNames = null;
            // 260622Codex: Keep optional open-set cache lifetime identical to the classifier cache.
            _featureExtractor = null;
            _unknownDetector = null;
            _modelLoadManagedThreadId = 0;
            _modelLoadNativeThreadId = 0;
        }

        // 260604Codex: Keep model identity compact in the flush-backed TensorFlow diagnostics.
        private string GetModelDebugInfo()
        {
            int modelHash = _classificationModel is null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_classificationModel);
            return $"modelHash={modelHash} loadedManagedThread={_modelLoadManagedThreadId} loadedNativeThread={_modelLoadNativeThreadId} loadedPath={TensorFlowPredictionDebugLog.Clean(_loadedClassificationPath ?? string.Empty)}";
        }

    }

    // 260521Codex: Holds the top classification result and all label probabilities for UI display.
    internal sealed record MineralClassificationPredictionResult(
        string PredictedMineral,
        float Confidence,
        IReadOnlyList<MineralClassificationProbability> Probabilities,
        bool IsUnknown = false,
        // 260828Codex: 未学習検知が比較した double 値を保存処理まで丸めず渡す。
        double? UnknownScore = null,
        double? UnknownThreshold = null,
        string? NearestKnownMineral = null,
        // 260827Claude: 未学習検知を実際に適用したかを記録する。距離の有無から逆算すると、距離を持たない検知方式や
        //   Evaluate が null を返す経路を足したときに、判定は正しいまま表示と保存物だけが静かに嘘になる。
        bool UnknownDetectionApplied = false)
    {
        // 260622Codex: UI and exports can show Unknown without losing the closed-set top-1 candidate.
        public string DisplayMineralName => IsUnknown ? MineralUnknownDetector.UnknownDisplayName : PredictedMineral;
    }

    // 260521Codex: Holds one mineral label probability returned by the classification model.
    internal sealed record MineralClassificationProbability(string MineralName, float Confidence);
}
