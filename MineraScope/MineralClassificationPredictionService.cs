using System.Text.Json;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.NumPy;
using static Tensorflow.KerasApi;

namespace MineraScope
{
    // 260522Codex: 正規化済みスペクトルに、保存済みAllMinerals分類モデルを適用する。
    // 260608Claude: predict()→Apply 解決後の per-call 詳細トレース(predict-single/batch-*, labels-*, model-cache-hit)を撤去し、tf-lock/model-load 等の診断要所だけ残す。
    internal sealed class MineralClassificationPredictionService
    {
        private string? _loadedClassificationPath;
        // 260930Codex: 単発予測と、マップがラベルを取得する際の1回に、内容の識別情報を確認する。
        private string? _loadedFingerprint;
        private IModel? _classificationModel;
        private string[]? _labelNames;
        // 260622Codex: 任意のオープンセット検知器の状態を、読込済み分類モデルとともにキャッシュする。
        private DenseClassificationFeatureExtractor? _featureExtractor;
        private MineralUnknownDetector? _unknownDetector;
        // 261001Codex: ファイル・PTS点・鉱物マップの予測で、保存済み検知器の選択を共用する。
        private SoftmaxOcnHead? _savedHead;
        // 260604Codex: キャッシュモデルを読んだスレッドを追跡し、TF.NETのThreadLocalコンテキストの移動を可視化する。
        private int _modelLoadManagedThreadId;
        private uint _modelLoadNativeThreadId;

        // 260606Claude: 同期版(SpectrumBatchPredictionWorkflow のバッチ用)。検証と dispatch は PredictAsync に集約し、呼び出し元 BG スレッドをブロックして結果を得る。
        // 260727Claude: 旧コメントの参照先 RunPrediction は削除済み。
        // 260924Codex: 呼び出しごとの学習済みheadは、組合せモデルのUnknown検知だけを変える。
        // 260930Codex: 新規の複数ファイル処理では1回だけ検証し、バッチ全体で同じリビジョンを維持できる。
        public MineralClassificationPredictionResult Predict(string modelPath, float[] normalizedSpectrum, SoftmaxOcnHead? learnedHead = null, bool verifyModelFiles = true)
            => PredictAsync(modelPath, normalizedSpectrum, learnedHead: learnedHead, verifyModelFiles: verifyModelFiles).GetAwaiter().GetResult();

        // 260606Claude: UI 単発分類用。Task.Run の代わりに専用スレッドへ投げ、UI を塞がず await できるようにする。
        // 260807Claude: detectUnknown は呼び出しごとの引数にする。検知器はモデルキャッシュと同じ寿命の共有フィールドなので、
        //   ここへ状態として持たせるとマップの設定が点分析へ波及する。既定 true で従来の呼び出しは無変更。
        // 260924Codex: 組合せ方式のheadも呼び出し側が渡し、共通モデルキャッシュには保持しない。
        // 260930Codex: 単発の対話予測は、キャッシュ重みの再利用前に選択モデルを確認する。
        public Task<MineralClassificationPredictionResult> PredictAsync(string modelPath, float[] normalizedSpectrum, bool detectUnknown = true, SoftmaxOcnHead? learnedHead = null, bool verifyModelFiles = true)
        {
            if (normalizedSpectrum.Length != SpectrumDataLoader.SpectrumLength)
                throw new InvalidOperationException($"{SpectrumDataLoader.SpectrumLength} 点のスペクトルだけを分類できます。");

            string classificationPath = GetClassificationPath(modelPath);
            return TensorFlowExecutor.RunAsync(() => RunLockedWithCacheReset(() => PredictCore(classificationPath, normalizedSpectrum, detectUnknown, learnedHead, verifyModelFiles), "single"));
        }

        // 260622Codex: 検知器を持たないモデルでは未学習検知を要求されても適用できない。
        // 260807Claude: マップは「実際に適用したか」を結果へ記録するため、モデル読込後 (GetLabelNames 後) にこの値を見る。
        // 260807Codex: ApplyUnknownDetector と同じ条件で、実際に検知を適用できる状態だけを公開する。
        public bool HasUnknownDetector => _unknownDetector is not null && _featureExtractor is not null;

        // 261001Claude: 読み込んだ保存物だけが方式を決めます。カタログのファイル有無から推測すると、
        //   表示と実際の判定がずれる余地が残ります。単発・マップの両方がこの1つの規則を使います。
        public UnknownDetectionMethod DescribeUnknownDetection(bool detectUnknown) =>
            ResolveUnknownDetection(detectUnknown, _savedHead);

        private UnknownDetectionMethod ResolveUnknownDetection(bool detectUnknown, SoftmaxOcnHead? head) =>
            !detectUnknown || !HasUnknownDetector ? UnknownDetectionMethod.None
            : head is null ? UnknownDetectionMethod.Distance : UnknownDetectionMethod.LearnedHead;

        // 260606Claude: TF 本体(load/np.array/Apply/numpy/ToArray/dispose)。必ず専用スレッド上で実行し、戻すのは managed な結果のみ。
        private MineralClassificationPredictionResult PredictCore(string classificationPath, float[] normalizedSpectrum, bool detectUnknown, SoftmaxOcnHead? learnedHead, bool verifyModelFiles)
        {
            // 260930Codex: 検証はUIスレッドから離れたTensorFlow実行基盤で行う。
            EnsureModelLoaded(classificationPath, verifyModelFiles);

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
                        // 260924Codex: Softmax確率とTop-1を保持し、保存済みUnknown判定だけを変える。
                        // 261001Claude: 呼出側が判定器を指定した場合はそちらが方式を決めます。
                        SoftmaxOcnHead? head = learnedHead ?? _savedHead;
                        UnknownDetectionMethod method = ResolveUnknownDetection(detectUnknown, head);
                        MineralUnknownDetectionResult? unknown = method == UnknownDetectionMethod.None
                            ? null
                            : EvaluateUnknown(_featureExtractor!.Transform(normalizedSpectrum), head);
                        return BuildPredictionResult(probabilities, unknown, method);
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
            // 260908Codex: 共通の中核処理は、対比較の実験マップ用にクローズドセットのラベルも保持できる。
            // 260909Codex: 中核処理は重複する行数引数を受け取らず、入力行列から行数を求める。
            return TensorFlowExecutor.Run(() => RunLockedWithCacheReset(() => PredictTop1BatchCore(classificationPath, batch, cancellationToken, detectUnknown, false).WithUnknown, "batch"));
        }

        // 260908Codex: 分類器の順伝播1回でマップの両設定を評価し、検知が利用不能なら実験エラーとする。


        // 260606Claude: バッチ TF 本体。必ず専用スレッド上で実行し、戻すのは top1 ラベル index の int[] のみ。
        // 260807Codex: closed-set 推論後の Unknown 上書きだけを条件付きにする。
        // 260908Codex: 呼び出し側が対比較を求めた場合だけ、クローズドセット用の別配列を保持する。
        // 260909Codex: バッチ次元の唯一の情報源を入力行列にする。
        private (int[] WithoutUnknown, int[] WithUnknown) PredictTop1BatchCore(string classificationPath, float[,] batch, CancellationToken cancellationToken, bool detectUnknown, bool retainClosedSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureModelLoaded(classificationPath);

            // 260908Codex: 検知有効と記したマップを、暗黙に検知無効へ切り替えない。
            if (retainClosedSet && !HasUnknownDetector)
                throw new InvalidDataException("未学習検知あり・なしの比較には、有効な未学習検知器を持つ分類モデルが必要です。");
            // 260909Codex: モデルと検知器の検証後に、既存の空バッチの検査を維持する。
            int rows = batch.GetLength(0);
            if (rows == 0)
                return ([], []);

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
                        // 260908Codex: Unknownラベルは対比較用コピーだけへ書き、分類器のTop-1を利用可能に保つ。
                        var withoutUnknown = retainClosedSet ? (int[])results.Clone() : results;
                        // 260807Codex: OFF のマップでは top-1 ラベルを Unknown に上書きしない。
                        if (detectUnknown)
                            ApplyUnknownDetector(batch, rows, results);
                        return (withoutUnknown, results);
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
            // 260930Codex: マップ準備で一度検証し、以後の全チャンクでキャッシュモデルを使う。
            EnsureModelLoaded(classificationPath, verifyModelFiles: true);
            return (string[])_labelNames!.Clone();
        }

        // 260622Codex: クローズドセットの詳細を保持し、対応する呼び出し側向けにnull許容のオープンセット情報を加える。
        private MineralClassificationPredictionResult BuildPredictionResult(float[] probabilities, MineralUnknownDetectionResult? unknown, UnknownDetectionMethod unknownDetection)
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
                UnknownDetection: unknownDetection);
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

        // 261001Codex: 従来の距離判定を保ち、保存済みheadは記録した入力幅と固定境界0.5を使う。
        private MineralUnknownDetectionResult EvaluateUnknown(float[] embedding, SoftmaxOcnHead? head)
        {
            if (head is null) return _unknownDetector!.Evaluate(embedding);
            var distances = new double[_unknownDetector!.LabelCount];
            _unknownDetector.Distances(embedding, distances);
            float[] input = SoftmaxOcnHeadFeatures.Nearest(
                SoftmaxOcnHeadFeatures.TransformAll(distances, _unknownDetector.Thresholds, normalized: false), null, head.InputCount);
            double score = head.Predict(input);
            return new MineralUnknownDetectionResult(score > .5, score, .5, Array.IndexOf(distances, distances.Min()));
        }

        // 260622Codex: PTSマップのバッチでは高速なTop-1経路を保ち、学習済み鉱物の埋め込み分布の外にある行だけを上書きする。
        private void ApplyUnknownDetector(float[,] batch, int rows, int[] results)
        {
            if (_unknownDetector is null || _featureExtractor is null)
                return;

            var embeddings = _featureExtractor.Transform(batch, rows);
            var detector = _unknownDetector;
            // 260810Claude: 行ごとに独立した数値計算 (TF 呼び出しなし) なので並列化する。書き込み先も行ごとに異なる。
            Parallel.For(0, rows, () => new double[detector.EmbeddingDim], (row, _, diff) =>
            {
                // 261001Codex: マップも単発スペクトル推論と同じ保存済みheadと判定境界を使う。
                bool isUnknown;
                if (_savedHead is null)
                    isUnknown = detector.Evaluate(embeddings, row, diff).IsUnknown;
                else
                {
                    var embedding = new float[detector.EmbeddingDim];
                    for (int column = 0; column < embedding.Length; column++) embedding[column] = embeddings[row, column];
                    isUnknown = EvaluateUnknown(embedding, _savedHead).IsUnknown;
                }
                if (isUnknown)
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
            // 260604Codex: ロックは直列化を保証するがスレッド固定は保証しないため、待機と進入を両方記録する。
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

        private void EnsureModelLoaded(string classificationPath, bool verifyModelFiles = false)
        {
            // 260930Codex: マップのチャンクは準備したモデルを使い、各操作の開始時に選択モデルを検証する。
            string? fingerprint = verifyModelFiles || _classificationModel is null || _loadedClassificationPath != classificationPath
                ? ModelArtifactFingerprint.Compute(classificationPath) : _loadedFingerprint;
            if (_classificationModel is not null && _loadedClassificationPath == classificationPath && _loadedFingerprint == fingerprint)
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

            // 260604Codex: clear_session・load_modelはネイティブ状態の境界なので、クラッシュログに記録する。
            TensorFlowPredictionDebugLog.Write("model-load-start", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()}");
            ResetModelCache();
            TensorFlowPredictionDebugLog.Write("model-clear-session-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            keras.backend.clear_session();
            TensorFlowPredictionDebugLog.Write("model-clear-session-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            TensorFlowPredictionDebugLog.Write("model-load-before", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)}");
            _classificationModel = keras.models.load_model(classificationPath);
            _labelNames = labelNames;
            // 260622Codex: 分類器の重みが利用可能になってから、任意のオープンセット情報を読み込む。
            LoadUnknownDetector(classificationPath, labelNames.Length);
            // 261001Codex: headが存在して不正な場合はエラーとし、従来検知器へ暗黙に切り替えない。
            if (File.Exists(Path.Combine(classificationPath, SoftmaxOcnHeadArtifact.FileName)))
            {
                if (!HasUnknownDetector) throw new InvalidDataException("OCN判定器に必要な距離検知器を読み込めません。");
                var artifact = SoftmaxOcnHeadArtifact.Load(Path.GetDirectoryName(classificationPath)!);
                _savedHead = new SoftmaxOcnHead(new Random(0), artifact.InputCount);
                _savedHead.Restore(artifact.Weights);
            }
            _loadedClassificationPath = classificationPath;
            // 260930Codex: 読み込んだ重みと付随ファイルの識別情報をまとめて保存する。
            _loadedFingerprint = fingerprint;
            _modelLoadManagedThreadId = Environment.CurrentManagedThreadId;
            _modelLoadNativeThreadId = TensorFlowPredictionDebugLog.CurrentNativeThreadId;
            TensorFlowPredictionDebugLog.Write("model-load-after", $"path={TensorFlowPredictionDebugLog.Clean(classificationPath)} {GetModelDebugInfo()} labels={labelNames.Length}");
        }

        // 260622Codex: Unknown検知は任意機能なので、成果物が古い・欠損した場合はオープンセット得点だけを無効にする。
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
            // 260604Codex: キャッシュ破棄でモデルとスレッドの対応が変わるため、モデルがなくても記録する。
            TensorFlowPredictionDebugLog.Write("model-cache-reset", $"{GetModelDebugInfo()}");
            TensorFlowRuntimeGate.DisposeIfPossible(_classificationModel);
            _loadedClassificationPath = null;
            // 260930Codex: 読込失敗時に、前の内容識別情報を残さない。
            _loadedFingerprint = null;
            _classificationModel = null;
            _labelNames = null;
            // 260622Codex: 任意のオープンセット用キャッシュの寿命を、分類器キャッシュと一致させる。
            _featureExtractor = null;
            _unknownDetector = null;
            // 261001Codex: headと分類器は、常に同じキャッシュリビジョンを使う。
            _savedHead = null;
            _modelLoadManagedThreadId = 0;
            _modelLoadNativeThreadId = 0;
        }

        // 260604Codex: 逐次書き出すTensorFlow診断では、モデル識別情報を簡潔にする。
        private string GetModelDebugInfo()
        {
            int modelHash = _classificationModel is null
                ? 0
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_classificationModel);
            return $"modelHash={modelHash} loadedManagedThread={_modelLoadManagedThreadId} loadedNativeThread={_modelLoadNativeThreadId} loadedPath={TensorFlowPredictionDebugLog.Clean(_loadedClassificationPath ?? string.Empty)}";
        }

    }

    // 260521Codex: UI表示用に、最上位の分類結果と全ラベル確率を保持する。
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
        // 261001Claude: 適用の有無ではなく方式を持ち、有無はそこから導きます。両方を別々に持つと、方式を足したときに
        //   「適用したが方式は未設定」という表現できてはいけない状態が作れてしまいます。
        UnknownDetectionMethod UnknownDetection = UnknownDetectionMethod.None)
    {
        public bool UnknownDetectionApplied => UnknownDetection != UnknownDetectionMethod.None;

        // 260622Codex: UIと出力で、クローズドセットのTop-1候補を失わずにUnknownを示せるようにする。
        public string DisplayMineralName => IsUnknown ? MineralUnknownDetector.UnknownDisplayName : PredictedMineral;
    }

    // 260521Codex: 分類モデルが返した、1つの鉱物ラベルの確率を保持する。
    internal sealed record MineralClassificationProbability(string MineralName, float Confidence);
}
