using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MineraScope
{
    // 260514Codex: manifest から選んだ学習入力と正式保存先を workflow の plan にまとめます。
    internal sealed record ModelTrainingPlan(
        string ModelOutputFolder,
        IReadOnlyList<SpectrumTrainingPool> TrainingPools,
        ModelTrainingSettings Settings,
        DetectorProfile DetectorProfile,
        // 260619Codex: 上書きの同意を計画内に保持し、ワークフローの呼び出し側による意図しない置換を防ぐ。
        bool AllowOverwriteExistingModel,
        // 260901Codex: 明示的な複数時間要求の厳密な件数と再現性メタデータを、昇格処理まで渡す。
        TrainingDataProvenanceSpecification? TrainingDataProvenance = null,
        // 260928Codex: Seedがnullでなければ、分類器と学習型Unknown headを一体で昇格する。
        int? SoftmaxOcnSeed = null);

    // 260901Codex: 正式モデルとして利用可能になったかを、学習処理の正常終了とは分けて表します。
    internal enum ModelTrainingStatus
    {
        Promoted,
        NotPromoted
    }

    // 260902Codex: UI が想定内の未昇格理由ごとに状態と案内を変えられるよう、理由を型で渡します。
    internal enum ModelTrainingFailureKind
    {
        TrainingDataLoadFailed,
        // 260930Codex: 件数不足と学習入力の読込不能を区別する。
        InsufficientTrainingSamples
    }

    // 260901Codex: 想定内の未昇格理由と保存済み成果物を例外にせず UI へ返します。
    internal sealed record ModelTrainingResult
    {
        private ModelTrainingResult(
            ModelTrainingStatus status,
            ModelTrainingFailureKind? failureKind,
            string? failureReason,
            string? preservedArtifactsFolder,
            // 260930Codex: 昇格した結果だけに、任意の定量処理の完了通知を付ける。
            string? completionMessage = null)
        {
            Status = status;
            FailureKind = failureKind;
            FailureReason = failureReason;
            PreservedArtifactsFolder = preservedArtifactsFolder;
            // 260930Codex: 成果物を再読込せず、保存モデルの要約をフォームへ渡す。
            CompletionMessage = completionMessage;
        }

        public ModelTrainingStatus Status { get; }

        public ModelTrainingFailureKind? FailureKind { get; }

        public string? FailureReason { get; }

        public string? PreservedArtifactsFolder { get; }

        // 260930Codex: 要求した定量がすべて学習済みなら、通知は不要とする。
        public string? CompletionMessage { get; }

        // 260930Codex: 任意の定量の省略は、鉱物ごとに列挙せずまとめて通知する。
        public static ModelTrainingResult CreatePromoted(IReadOnlyList<TrainingModelMetrics>? modelMetrics = null, string? ocnFailureReason = null)
        {
            var skipped = modelMetrics?.Where(metric => metric.Target == "regression" && metric.Status == "skipped").ToArray()
                ?? Array.Empty<TrainingModelMetrics>();
            string? completionMessage = null;
            if (skipped.Length > 0)
            {
                bool anyRegressionTrained = modelMetrics!.Any(metric => metric.Target == "regression" && metric.Status == "trained");
                // 260930Codex: 要約全体を一度だけ選び、両方の文で同じ条件に従う。
                completionMessage = (anyRegressionTrained
                    ? "分類モデルと学習可能な定量モデルを作成しました。\r\n一部の定量モデルはデータ数不足のため作成していません。"
                    : "分類モデルのみ作成しました。\r\n定量モデルはデータ数不足のため作成していません。")
                    + "\r\n\r\n" + string.Join("\r\n", skipped.Select(metric => metric.SkipReason).Distinct());
            }
            // 261001Codex: 分類器の昇格成功後にだけ、任意のheadの失敗を通知する。
            if (ocnFailureReason is not null)
            {
                // 261001Codex: 任意の通知を直接追加し、文言と区切りを保持する。
                string ocnNotice = "分類モデルを作成しました。OCN判定器は作成できませんでした。\r\n理由: " + ocnFailureReason;
                completionMessage = string.IsNullOrEmpty(completionMessage)
                    ? ocnNotice : completionMessage + "\r\n\r\n" + ocnNotice;
            }
            return new(
                ModelTrainingStatus.Promoted,
                failureKind: null,
                failureReason: null,
                preservedArtifactsFolder: null,
                completionMessage);
        }

        public static ModelTrainingResult CreateNotPromoted(
            ModelTrainingFailureKind failureKind,
            string failureReason,
            string? preservedArtifactsFolder = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
            if (preservedArtifactsFolder is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(preservedArtifactsFolder);

            return new(ModelTrainingStatus.NotPromoted, failureKind, failureReason, preservedArtifactsFolder);
        }
    }

    // 260514Codex: モデル学習の検証、tmp 保存先の管理、成功時の正式フォルダ昇格を Form から分離します。
    internal sealed class ModelTrainingWorkflow
    {
        // 260619Codex: 事前検査と昇格時の確認で、上書き防止のメッセージを共用する。
        private const string OverwriteNotAllowedMessage = "同じ名前のモデルが既に存在するため、上書き許可なしでは作成できません。";

        private readonly DeepLearning _deepLearning;
        private readonly Action<string> _logAction;

        // 260514Codex: workflow 実行に必要な学習本体とログ出力を constructor で固定します。
        public ModelTrainingWorkflow(DeepLearning deepLearning, Action<string> logAction)
        {
            ArgumentNullException.ThrowIfNull(deepLearning);
            ArgumentNullException.ThrowIfNull(logAction);

            _deepLearning = deepLearning;
            _logAction = logAction;
        }

        // 260514Codex: 学習対象は pool workflow が抽出した Completed spectrum だけにします。
        public ModelTrainingPlan CreatePlan(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumTrainingPool> trainingPools,
            // 260619Codex: UIの呼び出し側は、ユーザーが既存モデルの上書きを確認した場合だけtrueを渡す。
            bool allowOverwriteExistingModel = false,
            int? softmaxOcnSeed = null)
        {
            // 260917Claude: ルートの既定値はModelCreationPathsに置き、GUIとOCN学習で共用する。
            // 261014Codex: 画面と同じ既定保存先を使う。
            string modelOutputFolder = Path.Combine(request.Paths.ResolveModelRootFolder(), request.ModelName);

            return new ModelTrainingPlan(
                modelOutputFolder,
                trainingPools,
                request.Training,
                request.SemEdxCondition.GetDetectorProfile(),
                allowOverwriteExistingModel,
                // 260907Codex: 旧形式の明示割当一覧が空でも、組成・時間の予定表には由来情報が必要である。
                request.SpectrumTimeAllocations.Count == 0 && !request.UsesCompositionTimePlan
                    ? null
                    : new TrainingDataProvenanceSpecification(
                        request.GetEffectiveSpectrumTimeAllocations(),
                        TrainingDataProvenanceSpecification.DefaultSelectionSeed,
                        TrainingDataProvenanceSpecification.DefaultSplitSeed,
                        request.Training.ValidationSplit),
                softmaxOcnSeed);
        }

        // 260514Codex: target 未設定や pool 不足は学習開始前に止めます。
        public string? Validate(ModelTrainingPlan plan)
        {
            // 260930Codex: 学習開始前に不正な学習率を拒否する。
            if (!float.IsFinite(plan.Settings.LearningRate) || plan.Settings.LearningRate <= 0)
                return "分類の学習率は0より大きい有限の値にしてください。";
            // 261001Codex: 選択鉱物が0個・1個の場合は、同じ分類前提条件で拒否する。
            if (plan.TrainingPools.Select(pool => pool.MineralName).Distinct(StringComparer.Ordinal).Count() < 2)
                return "分類する鉱物を2種類以上選択してください。";

            if (plan.TrainingPools.Any(pool => pool.Samples.Count == 0))
                return "学習可能な spectrum がない鉱物があります。";

            // 260930Codex: モデル作成前に分類の件数検証を通し、定量の件数は任意条件とする。
            if (plan.Settings.BatchSize <= 0)
                return "バッチサイズは1以上にしてください。";
            if (!float.IsFinite(plan.Settings.ValidationSplit) || plan.Settings.ValidationSplit <= 0 || plan.Settings.ValidationSplit >= 1)
                return "検証データ割合は0より大きく1より小さい値にしてください。";
            var (trainingCount, validationCount) = TrainingSampleRequirements.GetSplitCounts(
                plan.TrainingPools.Sum(pool => pool.Samples.Count), plan.Settings.ValidationSplit);
            string? countFailure = TrainingSampleRequirements.GetFailureReason(trainingCount, validationCount, plan.Settings.BatchSize);
            if (countFailure is not null)
                return $"分類モデルを作成できません: {countFailure}";

            // 260928Codex: 疑似的にクラスを隠すエピソードには、学習開始前に十分な異なるクラスが必要である。
            // 261001Codex: 時間別pool数ではなく異なる鉱物数を数え、通常分類と同じ前提条件を使う。
            if (plan.SoftmaxOcnSeed is < 0)
                return "OCNの学習Seedは0以上にしてください。";

            return ValidateOutputPathLength(plan) ?? ValidateTrainingSources(plan);
        }

        // 261021Claude: 一時フォルダーの命名を、事前のパス長検査・実際の作成・境界テストで共有する。
        internal static string NewTemporaryOutputFolder(string modelOutputFolder) =>
            Path.Combine(Path.GetDirectoryName(modelOutputFolder)!, $".{Guid.NewGuid():N}.tmp");

        // 261021Claude: TensorFlowは保存中の中間ファイルがMAX_PATHを超えると失敗し、学習時間を無駄にするため、学習前に止める。
        //   内部フォルダー名は既存モデルとの互換のため変えられないので、計画に含まれる鉱物名から最長を求める。
        //   保存中の長さは実測した最悪値なので余裕を足さず、保存後のファイルは他の生成物と同じ余裕で検査する。
        private static string? ValidateOutputPathLength(ModelTrainingPlan plan)
        {
            string modelFolder = Path.GetFullPath(plan.ModelOutputFolder).TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(modelFolder)!;
            // 261021Claude: 学習と同じ判定・命名で内部フォルダーを求め、前後の区切り文字を含めた長さにする。
            int innerFolderLength = 1 + plan.TrainingPools
                .Where(pool => pool.HasRegressionModel)
                .Select(pool => ModelArtifactPaths.GetRegressionFolderName(pool.MineralName).Length)
                .Append(ModelArtifactPaths.ClassificationFolderName.Length)
                .Max() + 1;
            int whileSaving = Path.GetFileName(NewTemporaryOutputFolder(modelFolder)).Length + innerFolderLength
                + ModelArtifactPaths.SavedModelSavingDeepestLength;
            int afterSaving = Path.GetFileName(modelFolder).Length + innerFolderLength + ModelArtifactPaths.SavedModelDeepestLength;
            return GeneratedPathPolicy.FindTooLong(parent, whileSaving, "モデル", safetyMargin: 0)
                ?? GeneratedPathPolicy.FindTooLong(parent, afterSaving, "モデル");
        }

        // 260901Codex: TensorFlowの開始前に、不完全なメタデータと複数時間の比率不整合を拒否する。
        private static string? ValidateTrainingSources(ModelTrainingPlan plan)
        {
            SpectrumTrainingSample[] samples = plan.TrainingPools
                .SelectMany(pool => pool.Samples)
                .ToArray();
            int sourceCount = samples.Count(sample => sample.Source is not null);
            if (sourceCount > 0 && sourceCount != samples.Length)
                return $"学習元情報が一部のスペクトルにありません（全{samples.Length}件、情報あり{sourceCount}件）。";

            var specification = plan.TrainingDataProvenance;
            if (specification is null)
                return null;
            if (sourceCount == 0)
                return "測定時間を混在させる学習データに、manifest由来の学習元情報がありません。";

            string? allocationError = ValidateTimeAllocations(specification.TimeAllocations);
            if (allocationError is not null)
                return allocationError;
            if (!float.IsFinite(specification.ValidationSplit)
                || specification.ValidationSplit <= 0
                || specification.ValidationSplit >= 1)
                return "検証データ割合は0より大きく1より小さい値にしてください。";

            var duplicatePath = samples
                .GroupBy(sample => sample.FilePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicatePath is not null)
                return $"同じスペクトルファイルが重複して選択されています: {duplicatePath.Key}";

            foreach (var pool in plan.TrainingPools)
            {
                foreach (var sample in pool.Samples)
                {
                    var source = sample.Source!;
                    if (!double.IsFinite(source.LiveTime) || source.LiveTime <= 0
                        || string.IsNullOrWhiteSpace(source.ConditionKey)
                        || source.SimulationId < 0
                        || string.IsNullOrWhiteSpace(source.ManifestPath))
                        return $"学習元情報が不正です: {sample.FilePath}";
                    // 260902Codex: spectrum の存在と可読性は分類ローダーで一括検査し、全失敗をまとめて報告します。
                    if (!File.Exists(source.ManifestPath))
                        return $"学習元manifestが見つかりません: {source.ManifestPath}";
                }

                foreach (var allocation in specification.TimeAllocations)
                {
                    SpectrumTrainingSample[] allocationSamples = pool.Samples
                        .Where(sample => LiveTimesMatch(sample.Source!.LiveTime, allocation.LiveTime))
                        .ToArray();
                    if (allocationSamples.Length != allocation.TargetSpectrumCount)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データは"
                            + $"{allocationSamples.Length}件です。必要数は{allocation.TargetSpectrumCount}件です。";
                    if (allocationSamples.Select(sample => sample.Source!.ConditionKey).Distinct(StringComparer.Ordinal).Count() != 1)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データに複数のconditionKeyが混在しています。";
                    if (allocationSamples.Select(sample => sample.Source!.ManifestPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                        return $"{pool.MineralName}の{allocation.LiveTime}秒データに複数のmanifestが混在しています。";
                }

                int expectedCount = specification.TimeAllocations.Sum(allocation => allocation.TargetSpectrumCount);
                if (pool.Samples.Count != expectedCount)
                    return $"{pool.MineralName}の学習データは{pool.Samples.Count}件です。必要数は{expectedCount}件です。";

                var duplicateSource = pool.Samples
                    .GroupBy(sample => (sample.Source!.ManifestPath, sample.Source.SimulationId))
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateSource is not null)
                    return $"{pool.MineralName}で同じsimulation IDが重複しています: "
                        + $"{duplicateSource.Key.ManifestPath} / {duplicateSource.Key.SimulationId}";
            }

            return null;
        }

        // 260901Codex: ワークフローを直接呼ぶ場合に備え、要求構築とは独立して由来情報のコピーを検証する。
        private static string? ValidateTimeAllocations(IReadOnlyList<SpectrumTimeAllocation> allocations)
        {
            if (allocations.Count == 0)
                return "測定時間の配分が指定されていません。";

            for (int i = 0; i < allocations.Count; i++)
            {
                var allocation = allocations[i];
                if (!double.IsFinite(allocation.LiveTime) || allocation.LiveTime <= 0)
                    return "測定時間は0より大きい有限値にしてください。";
                if (allocation.TargetSpectrumCount <= 0)
                    return "測定時間ごとのスペクトル数は1以上にしてください。";
                for (int j = i + 1; j < allocations.Count; j++)
                    if (LiveTimesMatch(allocation.LiveTime, allocations[j].LiveTime))
                        return $"測定時間 {allocation.LiveTime} 秒が重複しています。";
            }

            return null;
        }

        // 260901Codex: SEM条件の識別検証と同じ許容誤差を使う。
        private static bool LiveTimesMatch(double left, double right) =>
            Math.Abs(left - right) <= 1e-9;

        // 260514Codex: 学習は tmp フォルダで完走させ、成功時だけ正式フォルダへ昇格します。
        // 260606Claude: 学習進捗 (モデル/エポック) を呼び出し元の UI へ渡すため progress を受け取ります。
        // 260901Codex: 呼び出し元が正式昇格と想定内の未昇格を区別できる結果を返します。
        public Task<ModelTrainingResult> RunAsync(
            ModelTrainingPlan plan,
            IProgress<TrainingProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.Run(() => Run(plan, progress, cancellationToken), cancellationToken);

        // 260514Codex: キャンセルや失敗では tmp だけを片付け、既存の正式フォルダは残します。
        // 260901Codex: 正式フォルダへの昇格が完了した時点でだけ Promoted を返します。
        private ModelTrainingResult Run(ModelTrainingPlan plan, IProgress<TrainingProgress>? progress, CancellationToken cancellationToken)
        {
            // 260917Codex: 削除処理より前に取得し、拒否された2回目の実行が1回目の出力を消さないようにする。
            using var outputLease = PathOperationLease.Acquire(plan.ModelOutputFolder, "ModelOutput");
            // 260917Codex: 専用の作業領域を使い、意図的に.tmpで終わる名前を付けたモデルとの衝突も避ける。
            // 260928Codex: 原子的な昇格のため同じボリュームで準備し、TensorFlowの深いSavedModelパスを短く保つ。
            string temporaryOutputFolder = NewTemporaryOutputFolder(plan.ModelOutputFolder);
            // 260928Codex: 正規化したheadのスナップショットを、昇格する成果物の外に置く。
            string? headDatasetFolder = plan.SoftmaxOcnSeed is null ? null
                : Path.Combine(Path.GetTempPath(), "MineraScopeHeadData", Guid.NewGuid().ToString("N"));
            // 261001Codex: 任意のhead作成に失敗しても、有効な分類器は破棄しない。
            string? ocnFailureReason = null;
            _logAction("モデル作成開始");
            _logAction($"保存先（正式）: {plan.ModelOutputFolder}");
            _logAction($"保存先（仮）: {temporaryOutputFolder}");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 260901Codex: ワークフローを直接呼ぶ場合も、UIと同じ厳密な複数時間の事前検査を行う。
                string? validationError = Validate(plan);
                if (validationError is not null)
                    throw new InvalidOperationException(validationError);

                // 260619Codex: 計画に上書き同意がなければ、高負荷な学習より前に停止する。
                if (Directory.Exists(plan.ModelOutputFolder) && !plan.AllowOverwriteExistingModel)
                    throw new InvalidOperationException(OverwriteNotAllowedMessage);

                if (headDatasetFolder is not null)
                {
                    try
                    {
                        ClassificationTrainingDataset.Create(plan.TrainingPools, plan.Settings, headDatasetFolder, cancellationToken, _logAction);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException and not TrainingInputChangedException)
                    {
                        ocnFailureReason = exception.Message;
                        _logAction($"OCN学習準備を省略します: {ocnFailureReason}");
                    }
                }
                Directory.CreateDirectory(temporaryOutputFolder);
                var trainingResult = _deepLearning.RunTraining(
                    plan.TrainingPools,
                    plan.Settings.Epochs,
                    plan.Settings.BatchSize,
                    plan.Settings.EarlyStoppingPatience,
                    plan.Settings.ValidationSplit,
                    plan.Settings.UnknownDistanceScale,
                    temporaryOutputFolder,
                    progress,
                    cancellationToken,
                    // 260918Codex: 通常のモデル作成でも、学習設定に記録したSoftmax学習率を使う。
                    classificationLearningRate: plan.Settings.LearningRate,
                    captureClassifierFeatures: headDatasetFolder is not null && ocnFailureReason is null);
                if (trainingResult.Status == DeepLearningTrainingStatus.NotCompleted)
                {
                    string failureReason = trainingResult.FailureReason
                        ?? throw new InvalidOperationException("未完了の学習結果に理由がありません。");
                    var failureKind = trainingResult.FailureKind switch
                    {
                        DeepLearningTrainingFailureKind.TrainingDataLoadFailed => ModelTrainingFailureKind.TrainingDataLoadFailed,
                        // 260930Codex: 学習処理の直接呼び出しが件数不足で拒否された場合、既存の正式モデルを保持する。
                        DeepLearningTrainingFailureKind.InsufficientTrainingSamples => ModelTrainingFailureKind.InsufficientTrainingSamples,
                        null => throw new InvalidOperationException("未完了の学習結果に理由種別がありません。"),
                        _ => throw new InvalidOperationException($"未対応の学習失敗理由です: {trainingResult.FailureKind}")
                    };

                    // 260902Codex: 読込関門で止まった一時フォルダを消し、provenance・metadata・正式昇格へ進みません。
                    string? preservedArtifactsFolder = TryDeleteTemporaryFolder(temporaryOutputFolder)
                        ? null
                        : temporaryOutputFolder;
                    return ModelTrainingResult.CreateNotPromoted(
                        failureKind,
                        failureReason,
                        preservedArtifactsFolder);
                }
                if (trainingResult.Status != DeepLearningTrainingStatus.Completed)
                    throw new InvalidOperationException($"未対応の学習結果です: {trainingResult.Status}");

                // 260901Codex: 由来情報も一時モデルの一部とし、正式な昇格前に保存を成功させる。
                if (plan.TrainingDataProvenance is not null)
                    TrainingDataProvenanceWriter.Write(
                        temporaryOutputFolder,
                        plan.TrainingDataProvenance,
                        plan.TrainingPools);
                // 260906Codex: 観測した検証結果を一時モデルに保持し、不完全な実行を結果として昇格させない。
                TrainingResultsWriter.Write(
                    temporaryOutputFolder,
                    new TrainingResultsSpecification(
                        plan.Settings.Epochs,
                        plan.Settings.BatchSize,
                        plan.Settings.EarlyStoppingPatience,
                        plan.Settings.ValidationSplit,
                        // 261021Codex: 公開版の既存引数順と保存形式を変えず、学習率を名前で渡す。
                        ClassificationLearningRate: plan.Settings.LearningRate,
                        SplitSeed: DeepLearningDataSplitter.DefaultRandomState,
                        EarlyStoppingMonitor: DeepLearning.EarlyStoppingMonitor),
                    trainingResult.ModelMetrics);
                WriteDetectorMetadata(plan.DetectorProfile, temporaryOutputFolder);

                // 260928Codex: headの学習と再読込検証が終わってから、分類＋OCNモデルを公開する。
                if (headDatasetFolder is not null && ocnFailureReason is null)
                {
                    ClassificationTrainingDatasetInfo frozen = ClassificationTrainingDataset.ReadInfo(headDatasetFolder, cancellationToken);
                    foreach (ClassificationTrainingSampleInfo sample in frozen.Samples)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (sample.SourceSha256 != ClassificationTrainingDataset.ComputeFileSha256(sample.FilePath, cancellationToken))
                            throw new IOException($"分類＋OCNの学習中に入力が変更されました: {sample.FilePath}");
                    }
                    // 261001Codex: 上限は共通とし、中止時は要求した処理全体を中断する。
                    try
                    {
                        SoftmaxOcnHeadTraining.TrainAndSave(temporaryOutputFolder, headDatasetFolder,
                            plan.SoftmaxOcnSeed!.Value, "ModelCreation", cancellationToken, _logAction,
                            plan.Settings.Epochs, plan.Settings.EarlyStoppingPatience, trainingResult.ClassifierFeatures);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException and not TrainingInputChangedException)
                    {
                        ocnFailureReason = exception.Message;
                        File.Delete(Path.Combine(ModelArtifactPaths.GetClassificationFolder(temporaryOutputFolder), SoftmaxOcnHeadArtifact.FileName));
                        _logAction($"OCN判定器を作成できませんでした: {ocnFailureReason}");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                PromoteTemporaryFolder(temporaryOutputFolder, plan.ModelOutputFolder, plan.AllowOverwriteExistingModel);
                // 260930Codex: 分類器と利用可能な定量モデルを正式に保存してから通知する。
                return ModelTrainingResult.CreatePromoted(trainingResult.ModelMetrics, ocnFailureReason);
            }
            catch (Exception exception)
            {
                // 260827Claude: 予期しない失敗は型とスタックを学習ログへ残す。UI へ返るのは Message だけで、
                //   ログ TextBox も保存されないため、これが後から原因を追える唯一の記録になる。
                //   キャンセルは正常な終わり方なので記録しない。
                if (exception is not OperationCanceledException)
                    TensorFlowTrainingDebugLog.Write(
                        "training-run-failed",
                        $"path={TensorFlowTrainingDebugLog.Clean(plan.ModelOutputFolder)} type={exception.GetType().FullName} detail={TensorFlowTrainingDebugLog.Clean(exception.ToString())}");

                // 260514Codex: cleanup 失敗でキャンセル例外を隠さないよう、削除エラーはログに残して元の例外を戻します。
                TryDeleteTemporaryFolder(temporaryOutputFolder);

                throw;
            }
            finally
            {
                if (headDatasetFolder is not null)
                    TryDeleteTemporaryFolder(headDatasetFolder);
            }
        }

        // 260930Claude: ウイルス対策のスキャンや削除保留のハンドルは短時間で解消するため、この待ち時間で足ります。
        private static readonly int[] FolderDeleteRetryDelaysMilliseconds = [100, 200, 400];

        // 260902Codex: 想定内の未昇格でも例外でも、cleanup失敗が本来の結果を上書きしないよう共通化します。
        // 260930Claude: 一時的なロックは間隔を空けて再試行します。中断要求は見ません。昇格後の後片付けで
        //   中断例外を投げると、すでに成功した公開が中止されたように見えるためです。
        private bool TryDeleteTemporaryFolder(string folder)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    return true;
                }
                catch (DirectoryNotFoundException)
                {
                    return true;
                }
                // 260930Claude: 途中まで消えたフォルダを再試行しても、残りを消すだけなので不整合は起きません。
                catch (Exception retryable) when (retryable is IOException or UnauthorizedAccessException
                    && attempt < FolderDeleteRetryDelaysMilliseconds.Length)
                {
                    Thread.Sleep(FolderDeleteRetryDelaysMilliseconds[attempt]);
                }
                catch (Exception cleanupException)
                {
                    _logAction($"不要なフォルダを削除できませんでした。手動で削除してください: {folder}");
                    _logAction(cleanupException.Message);
                    return false;
                }
            }
        }

        // 260626Codex: 学習スペクトルを後の予測から追跡できるよう、学習成果物とともに検出器プロファイルを記録する。
        private static void WriteDetectorMetadata(DetectorProfile detectorProfile, string modelRootFolder)
        {
            detectorProfile.WriteToModelFolder(modelRootFolder);
            foreach (string modelFolder in Directory.EnumerateDirectories(modelRootFolder))
            {
                if (File.Exists(Path.Combine(modelFolder, ModelArtifactPaths.ModelTypeFileName)))
                    detectorProfile.WriteToModelFolder(modelFolder);
            }
        }

        // 260514Codex: 既存の正式フォルダがある場合も、成功した tmp を置く直前まで退避して失敗時に戻せるようにします。
        private void PromoteTemporaryFolder(string temporaryOutputFolder, string modelOutputFolder, bool allowOverwriteExistingModel)
        {
            _logAction("モデル保存先の仮フォルダを正式フォルダへ昇格します。");

            // 260917Codex: .previousで終わる別名のモデルは、この実行で削除してよいバックアップではない。
            string backupOutputFolder = $"{modelOutputFolder}.{Guid.NewGuid():N}.previous";

            if (Directory.Exists(modelOutputFolder))
            {
                // 260619Codex: 昇格時にも再確認し、後から作られたフォルダーを暗黙に上書きしない。
                if (!allowOverwriteExistingModel)
                    throw new InvalidOperationException(OverwriteNotAllowedMessage);

                _logAction($"既存モデルを上書きします: {modelOutputFolder}");
                Directory.Move(modelOutputFolder, backupOutputFolder);
            }

            // 260930Claude: 公開の確定だけをロールバックの対象にします。以前は退避先の削除も同じ try にあり、
            //   削除が途中で失敗すると catch が昇格済みの新モデルを消して、部分削除された旧モデルを戻していました。
            try
            {
                Directory.Move(temporaryOutputFolder, modelOutputFolder);
            }
            catch
            {
                // 260930Claude: 退避先の名前は毎回の GUID を持つので、存在すればこの呼び出しが作ったものです。
                if (Directory.Exists(backupOutputFolder))
                    TryRestoreBackup(backupOutputFolder, modelOutputFolder);

                throw;
            }

            _logAction($"モデル保存先を昇格しました: {modelOutputFolder}");
            // 260930Claude: 公開は確定済みなので、ここから先の失敗を公開の失敗へ変えません。
            //   今回の退避先と、以前の実行が消しきれなかった同名の退避先をまとめて片付けます。
            DeleteAbandonedBackups(modelOutputFolder);
        }

        // 260930Claude: 昇格に失敗したときだけ呼ぶ復旧。復旧そのものの失敗で元の失敗原因を隠さないよう、
        //   ここでは退避先を記録するに留め、呼び出し元が元の例外を投げ直します。
        private void TryRestoreBackup(string backupOutputFolder, string modelOutputFolder)
        {
            try
            {
                if (Directory.Exists(modelOutputFolder))
                    Directory.Delete(modelOutputFolder, recursive: true);

                Directory.Move(backupOutputFolder, modelOutputFolder);
            }
            catch (Exception restoreException)
            {
                _logAction($"既存モデルを復旧できませんでした。退避先を確認してください: {backupOutputFolder}");
                _logAction(restoreException.Message);
            }
        }

        // 260930Claude: 昇格が確定した後の後片付け。呼び出し元へ例外を伝えず、残った場所だけを記録します。
        //   対象を同じモデル名の退避先に限るので、ModelOutput の path lease が他プロセスとの競合を防ぎます。
        private void DeleteAbandonedBackups(string modelOutputFolder)
        {
            try
            {
                string fullPath = Path.GetFullPath(modelOutputFolder);
                string modelName = Path.GetFileName(fullPath);
                string? parentFolder = Path.GetDirectoryName(fullPath);
                if (modelName.Length == 0 || parentFolder is null)
                    return;

                foreach (string folder in Directory.EnumerateDirectories(parentFolder, $"{modelName}.*"))
                {
                    if (IsPromotionBackupName(Path.GetFileName(folder), modelName))
                        TryDeleteTemporaryFolder(folder);
                }
            }
            catch (Exception cleanupException)
            {
                _logAction($"退避フォルダの後片付けに失敗しました: {cleanupException.Message}");
            }
        }

        // 260930Claude: PromoteTemporaryFolder が作る `{モデル名}.{GUID}.previous` だけを片付けます。
        //   GUID を要求するので、利用者が名付けた `sample.previous` は残ります。判定は ModelCatalog.IsTrainingWorkFolder と
        //   同じ綴りにして、片方だけが work folder とみなす状態を作りません。`.tmp` はモデル名を含まないため対象外です。
        private static bool IsPromotionBackupName(string folderName, string modelName) =>
            // 260930Codex: モデル名全体を照合し、modelの削除処理でmodel.childのバックアップを消さないようにする。
            folderName.Split('.') is [.., var id, "previous"] && Guid.TryParseExact(id, "N", out _)
            && string.Equals(folderName, $"{modelName}.{id}.previous", StringComparison.OrdinalIgnoreCase);
    }
}
