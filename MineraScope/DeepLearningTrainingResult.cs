namespace MineraScope
{
    // 260924Codex: 学習の進捗と結果を、学習処理とワークフローの共通契約とする。
    // 260606Claude: 学習の進捗を UI へ運ぶ DTO。OverallFraction は分類1個+回帰N個を「モデル均等×エポック比」で 0..1 に正規化した全体進捗。経過時間は UI 側の操作ストップウォッチで表示する。
    internal sealed record TrainingProgress(
        string ModelName,
        int ModelIndex,
        int TotalModels,
        int Epoch,
        int RequestedEpochs,
        double OverallFraction);

    // 260902Codex: 想定内の学習中止理由を、予期しない例外と分けて workflow へ返します。
    internal enum DeepLearningTrainingFailureKind
    {
        TrainingDataLoadFailed,
        // 260930Codex: 分類の件数不足を、学習済みモデルとして昇格させない。
        InsufficientTrainingSamples
    }

    // 260902Codex: 学習本体が正常完了したか、想定内の理由で開始前に止まったかを表します。
    internal enum DeepLearningTrainingStatus
    {
        Completed,
        NotCompleted
    }

    // 260902Codex: 分類で打ち切った場合に回帰と正式昇格へ進ませないための内部結果です。
    internal sealed record DeepLearningTrainingResult
    {
        private DeepLearningTrainingResult(
            DeepLearningTrainingStatus status,
            DeepLearningTrainingFailureKind? failureKind,
            string? failureReason,
            IReadOnlyList<TrainingModelMetrics> modelMetrics)
        {
            Status = status;
            FailureKind = failureKind;
            FailureReason = failureReason;
            ModelMetrics = modelMetrics;
        }

        // 261001Codex: 作業用ファイルを再読込せず、採用した分類器のマネージドコピーを任意のhead学習へ渡す。
        public DenseClassificationFeatureExtractor? ClassifierFeatures { get; init; }
        public DeepLearningTrainingStatus Status { get; }

        public DeepLearningTrainingFailureKind? FailureKind { get; }

        public string? FailureReason { get; }

        public IReadOnlyList<TrainingModelMetrics> ModelMetrics { get; }

        public static DeepLearningTrainingResult CreateCompleted(IReadOnlyList<TrainingModelMetrics> modelMetrics)
        {
            ArgumentNullException.ThrowIfNull(modelMetrics);
            if (modelMetrics.Count == 0)
                throw new ArgumentException("At least one model metric is required for a completed training run.", nameof(modelMetrics));
            return new(DeepLearningTrainingStatus.Completed, failureKind: null, failureReason: null, modelMetrics);
        }

        public static DeepLearningTrainingResult CreateNotCompleted(
            DeepLearningTrainingFailureKind failureKind,
            string failureReason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            return new(DeepLearningTrainingStatus.NotCompleted, failureKind, failureReason, Array.Empty<TrainingModelMetrics>());
        }
    }

}
