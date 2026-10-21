using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
// 260908Codex: 独立したスペクトル読取だけを並列化し、pool選択とmanifest修復は順序を保つ。
using System.Threading.Tasks;

namespace MineraScope
{
    // 260901Codex: 1鉱物に複数の独立した不足があるため、ライブタイムと条件の識別情報を含める。
    internal sealed record SpectrumPoolShortage(
        string MineralName,
        int RequiredCount,
        int CompletedCount,
        double LiveTime,
        string ConditionKey)
    {
        public int MissingCount => Math.Max(0, RequiredCount - CompletedCount);
    }

    // 260901Codex: 不正・欠損した予約を変えずに、Pending復旧の結果を明示する。
    internal sealed record PendingSpectrumRecoveryResult(
        int ExaminedCount,
        int RecoveredCount,
        int InvalidCount,
        int MissingFileCount);

    // 260902Codex: UIスレッド外でmanifestとEMSA項目を検査する間、pool準備の進捗を表示する。
    internal sealed record SpectrumPoolPreparationProgress(
        string Phase,
        int PoolNumber,
        int TotalPoolCount,
        int CompletedPoolCount,
        string MineralName,
        double LiveTime,
        int FilesChecked,
        int FilesToCheck,
        bool PoolCompleted,
        // 260907Codex: 候補検査はEMSA読込ではないため、準備表示の単位を明示する。
        string ItemName = "EMSA");

    // 260907Codex: 復旧と正確なサンプル選択を、範囲を限定した1回の準備操作にまとめる。
    internal sealed record SpectrumTrainingPreparationResult(
        PendingSpectrumRecoveryResult Recovery,
        IReadOnlyList<SpectrumTrainingPool> Pools,
        IReadOnlyList<SpectrumPoolShortage> Shortages);

    // 260513Codex: シミュレーション後の manifest 状態をログへ出すため、学習対象外 status も集計します。
    internal sealed record SpectrumPoolStatusCounts(
        int Completed,
        int Failed,
        int Missing,
        int Pending);

    // 260507Codex: pool manifest を正本として、不足確認・予約・学習入力作成を担当します。
    internal sealed class SpectrumPoolWorkflow
    {
        private readonly SpectrumPoolRepository _repository;
        private readonly SimulationPlanBuilder _simulationPlanBuilder;
        // 260803Codex: ベースライン採取では修復結果をメモリ上だけに反映し、正本の manifest を変更しません。
        private readonly bool _persistManifestRepairs;
        private readonly Random _random = new();

        // 260623Claude: 学習データ選抜を再現可能にするための固定 seed。コードベース他所の split/seed と同じ 42 に合わせる。
        private const int TrainingSelectionSeed = 42;

        public SpectrumPoolWorkflow(
            SpectrumPoolRepository repository,
            SimulationPlanBuilder simulationPlanBuilder,
            // 260803Codex: 通常経路の既存挙動は維持し、読み取り専用の計測経路だけ false を指定します。
            bool persistManifestRepairs = true)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _simulationPlanBuilder = simulationPlanBuilder ?? throw new ArgumentNullException(nameof(simulationPlanBuilder));
            // 260803Codex: LoadState が検出した正規化・欠損状態を書き戻すかを workflow 単位で固定します。
            _persistManifestRepairs = persistManifestRepairs;
        }

        // 260901Codex: 従来の単一時間経路を保持しつつ、有効な各時間割当を別々に集計する。
        public IReadOnlyList<SpectrumPoolShortage> GetShortages(
            ModelCreationRequest request,
            bool validateCompletedSpectra = true)
        {
            if (request.UsesCompositionTimePlan)
                return GetCompositionTimePlanShortages(request, validateCompletedSpectra);

            var shortages = new List<SpectrumPoolShortage>();

            foreach (var allocation in request.GetEffectiveSpectrumTimeAllocations())
            {
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                foreach (var solution in request.SelectedMineralSolutions)
                {
                    var state = LoadState(allocationRequest, solution, validateCompletedSpectra);
                    int completedCount = state.CompletedEntries.Count;
                    if (completedCount < allocation.TargetSpectrumCount)
                        shortages.Add(new SpectrumPoolShortage(
                            solution.Name,
                            allocation.TargetSpectrumCount,
                            completedCount,
                            allocation.LiveTime,
                            state.Handle.ConditionKey));
                }
            }

            return shortages;
        }

        // 260901Codex: 試行確認のプレビューは不足行だけを返し、ファイルシステムへの全アクセスを読み取り専用にする。
        public IReadOnlyList<SpectrumPoolShortage> PreviewShortages(ModelCreationRequest request) =>
            PreviewPoolStates(request)
                .Where(item => item.MissingCount > 0)
                .ToArray();

        // 260901Codex: 充足済みの行も含め、非対話出力で20秒・50秒poolの実際の再利用可能件数を示す。
        public IReadOnlyList<SpectrumPoolShortage> PreviewPoolStates(ModelCreationRequest request)
        {
            if (request.UsesCompositionTimePlan)
                return PreviewCompositionTimePlanStates(request);

            var poolStates = new List<SpectrumPoolShortage>();

            foreach (var allocation in request.GetEffectiveSpectrumTimeAllocations())
            {
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                foreach (var solution in request.SelectedMineralSolutions)
                {
                    var handle = _repository.ResolvePool(
                        allocationRequest.Paths.SpectrumOutputFolder,
                        solution,
                        allocationRequest.Simulation.ResolutionStep,
                        allocationRequest.SemEdxCondition);
                    var manifest = _repository.Load(handle.ManifestPath);
                    int completedCount = manifest?.Spectra.Count(entry =>
                        entry.Status == SpectrumManifestStatus.Completed
                        && EmsaSpectrumIntegrityValidator.TryValidate(
                            Path.Combine(handle.PoolFolder, entry.FileName),
                            handle.Condition.SemEdxCondition,
                            out _)) ?? 0;
                    poolStates.Add(new SpectrumPoolShortage(
                        solution.Name,
                        allocation.TargetSpectrumCount,
                        completedCount,
                        allocation.LiveTime,
                        handle.ConditionKey));
                }
            }

            return poolStates;
        }

        // 260901Codex: 本番のジョブ割当で作業量をプレビューし、予約の副作用は生じさせない。
        public SimulationExecutionPlanPreview PreviewMissingSimulationPlan(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            shortages = PreviewShortages(request);
            return _simulationPlanBuilder.CreatePreview(request, shortages);
        }

        // 260901Codex: 有効な全測定時間のpoolで、状態件数を集計する。
        public SpectrumPoolStatusCounts GetStatusCounts(
            ModelCreationRequest request,
            bool validateCompletedSpectra = true)
        {
            if (request.UsesCompositionTimePlan)
                return GetCompositionTimePlanStatusCounts(request, validateCompletedSpectra);

            int completed = 0;
            int failed = 0;
            int missing = 0;
            int pending = 0;

            foreach (var allocation in request.GetEffectiveSpectrumTimeAllocations())
            {
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                foreach (var solution in request.SelectedMineralSolutions)
                {
                    var state = LoadState(allocationRequest, solution, validateCompletedSpectra);
                    foreach (var entry in state.Manifest.Spectra)
                    {
                        switch (entry.Status)
                        {
                            case SpectrumManifestStatus.Completed:
                                completed++;
                                break;
                            case SpectrumManifestStatus.Failed:
                                failed++;
                                break;
                            case SpectrumManifestStatus.Missing:
                                missing++;
                                break;
                            case SpectrumManifestStatus.Pending:
                                pending++;
                                break;
                        }
                    }
                }
            }

            return new SpectrumPoolStatusCounts(completed, failed, missing, pending);
        }

        // 260907Codex: Bの状態は予定行だけで報告し、保存した旧スペクトルのために完了計画が失敗・未完了に見えないようにする。
        private SpectrumPoolStatusCounts GetCompositionTimePlanStatusCounts(
            ModelCreationRequest request,
            bool validateCompletedSpectra)
        {
            int completed = 0;
            int failed = 0;
            int missing = 0;
            int pending = 0;
            foreach (var solution in request.SelectedMineralSolutions)
            {
                var state = BuildCompositionTimePlanState(request, solution, validateCompletedSpectra, persistPlan: true);
                foreach (var entry in BuildPlanBindings(solution, state, includeLegacyCompleted: false, createMissingEntries: false)
                    .Select(item => item.Entry)
                    .Where(entry => entry is not null)
                    .Cast<SpectrumManifestEntry>())
                {
                    switch (entry.Status)
                    {
                        case SpectrumManifestStatus.Completed:
                            completed++;
                            break;
                        case SpectrumManifestStatus.Failed:
                            failed++;
                            break;
                        case SpectrumManifestStatus.Missing:
                            missing++;
                            break;
                        case SpectrumManifestStatus.Pending:
                            pending++;
                            break;
                    }
                }
            }

            return new SpectrumPoolStatusCounts(completed, failed, missing, pending);
        }

        // 260907Codex: 呼び出し側がpool変更用利用権を保持する。Bは1鉱物ずつ復旧・選択間でmanifest読込を共有し、キャッシュメモリを抑える。
        public SpectrumTrainingPreparationResult PrepareTrainingPools(
            ModelCreationRequest request,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default,
            Action<string>? log = null,
            // 260908Codex: 実験では既存の準備呼び出しを変えず、EMSAの同時読込数を制限できる。
            int validationParallelism = 1)
        {
            // 260908Codex: poolを開く・修復する前に、不正な上限を拒否する。
            ArgumentOutOfRangeException.ThrowIfLessThan(validationParallelism, 1);
            var timer = Stopwatch.StartNew();
            var recovery = new PendingSpectrumRecoveryResult(0, 0, 0, 0);
            var pools = new List<SpectrumTrainingPool>();
            var shortages = new List<SpectrumPoolShortage>();
            int manifestReads = 0;
            int manifestCacheHits = 0;
            int poolOffset = 0;
            int timeCount = request.GetEffectiveSpectrumTimeAllocations().Count;
            int totalPoolCount = request.SelectedMineralSolutions.Count * timeCount;
            // 260907Codex: 旧形式の選択は鉱物間で1つのRandomを共用し、その経路を分割・再初期化しない。
            IEnumerable<ModelCreationRequest> batches = request.UsesCompositionTimePlan
                ? request.SelectedMineralSolutions.Select(solution => request with { SelectedMineralSolutions = [solution] })
                : [request];
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repository = _repository.CreatePreparationRepository(cancellationToken);
                var workflow = new SpectrumPoolWorkflow(repository, _simulationPlanBuilder, _persistManifestRepairs);
                var batchProgress = new OffsetPreparationProgress(progress, poolOffset, totalPoolCount);
                var recoveryTimer = Stopwatch.StartNew();
                var recovered = workflow.RecoverPendingSpectra(batch, batchProgress, cancellationToken);
                // 260917Codex: 読み取り専用の準備では、生成側の保留出力を復旧しない。
                if (_persistManifestRepairs)
                    log?.Invoke($"学習準備 Pending回収: {string.Join(", ", batch.SelectedMineralSolutions.Select(item => item.Name))}, {recoveryTimer.Elapsed.TotalSeconds:F2}秒");
                recovery = new PendingSpectrumRecoveryResult(
                    recovery.ExaminedCount + recovered.ExaminedCount,
                    recovery.RecoveredCount + recovered.RecoveredCount,
                    recovery.InvalidCount + recovered.InvalidCount,
                    recovery.MissingFileCount + recovered.MissingFileCount);
                // 260908Codex: 読込上限を、鉱物ごとの準備スコープへ伝える。
                pools.AddRange(workflow.CreateTrainingPools(batch, out var batchShortages, batchProgress, cancellationToken, log, validationParallelism));
                shortages.AddRange(batchShortages);
                manifestReads += repository.ManifestReadCount;
                manifestCacheHits += repository.ManifestCacheHitCount;
                poolOffset += batch.SelectedMineralSolutions.Count * timeCount;
            }

            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke($"学習準備完了: {timer.Elapsed.TotalSeconds:F2}秒, manifest読込 {manifestReads}回 / 再利用 {manifestCacheHits}回");
            return new SpectrumTrainingPreparationResult(recovery, shortages.Count == 0 ? pools : [], shortages);
        }

        // 260907Codex: 鉱物別件数を同期的に変換し、UIスレッドへの配送は呼び出し側のIProgressに任せる。
        private sealed class OffsetPreparationProgress(
            IProgress<SpectrumPoolPreparationProgress>? progress,
            int poolOffset,
            int totalPoolCount) : IProgress<SpectrumPoolPreparationProgress>
        {
            public void Report(SpectrumPoolPreparationProgress value) => progress?.Report(value with
            {
                PoolNumber = value.PoolNumber + poolOffset,
                CompletedPoolCount = value.CompletedPoolCount + poolOffset,
                TotalPoolCount = totalPoolCount
            });
        }

        // 260901Codex: 各時間poolから目標数を正確に選び、明示予定表に元メタデータと組成の層化情報を持たせる。
        public IReadOnlyList<SpectrumTrainingPool> CreateTrainingPools(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default,
            // 260907Codex: 工程時間には注入したロガーを使い、非対話・テストの呼び出し側にログ書込を強制しない。
            Action<string>? log = null,
            // 260908Codex: 既存の生成・学習経路向けの公開既定値は、逐次検証のままにする。
            int validationParallelism = 1)
        {
            // 260908Codex: 計画あり・旧形式の両経路で、同じ選択と修復の順序を保持する。
            ArgumentOutOfRangeException.ThrowIfLessThan(validationParallelism, 1);
            if (request.UsesCompositionTimePlan)
                return CreateCompositionTimeTrainingPools(request, out shortages, progress, cancellationToken, log, validationParallelism);

            var pools = new List<SpectrumTrainingPool>(request.SelectedMineralSolutions.Count);
            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            bool hasExplicitAllocations = request.SpectrumTimeAllocations.Count > 0;
            int totalPoolCount = request.SelectedMineralSolutions.Count * allocations.Count;
            int poolNumber = 0;
            var preparedStates = new List<TrainingPoolSelectionState>(
                request.SelectedMineralSolutions.Count * allocations.Count);
            var shortageList = new List<SpectrumPoolShortage>();

            // 260901Codex: 各poolを一度だけ読込・検証し、選択前の2回目の全EMSA走査を避ける。
            foreach (var solution in request.SelectedMineralSolutions)
            {
                foreach (var allocation in allocations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    poolNumber++;
                    var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                    ReportPreparationProgress(
                        progress,
                        "学習pool確認",
                        poolNumber,
                        totalPoolCount,
                        poolNumber - 1,
                        solution.Name,
                        allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: false);
                    var state = LoadState(
                        allocationRequest,
                        solution,
                        validateCompletedSpectra: true,
                        onCompletedSpectrumChecked: (checkedCount, totalCount) =>
                        {
                            if (checkedCount == totalCount || checkedCount % 128 == 0)
                                ReportPreparationProgress(
                                    progress,
                                    "学習pool確認",
                                    poolNumber,
                                    totalPoolCount,
                                    poolNumber - 1,
                                    solution.Name,
                                    allocation.LiveTime,
                                    checkedCount,
                                    totalCount,
                                    poolCompleted: false);
                        },
                        // 260908Codex: 読み込んだmanifestはこのスレッドに保持し、ファイル検査だけを並行実行する。
                        cancellationToken: cancellationToken, validationParallelism: validationParallelism);
                    preparedStates.Add(new TrainingPoolSelectionState(solution, allocation, state));
                    if (state.CompletedEntries.Count < allocation.TargetSpectrumCount)
                        shortageList.Add(new SpectrumPoolShortage(
                            solution.Name,
                            allocation.TargetSpectrumCount,
                            state.CompletedEntries.Count,
                            allocation.LiveTime,
                            state.Handle.ConditionKey));
                    ReportPreparationProgress(
                        progress,
                        "学習pool確認",
                        poolNumber,
                        totalPoolCount,
                        poolNumber,
                        solution.Name,
                        allocation.LiveTime,
                        state.Manifest.Spectra.Count(entry => entry.Status == SpectrumManifestStatus.Completed),
                        state.Manifest.Spectra.Count(entry => entry.Status == SpectrumManifestStatus.Completed),
                        poolCompleted: true);
                }
            }

            if (shortageList.Count > 0)
            {
                shortages = shortageList;
                return [];
            }

            shortages = [];

            // 260623Claude: 学習データの選抜・並び順は固定 seed の専用乱数で行い、再学習のたびに train/test が変わって結果がばらつくのを防ぐ。
            //               生成側の _random とは分け、呼び出しごとに seed=42 から作り直す (フィールドにすると同一セッションの2回目で選抜がずれる)。
            var selectionRandom = new Random(TrainingSelectionSeed);

            foreach (var solution in request.SelectedMineralSolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = new List<SpectrumTrainingSample>(allocations.Sum(item => item.TargetSpectrumCount));
                foreach (var preparedState in preparedStates.Where(item => ReferenceEquals(item.Solution, solution)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var allocation = preparedState.Allocation;
                    var state = preparedState.State;
                    var candidates = state.CompletedEntries
                        .Select(entry => new SpectrumTrainingSample(
                            Path.Combine(state.Handle.PoolFolder, entry.FileName),
                            entry.EndmemberFractions,
                            hasExplicitAllocations
                                ? new SpectrumTrainingSource(
                                    allocation.LiveTime,
                                    state.Handle.ConditionKey,
                                    entry.SimulationId,
                                    state.Handle.ManifestPath)
                                : null))
                        .ToArray();
                    // 260901Codex: 最初の不足確認と選択の間にファイルが消えた場合、例外でなく不足として返す。
                    if (candidates.Length < allocation.TargetSpectrumCount)
                    {
                        shortages =
                        [
                            new SpectrumPoolShortage(
                                solution.Name,
                                allocation.TargetSpectrumCount,
                                candidates.Length,
                                allocation.LiveTime,
                                state.Handle.ConditionKey)
                        ];
                        return [];
                    }

                    IReadOnlyList<SpectrumTrainingSample> selected = hasExplicitAllocations
                        ? SpectrumTrainingSampleSelector.SelectCompositionStratified(
                            candidates,
                            allocation.TargetSpectrumCount,
                            TrainingSelectionSeed)
                        : candidates
                            .OrderBy(_ => selectionRandom.Next())
                            .Take(allocation.TargetSpectrumCount)
                            .ToArray();
                    if (selected.Count != allocation.TargetSpectrumCount)
                    {
                        shortages =
                        [
                            new SpectrumPoolShortage(
                                solution.Name,
                                allocation.TargetSpectrumCount,
                                selected.Count,
                                allocation.LiveTime,
                                state.Handle.ConditionKey)
                        ];
                        return [];
                    }

                    samples.AddRange(selected);
                }

                pools.Add(new SpectrumTrainingPool(
                    solution.Name,
                    solution.Members.Select(member => member.Name).ToArray(),
                    samples));
            }

            return pools;
        }

        // 260901Codex: 通常の条件キーmanifestへ時間ごとに予約し、鉱物ごとに制限付きバッチを1つ作る。
        public SimulationExecutionPlan CreateMissingSimulationPlan(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool validateCompletedSpectra = true)
        {
            if (request.UsesCompositionTimePlan)
                return CreateCompositionTimeSimulationPlan(
                    request,
                    out shortages,
                    progress,
                    cancellationToken,
                    validateCompletedSpectra);

            var shortageList = new List<SpectrumPoolShortage>();
            var reservations = new List<SpectrumSimulationReservation>();
            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            int totalPoolCount = request.SelectedMineralSolutions.Count * allocations.Count;
            int poolNumber = 0;

            foreach (var allocation in allocations)
            {
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                foreach (var solution in request.SelectedMineralSolutions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    poolNumber++;
                    ReportPreparationProgress(
                        progress,
                        "生成計画作成",
                        poolNumber,
                        totalPoolCount,
                        poolNumber - 1,
                        solution.Name,
                        allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: false);
                    // 260902Codex: GUIはここでfalseを渡し、再開時はファイルの存在だけを確認する。
                    // 既定値を使う呼び出し側には、従来の完全なEMSA完全性検証を適用する。
                    var state = LoadState(
                        allocationRequest,
                        solution,
                        validateCompletedSpectra,
                        cancellationToken: cancellationToken);
                    int missingCount = Math.Max(0, allocation.TargetSpectrumCount - state.CompletedEntries.Count);
                    if (missingCount == 0)
                    {
                        ReportPreparationProgress(
                            progress,
                            "生成計画作成",
                            poolNumber,
                            totalPoolCount,
                            poolNumber,
                            solution.Name,
                            allocation.LiveTime,
                            0,
                            0,
                            poolCompleted: true);
                        continue;
                    }

                    shortageList.Add(new SpectrumPoolShortage(
                        solution.Name,
                        allocation.TargetSpectrumCount,
                        state.CompletedEntries.Count,
                        allocation.LiveTime,
                        state.Handle.ConditionKey));
                    reservations.AddRange(ReserveMissingSpectra(
                        solution,
                        state,
                        allocationRequest.Simulation.ResolutionStep,
                        allocationRequest.SemEdxCondition,
                        allocation.TargetSpectrumCount,
                        missingCount));
                    _repository.Save(state.Handle, state.Manifest);
                    ReportPreparationProgress(
                        progress,
                        "生成計画作成",
                        poolNumber,
                        totalPoolCount,
                        poolNumber,
                        solution.Name,
                        allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: true);
                }
            }

            shortages = shortageList;
            return _simulationPlanBuilder.CreatePlan(request, reservations);
        }

        // 260507Codex: DTSA-II 実行結果を予約 entry へ反映し、Completed でも読めないファイルは Missing にします。
        public void ApplySimulationResults(IReadOnlyList<SimulationExecutionResult> results)
        {
            // 260508Codex: 一度だけ使う展開 helper を避け、結果と予約の対応をここで直接作ります。
            var resultReservations = results.SelectMany(result =>
                result.Reservations.Select(reservation => (Result: result, Reservation: reservation)));

            foreach (var group in resultReservations.GroupBy(item => item.Reservation.ManifestPath))
            {
                var manifest = _repository.Load(group.Key);
                if (manifest is null)
                    continue;

                foreach (var item in group)
                {
                    var entry = manifest.Spectra.FirstOrDefault(spectrum => spectrum.SimulationId == item.Reservation.SimulationId);
                    if (entry is null)
                        continue;

                    ApplyResultToEntry(item.Result, item.Reservation, entry);
                }

                _repository.Save(group.Key, manifest);
            }
        }

        // 260901Codex: DTSA-IIが稼働していない時だけ呼ぶ。有効な完成ファイルを復旧し、不正・欠損のPending項目は再試行可能に残す。
        public PendingSpectrumRecoveryResult RecoverPendingSpectra(
            ModelCreationRequest request,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // 260917Codex: 構文上完成したPendingファイルでも、並行する生成側が所有している場合がある。
            // 復旧はシミュレーションだけが行い、読み取り側はCompleted状態の公開を待つ。
            if (!_persistManifestRepairs)
                return new PendingSpectrumRecoveryResult(0, 0, 0, 0);

            int examinedCount = 0;
            int recoveredCount = 0;
            int invalidCount = 0;
            int missingFileCount = 0;
            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            int totalPoolCount = request.SelectedMineralSolutions.Count * allocations.Count;
            int poolNumber = 0;

            foreach (var allocation in allocations)
            {
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                foreach (var solution in request.SelectedMineralSolutions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    poolNumber++;
                    ReportPreparationProgress(
                        progress,
                        "Pending回収",
                        poolNumber,
                        totalPoolCount,
                        poolNumber - 1,
                        solution.Name,
                        allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: false);
                    var handle = _repository.ResolvePool(
                        allocationRequest.Paths.SpectrumOutputFolder,
                        solution,
                        allocationRequest.Simulation.ResolutionStep,
                        allocationRequest.SemEdxCondition);
                    var manifest = _repository.Load(handle.ManifestPath);
                    if (manifest is null)
                    {
                        ReportPreparationProgress(
                            progress,
                            "Pending回収",
                            poolNumber,
                            totalPoolCount,
                            poolNumber,
                            solution.Name,
                            allocation.LiveTime,
                            0,
                            0,
                            poolCompleted: true);
                        continue;
                    }

                    bool changed = false;
                    var pendingEntries = manifest.Spectra
                        .Where(item => item.Status == SpectrumManifestStatus.Pending)
                        .ToArray();
                    for (int pendingIndex = 0; pendingIndex < pendingEntries.Length; pendingIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = pendingEntries[pendingIndex];
                        examinedCount++;
                        string spectrumPath = Path.Combine(handle.PoolFolder, entry.FileName);
                        if (!File.Exists(spectrumPath))
                            missingFileCount++;
                        else if (!EmsaSpectrumIntegrityValidator.TryValidate(
                            spectrumPath,
                            handle.Condition.SemEdxCondition,
                            out _))
                            invalidCount++;
                        else
                        {
                            entry.Status = SpectrumManifestStatus.Completed;
                            entry.FailureReason = null;
                            entry.LastFailureKind = null;
                            recoveredCount++;
                            changed = true;
                        }

                        int filesChecked = pendingIndex + 1;
                        if (filesChecked == pendingEntries.Length || filesChecked % 128 == 0)
                            ReportPreparationProgress(
                                progress,
                                "Pending回収",
                                poolNumber,
                                totalPoolCount,
                                poolNumber - 1,
                                solution.Name,
                                allocation.LiveTime,
                                filesChecked,
                                pendingEntries.Length,
                                poolCompleted: false);
                    }

                    // 260907Codex: 復旧した完了状態を保存する。
                    // 260917Codex: 読み取り専用の呼び出しは復旧前に戻るため、この分岐にはシミュレーションの書き込み側だけが到達する。
                    if (changed)
                        _repository.Save(handle, manifest);

                    ReportPreparationProgress(
                        progress,
                        "Pending回収",
                        poolNumber,
                        totalPoolCount,
                        poolNumber,
                        solution.Name,
                        allocation.LiveTime,
                        pendingEntries.Length,
                        pendingEntries.Length,
                        poolCompleted: true);
                }
            }

            return new PendingSpectrumRecoveryResult(
                examinedCount,
                recoveredCount,
                invalidCount,
                missingFileCount);
        }

        // 260907Codex: 読み取り専用プレビューでも同じ決定的計画を求めるが、計画ファイル・予約・manifestタグは作らない。
        private IReadOnlyList<SpectrumPoolShortage> PreviewCompositionTimePlanStates(ModelCreationRequest request) =>
            CollectCompositionTimePlanShortages(
                request,
                validateCompletedSpectra: true,
                persistPlan: false,
                includeLegacyCompleted: true);

        // 260907Codex: 計画予約後の不足を満たすのは計画タグ付きCompleted行だけとし、旧行は予約時に取り込み、学習時に暗黙に採用しない。
        private IReadOnlyList<SpectrumPoolShortage> GetCompositionTimePlanShortages(
            ModelCreationRequest request,
            bool validateCompletedSpectra)
            => CollectCompositionTimePlanShortages(
                request,
                validateCompletedSpectra,
                persistPlan: true,
                includeLegacyCompleted: false)
                // 260907Codex: 従来の契約に合わせ、完了計画は不足0の5行ではなく、不足行なしとする。
                .Where(item => item.MissingCount > 0)
                .ToArray();

        // 260907Codex: プレビューでは予約前に一致する旧行を数えられるが、以後の状態・学習検証では保存計画へ結び付いた行を必須にする。
        private IReadOnlyList<SpectrumPoolShortage> CollectCompositionTimePlanShortages(
            ModelCreationRequest request,
            bool validateCompletedSpectra,
            bool persistPlan,
            bool includeLegacyCompleted)
        {
            var shortages = new List<SpectrumPoolShortage>();
            foreach (var solution in request.SelectedMineralSolutions)
            {
                var state = BuildCompositionTimePlanState(request, solution, validateCompletedSpectra, persistPlan);
                var bindings = BuildPlanBindings(solution, state, includeLegacyCompleted, createMissingEntries: false);
                foreach (var pool in state.Pools)
                {
                    int completed = bindings.Count(item => item.Pool == pool && item.Entry?.Status == SpectrumManifestStatus.Completed);
                    shortages.Add(new SpectrumPoolShortage(
                        solution.Name,
                        pool.Allocation.TargetSpectrumCount,
                        completed,
                        pool.Allocation.LiveTime,
                        pool.State.Handle.ConditionKey));
                }
            }

            return shortages;
        }

        // 260907Codex: 欠損予定行を各1回予約し、既存Completed行は組成と物理poolが予定行と完全一致する場合だけ採用する。
        private SimulationExecutionPlan CreateCompositionTimeSimulationPlan(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress,
            CancellationToken cancellationToken,
            bool validateCompletedSpectra)
        {
            var shortageList = new List<SpectrumPoolShortage>();
            var reservations = new List<SpectrumSimulationReservation>();
            int totalPoolCount = request.SelectedMineralSolutions.Count * request.GetEffectiveSpectrumTimeAllocations().Count;
            int poolNumber = 0;

            foreach (var solution in request.SelectedMineralSolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = BuildCompositionTimePlanState(request, solution, validateCompletedSpectra, persistPlan: true);
                var bindings = BuildPlanBindings(solution, state, includeLegacyCompleted: true, createMissingEntries: true);
                foreach (var pool in state.Pools)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    poolNumber++;
                    ReportPreparationProgress(
                        progress,
                        "生成計画作成",
                        poolNumber,
                        totalPoolCount,
                        poolNumber - 1,
                        solution.Name,
                        pool.Allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: false);
                    int completed = 0;
                    foreach (var binding in bindings.Where(item => item.Pool == pool))
                    {
                        var entry = binding.Entry
                            ?? throw new InvalidOperationException("生成計画に予約されていない行があります。");
                        if (entry.Status == SpectrumManifestStatus.Completed)
                        {
                            completed++;
                            continue;
                        }

                        entry.Status = SpectrumManifestStatus.Pending;
                        entry.FailureReason = null;
                        entry.GenerationAttemptCount++;
                        reservations.Add(CreateReservation(
                            solution,
                            pool.State.Handle,
                            entry,
                            request.Simulation.ResolutionStep,
                            pool.State.Handle.Condition.SemEdxCondition,
                            pool.Allocation.TargetSpectrumCount));
                    }

                    if (completed < pool.Allocation.TargetSpectrumCount)
                        shortageList.Add(new SpectrumPoolShortage(
                            solution.Name,
                            pool.Allocation.TargetSpectrumCount,
                            completed,
                            pool.Allocation.LiveTime,
                            pool.State.Handle.ConditionKey));
                    _repository.Save(pool.State.Handle, pool.State.Manifest);
                    ReportPreparationProgress(
                        progress,
                        "生成計画作成",
                        poolNumber,
                        totalPoolCount,
                        poolNumber,
                        solution.Name,
                        pool.Allocation.LiveTime,
                        0,
                        0,
                        poolCompleted: true);
                }
            }

            shortages = shortageList;
            return _simulationPlanBuilder.CreatePlan(request, reservations);
        }

        // 260907Codex: 学習は予定行そのものを使い、欠損行を同じpoolの別スペクトルで代替せず不足とする。
        private IReadOnlyList<SpectrumTrainingPool> CreateCompositionTimeTrainingPools(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress,
            CancellationToken cancellationToken,
            Action<string>? log,
            // 260908Codex: 計画の関連付けやmanifest解決ではなく、採用行そのものの検査だけを並列化する。
            int validationParallelism)
        {
            var pools = new List<SpectrumTrainingPool>(request.SelectedMineralSolutions.Count);
            var shortageList = new List<SpectrumPoolShortage>();
            int totalPoolCount = request.SelectedMineralSolutions.Count * request.GetEffectiveSpectrumTimeAllocations().Count;
            int poolNumber = 0;

            foreach (var solution in request.SelectedMineralSolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 260907Codex: スペクトルを開く前に計画の識別情報を解決・検証し、この計画外の保存ファイルは学習入力にしない。
                var state = BuildCompositionTimePlanState(request, solution, validateCompletedSpectra: true, persistPlan: true,
                    deferCompletedSpectrumValidation: true, progress: progress, completedPoolCount: poolNumber,
                    cancellationToken: cancellationToken, log: log);
                var bindings = BuildPlanBindings(solution, state, includeLegacyCompleted: false, createMissingEntries: false);
                var validationTimer = Stopwatch.StartNew();
                int validatedCount = 0;
                foreach (var pool in state.Pools)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    poolNumber++;
                    var entriesToCheck = bindings
                        .Where(item => item.Pool == pool && item.Entry?.Status == SpectrumManifestStatus.Completed)
                        .Select(item => item.Entry!)
                        .ToArray();
                    // 260907Codex: 各poolの識別情報と総数を1つの進捗値に保持し、検証中は検査済み・完了件数だけを変える。
                    var poolProgress = new SpectrumPoolPreparationProgress(
                        "採用EMSA確認", poolNumber, totalPoolCount, poolNumber - 1,
                        solution.Name, pool.Allocation.LiveTime, 0, entriesToCheck.Length, PoolCompleted: false);
                    progress?.Report(poolProgress);
                    bool changed = ValidateCompletedEntries(pool.State.Handle, entriesToCheck, validateCompletedSpectra: true,
                        onCompletedSpectrumChecked: (checkedCount, totalCount) =>
                        {
                            if (checkedCount == totalCount || checkedCount % 128 == 0)
                                progress?.Report(poolProgress with { FilesChecked = checkedCount });
                        // 260908Codex: 制限付き読込バッチごとに、項目順で結果と進捗を適用する。
                        }, cancellationToken, validationParallelism);
                    if (changed && _persistManifestRepairs)
                        _repository.Save(pool.State.Handle, pool.State.Manifest);
                    validatedCount += entriesToCheck.Length;
                    progress?.Report(poolProgress with
                    {
                        FilesChecked = entriesToCheck.Length,
                        CompletedPoolCount = poolNumber,
                        PoolCompleted = true
                    });
                    int completed = bindings.Count(item => item.Pool == pool && item.Entry?.Status == SpectrumManifestStatus.Completed);
                    if (completed < pool.Allocation.TargetSpectrumCount)
                        shortageList.Add(new SpectrumPoolShortage(
                            solution.Name,
                            pool.Allocation.TargetSpectrumCount,
                            completed,
                            pool.Allocation.LiveTime,
                            pool.State.Handle.ConditionKey));
                }
                log?.Invoke($"学習準備 採用EMSA確認: {solution.Name}, {validatedCount}件, {validationTimer.Elapsed.TotalSeconds:F2}秒");

                if (shortageList.Count > 0)
                    continue;
                var samples = bindings
                    .OrderBy(item => item.PlanEntry.PlanEntryId, StringComparer.Ordinal)
                    .Select(item => new SpectrumTrainingSample(
                        Path.Combine(item.Pool.State.Handle.PoolFolder, item.Entry!.FileName),
                        item.Entry.EndmemberFractions,
                        new SpectrumTrainingSource(
                            item.Pool.Allocation.LiveTime,
                            item.Pool.State.Handle.ConditionKey,
                            item.Entry.SimulationId,
                            item.Pool.State.Handle.ManifestPath,
                            state.Plan.PlanId,
                            // 260907Codex: 各採用元とともに、実際の等重み・不等重みの割当アルゴリズムを記録する。
                            state.Plan.PlannerVersion)))
                    .ToArray();
                pools.Add(new SpectrumTrainingPool(
                    solution.Name,
                    solution.Members.Select(member => member.Name).ToArray(),
                    samples));
            }

            if (shortageList.Count > 0)
            {
                shortages = shortageList;
                return [];
            }

            shortages = [];
            return pools;
        }

        // 260907Codex: 各予定実体行のハッシュに条件キーを含むため、先に全条件キーのpoolを解決する。
        private CompositionTimePlanState BuildCompositionTimePlanState(
            ModelCreationRequest request,
            SolidSolution solution,
            bool validateCompletedSpectra,
            bool persistPlan,
            // 260907Codex: 厳密な計画学習だけは関連付け確定まで実体検証を遅延し、生成とプレビューはpool全体の検証を維持する。
            bool deferCompletedSpectrumValidation = false,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            int completedPoolCount = 0,
            CancellationToken cancellationToken = default,
            Action<string>? log = null)
        {
            var pools = new List<CompositionTimePoolState>();
            var timer = Stopwatch.StartNew();
            // 260907Codex: 検証済み時間割当を一度解決し、pool読込と決定的な計画構築に使う。
            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            int totalPoolCount = request.SelectedMineralSolutions.Count * allocations.Count;
            foreach (var allocation in allocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportPreparationProgress(progress, "管理データ確認", completedPoolCount + pools.Count + 1, totalPoolCount,
                    completedPoolCount, solution.Name, allocation.LiveTime, 0, 0, poolCompleted: false);
                var allocationRequest = request.ForSpectrumTimeAllocation(allocation);
                pools.Add(new CompositionTimePoolState(
                    allocation,
                    persistPlan
                        ? LoadState(allocationRequest, solution, validateCompletedSpectra,
                            cancellationToken: cancellationToken, deferCompletedSpectrumValidation: deferCompletedSpectrumValidation)
                        : LoadPreviewState(allocationRequest, solution)));
            }
            log?.Invoke($"学習準備 管理データ確認: {solution.Name}, {timer.Elapsed.TotalSeconds:F2}秒");

            var conditionKeys = pools.ToDictionary(item => item.Allocation.LiveTime, item => item.State.Handle.ConditionKey);
            var planner = new SpectrumCompositionTimePlanner();
            timer.Restart();
            // 260907Codex: 乱数取得や計画ハッシュを変えず、候補件数で長い制約付き列挙の進捗と中止可能性を保つ。
            void ReportCandidateProgress(int count) => progress?.Report(new SpectrumPoolPreparationProgress(
                "組成・時間計画の再計算", completedPoolCount + 1, totalPoolCount, completedPoolCount,
                solution.Name, 0, count, 0, false, "組成候補"));
            SpectrumCompositionTimePlan proposed = planner.Create(
                solution,
                request.Simulation.ResolutionStep,
                allocations,
                conditionKeys,
                // 260907Codex: 等しい比率でも丸め後の件数は異なり得るため、本来の重みが不等な場合だけ割当数をシャッフルする。
                useShuffledTimeSlots: request.SpectrumTimeSchedule!.TimeWeights.Select(item => item.Weight).Distinct().Skip(1).Any(),
                cancellationToken: cancellationToken, onCandidateExamined: ReportCandidateProgress);
            log?.Invoke($"学習準備 組成・時間計画の再計算: {solution.Name}, {timer.Elapsed.TotalSeconds:F2}秒");
            cancellationToken.ThrowIfCancellationRequested();
            ReportPreparationProgress(progress, "保存計画との照合", completedPoolCount + 1, totalPoolCount,
                completedPoolCount, solution.Name, 0, 0, 0, poolCompleted: false);
            timer.Restart();
            // 260907Codex: 読み取り専用・変更可能の両経路で保存計画を検証し、欠損文書の復元は後者だけに許す。
            SpectrumCompositionTimePlan plan = SpectrumCompositionTimePlanStore.LoadOrCreate(
                request.Paths.SpectrumOutputFolder,
                proposed,
                pools.Select(item => item.State.Manifest).ToArray(),
                persist: persistPlan && _persistManifestRepairs);
            log?.Invoke($"学習準備 保存計画との照合: {solution.Name}, {timer.Elapsed.TotalSeconds:F2}秒");
            cancellationToken.ThrowIfCancellationRequested();
            return new CompositionTimePlanState(plan, pools);
        }

        // 260907Codex: 非対話・GUIのプレビューは、再利用可能な正確な予定行を数える間にmanifest作成や修復保存を行わない。
        private PoolState LoadPreviewState(ModelCreationRequest request, SolidSolution solution)
        {
            var handle = _repository.ResolvePool(
                request.Paths.SpectrumOutputFolder,
                solution,
                request.Simulation.ResolutionStep,
                request.SemEdxCondition);
            SpectrumPoolManifest manifest = _repository.Load(handle.ManifestPath)
                ?? new SpectrumPoolManifest
                {
                    ConditionKey = handle.ConditionKey,
                    MineralName = solution.Name,
                    Condition = handle.Condition
                };
            // 260907Codex: 古いCompletedフラグをメモリ内で下げ、プレビューで計画の関連付けが欠損・破損ファイルを数えないようにする。
            foreach (var entry in manifest.Spectra.Where(entry => entry.Status == SpectrumManifestStatus.Completed))
                if (!EmsaSpectrumIntegrityValidator.TryValidate(
                    Path.Combine(handle.PoolFolder, entry.FileName),
                    handle.Condition.SemEdxCondition,
                    out _))
                    entry.Status = SpectrumManifestStatus.Missing;
            var completed = manifest.Spectra
                .Where(entry => entry.Status == SpectrumManifestStatus.Completed)
                .ToArray();
            return new PoolState(handle, manifest, completed);
        }

        // 260907Codex: 計画済み項目を先に照合し、必要なら組成の完全一致で旧Completed行を一対一に結び付ける。manifest変更と行作成は生成時だけ行う。
        private static IReadOnlyList<CompositionTimePlanBinding> BuildPlanBindings(
            SolidSolution solution,
            CompositionTimePlanState state,
            bool includeLegacyCompleted,
            bool createMissingEntries)
        {
            var poolByLiveTime = state.Pools.ToDictionary(item => item.Allocation.LiveTime);
            var expectedById = state.Plan.Entries.ToDictionary(item => item.PlanEntryId, StringComparer.Ordinal);
            var bindings = new Dictionary<string, CompositionTimePlanBinding>(StringComparer.Ordinal);
            var usedEntries = new HashSet<SpectrumManifestEntry>();
            foreach (var pool in state.Pools)
            {
                // 260907Codex: 以前の計画との関連を保ちつつ、この計画のすべての関連付けを解決する。
                foreach (var row in pool.State.Manifest.Spectra.SelectMany(entry => entry.GetPlanBindings()
                    .Where(identity => string.Equals(identity.PlanId, state.Plan.PlanId, StringComparison.Ordinal))
                    .Select(identity => new { Entry = entry, Identity = identity })))
                {
                    var entry = row.Entry;
                    var identity = row.Identity;
                    if (identity.PlanEntryId is null
                        || !expectedById.TryGetValue(identity.PlanEntryId, out var planned)
                        || !string.Equals(identity.PlanRowHash, planned.RowHash, StringComparison.Ordinal)
                        // 260907Codex: コピーした計画ハッシュが一致していても、誤ったラベルのmanifest行は拒否する。
                        || identity.PlanRepeatOrdinal != planned.RepeatOrdinal
                        || !planned.MatchesFractions(entry.EndmemberFractions)
                        || Math.Abs(planned.LiveTime - pool.Allocation.LiveTime) > 1e-9
                        || !usedEntries.Add(entry)
                        || !bindings.TryAdd(identity.PlanEntryId, new CompositionTimePlanBinding(planned, pool, entry)))
                        throw new InvalidOperationException($"{solution.Name} のmanifestに不整合な組成・測定時間計画行があります。");
                }
            }

            foreach (var planned in state.Plan.Entries)
            {
                if (bindings.ContainsKey(planned.PlanEntryId))
                    continue;
                var pool = poolByLiveTime[planned.LiveTime];
                SpectrumManifestEntry? legacyEntry = includeLegacyCompleted
                    ? pool.State.CompletedEntries
                        // 260907Codex: 前の計画の正確なセルも新計画につき1回再利用し、元の関連付けは保持する。
                        .Where(entry => !usedEntries.Contains(entry)
                            && string.Equals(
                                solution.ComposeFractionKey(entry.EndmemberFractions),
                                solution.ComposeFractionKey(planned.EndmemberFractions),
                                StringComparison.Ordinal))
                        .OrderBy(entry => entry.SimulationId)
                        .FirstOrDefault()
                    : null;
                if (legacyEntry is not null)
                {
                    if (createMissingEntries)
                        ApplyPlanIdentity(legacyEntry, state.Plan, planned);
                    bindings.Add(planned.PlanEntryId, new CompositionTimePlanBinding(planned, pool, legacyEntry));
                    usedEntries.Add(legacyEntry);
                    continue;
                }

                if (!createMissingEntries)
                    continue;
                int simulationId = pool.State.Manifest.NextSimulationId++;
                var created = new SpectrumManifestEntry
                {
                    SimulationId = simulationId,
                    FileName = $"{SpectrumPoolRepository.SanitizeFileName(solution.Name)}_sim{simulationId:D6}.emsa",
                    Status = SpectrumManifestStatus.Pending,
                    EndmemberFractions = planned.EndmemberFractions
                };
                ApplyPlanIdentity(created, state.Plan, planned);
                pool.State.Manifest.Spectra.Add(created);
                bindings.Add(planned.PlanEntryId, new CompositionTimePlanBinding(planned, pool, created));
            }

            if (createMissingEntries)
            {
                foreach (var pool in state.Pools)
                    SpectrumCompositionTimePlanStore.AttachReference(pool.State.Manifest, state.Plan);
            }

            return bindings.Values.ToArray();
        }

        private static void ApplyPlanIdentity(
            SpectrumManifestEntry entry,
            SpectrumCompositionTimePlan plan,
            SpectrumCompositionTimePlanEntry planned)
        {
            // 260907Codex: 元のフィールドの後方互換性を保ち、後続計画ごとに別の関連付けを追加する。
            if (!string.IsNullOrWhiteSpace(entry.PlanId))
            {
                entry.AdditionalPlanBindings.Add(new SpectrumManifestPlanBinding
                {
                    PlanId = plan.PlanId,
                    PlanEntryId = planned.PlanEntryId,
                    PlanRowHash = planned.RowHash,
                    PlanRepeatOrdinal = planned.RepeatOrdinal
                });
                return;
            }
            entry.PlanId = plan.PlanId;
            entry.PlanEntryId = planned.PlanEntryId;
            entry.PlanRowHash = planned.RowHash;
            entry.PlanRepeatOrdinal = planned.RepeatOrdinal;
        }

        // 260901Codex: 同じ鉱物の独立した不足を、測定時間で区別する。
        public static string FormatShortageMessage(IEnumerable<SpectrumPoolShortage> shortages) =>
            string.Join(
                Environment.NewLine,
                shortages
                    .Where(shortage => shortage.MissingCount > 0)
                    .Select(shortage =>
                        $"{shortage.MineralName}（{shortage.LiveTime.ToString("G", CultureInfo.InvariantCulture)}秒）は {shortage.MissingCount} 件不足しています。"));

        // 260528Claude: Completed でも実体ファイルが消えている entry は Missing に格下げして manifest に書き戻し、次回再利用ループで同じ組成・simulationId が補充されるようにします。
        private PoolState LoadState(
            ModelCreationRequest request,
            SolidSolution solution,
            bool validateCompletedSpectra = true,
            Action<int, int>? onCompletedSpectrumChecked = null,
            CancellationToken cancellationToken = default,
            // 260907Codex: 厳密な計画学習では先にメタデータだけを読み、選択したCompleted行を同じ検証器で確認する。
            bool deferCompletedSpectrumValidation = false,
            // 260908Codex: 任意の同時読込数は、学習準備だけから渡す。
            int validationParallelism = 1)
        {
            var handle = _repository.ResolvePool(
                request.Paths.SpectrumOutputFolder,
                solution,
                request.Simulation.ResolutionStep,
                request.SemEdxCondition);
            var manifest = _repository.LoadOrCreate(handle);
            bool changed = EnsureNextSimulationIdAfterManifest(manifest);

            // 260620Codex: 既存manifestのラベルを整え、再試行と学習で百分率グリッド上の比率を使う。
            foreach (var entry in manifest.Spectra)
            {
                cancellationToken.ThrowIfCancellationRequested();
                changed |= NormalizeManifestEntryFractions(solution, entry, request.Simulation.ResolutionStep);
            }

            var completedEntriesToCheck = manifest.Spectra
                .Where(entry => entry.Status == SpectrumManifestStatus.Completed)
                .ToArray();
            if (!deferCompletedSpectrumValidation)
                changed |= ValidateCompletedEntries(handle, completedEntriesToCheck, validateCompletedSpectra,
                    // 260908Codex: 読み取り専用の検証が順序どおり戻ってから、manifest状態を変更する。
                    onCompletedSpectrumChecked, cancellationToken, validationParallelism);

            // 260803Codex: 読み取り専用でも同じ修復後データを返し、永続化だけを止めて採取値を維持します。
            if (changed && _persistManifestRepairs)
                _repository.Save(handle, manifest);

            var completedEntries = manifest.Spectra
                .Where(entry => entry.Status == SpectrumManifestStatus.Completed)
                .ToArray();

            return new PoolState(handle, manifest, completedEntries);
        }

        // 260907Codex: 旧式の全pool検査とBの厳密な採用行検査で、同じ完全性検証・エラー処理を共用する。
        private static bool ValidateCompletedEntries(
            SpectrumPoolHandle handle,
            SpectrumManifestEntry[] completedEntriesToCheck,
            bool validateCompletedSpectra,
            Action<int, int>? onCompletedSpectrumChecked,
            CancellationToken cancellationToken,
            // 260908Codex: 既存の検証利用側では、ファイルを1つずつ読み取る。
            int validationParallelism = 1)
        {
            // 260908Codex: 進行中の読込と結果バッファーを進捗1区間に制限し、リポジトリ状態をworkerへ渡さない。
            int batchSize = validationParallelism > 1 ? 128 : 1;
            var failureReasons = new string?[Math.Min(batchSize, completedEntriesToCheck.Length)];
            var options = new ParallelOptions { MaxDegreeOfParallelism = validationParallelism, CancellationToken = cancellationToken };
            bool changed = false;
            for (int batchStart = 0; batchStart < completedEntriesToCheck.Length; batchStart += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(batchSize, completedEntriesToCheck.Length - batchStart);
                if (validationParallelism > 1 && count > 1)
                    Parallel.For(0, count, options, index => failureReasons[index] = ValidateFile(batchStart + index));
                else
                    failureReasons[0] = ValidateFile(batchStart);

                // 260908Codex: workerは項目変更やコールバック呼出をせず、修復と進捗の意味を決定的に保つ。
                for (int index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int completedIndex = batchStart + index;
                    if (failureReasons[index] is string failureReason)
                    {
                        var entry = completedEntriesToCheck[completedIndex];
                        entry.Status = SpectrumManifestStatus.Missing;
                        entry.FailureReason = failureReason;
                        changed = true;
                    }

                    onCompletedSpectrumChecked?.Invoke(completedIndex + 1, completedEntriesToCheck.Length);
                }
            }

            return changed;

            // 260908Codex: 逐次・並列の読取で、変更していない実体検証器と失敗文言を共用する。
            string? ValidateFile(int completedIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = completedEntriesToCheck[completedIndex];
                string spectrumPath = Path.Combine(handle.PoolFolder, entry.FileName);
                bool valid = File.Exists(spectrumPath);
                string failureReason = valid ? string.Empty : "出力ファイルが見つかりませんでした。";
                if (valid && validateCompletedSpectra)
                    // 260901Codex: 学習と明示的な完全性検証では全EMSAを検証し、生成の再開では高速な存在確認を使う。
                    valid = EmsaSpectrumIntegrityValidator.TryValidate(
                        spectrumPath,
                        handle.Condition.SemEdxCondition,
                        out failureReason);

                return valid ? null : failureReason;
            }
        }

        // 260902Codex: 準備進捗の報告を一か所にまとめ、workerスレッドの走査からWinFormsコントロールへ触れないようにする。
        private static void ReportPreparationProgress(
            IProgress<SpectrumPoolPreparationProgress>? progress,
            string phase,
            int poolNumber,
            int totalPoolCount,
            int completedPoolCount,
            string mineralName,
            double liveTime,
            int filesChecked,
            int filesToCheck,
            bool poolCompleted) =>
            progress?.Report(new SpectrumPoolPreparationProgress(
                phase,
                poolNumber,
                totalPoolCount,
                completedPoolCount,
                mineralName,
                liveTime,
                filesChecked,
                filesToCheck,
                poolCompleted));

        // 260528Claude: 既存 Pending/Failed/Missing entry を先に再利用し、不足分は二分岐で発行します。N' (constraint 適用後の有効候補数) と totalAfter (合計目標件数) の大小で、全列挙ベースか直接サンプリングかを自動で切り替えます。
        private IReadOnlyList<SpectrumSimulationReservation> ReserveMissingSpectra(
            SolidSolution solution,
            PoolState state,
            double resolutionStep,
            // 260901Codex: 各予約に割当条件と目標を保持し、複数時間のジョブをまとめる。
            SemEdxCondition semEdxCondition,
            int targetSpectrumCount,
            int missingCount)
        {
            var reservations = new List<SpectrumSimulationReservation>(missingCount);
            foreach (var entry in state.Manifest.Spectra
                .Where(entry => entry.Status is SpectrumManifestStatus.Pending or SpectrumManifestStatus.Failed or SpectrumManifestStatus.Missing)
                .OrderBy(entry => entry.Status switch
                {
                    SpectrumManifestStatus.Pending => 0,
                    SpectrumManifestStatus.Failed => 1,
                    SpectrumManifestStatus.Missing => 2,
                    _ => 3
                })
                .ThenBy(entry => entry.SimulationId)
                .Take(missingCount))
            {
                entry.Status = SpectrumManifestStatus.Pending;
                entry.FailureReason = null;
                reservations.Add(CreateReservation(
                    solution,
                    state.Handle,
                    entry,
                    resolutionStep,
                    semEdxCondition,
                    targetSpectrumCount));
            }

            int newReservationCount = missingCount - reservations.Count;
            if (newReservationCount <= 0)
                return reservations;

            // 260528Claude: 既存組成の出現回数を端成分順キーで集計します。Completed だけでなく再利用予約済みの Pending/Failed/Missing も含めて、manifest 全体の現状を反映させます。
            var existingCounts = new Dictionary<string, int>();
            foreach (var entry in state.Manifest.Spectra)
            {
                string key = solution.ComposeFractionKey(entry.EndmemberFractions);
                existingCounts[key] = existingCounts.GetValueOrDefault(key) + 1;
            }

            // 260528Claude: totalAfter は「最終的に manifest に並ぶ entry 数」。既存 entry すべて + 新規発行予定で算出します。
            int totalAfter = state.Manifest.Spectra.Count + newReservationCount;

            // 260528Claude: 巨大候補空間 (例: 6 端成分 1% で N=96M) でも Take で必ず打ち切るため、配列長で N' との大小を判定します。
            var candidates = solution.EnumerateCandidateFractionsLazy(resolutionStep)
                .Take(totalAfter + 1)
                .ToArray();

            if (candidates.Length == 0)
                return reservations;

            double[][] fractionsToReserve = candidates.Length <= totalAfter
                ? AssignByDeficit(solution, candidates, existingCounts, newReservationCount, _random)
                : SampleUniqueFractions(solution, resolutionStep, existingCounts.Keys, newReservationCount, _random);

            foreach (var fractions in fractionsToReserve)
            {
                int simulationId = state.Manifest.NextSimulationId++;
                string fileName = $"{SpectrumPoolRepository.SanitizeFileName(solution.Name)}_sim{simulationId:D6}.emsa";
                var entry = new SpectrumManifestEntry
                {
                    SimulationId = simulationId,
                    FileName = fileName,
                    Status = SpectrumManifestStatus.Pending,
                    EndmemberFractions = solution.CreateEndmemberFractionMap(fractions, resolutionStep)
                };

                state.Manifest.Spectra.Add(entry);
                reservations.Add(CreateReservation(
                    solution,
                    state.Handle,
                    entry,
                    resolutionStep,
                    semEdxCondition,
                    targetSpectrumCount));
            }

            return reservations;
        }

        // 260528Claude: 全列挙ベース。priority queue で (existing+assigned) が最小の候補に +1 を newReservationCount 回割り当て、出現回数を可能な限り均等化します。
        private static double[][] AssignByDeficit(
            SolidSolution solution,
            double[][] candidates,
            Dictionary<string, int> existingCounts,
            int newReservationCount,
            Random random)
        {
            var heap = new PriorityQueue<int, (int Count, int Shuffle)>();
            var assigned = new int[candidates.Length];
            var initialCounts = new int[candidates.Length];
            for (int i = 0; i < candidates.Length; i++)
            {
                initialCounts[i] = existingCounts.GetValueOrDefault(solution.ComposeFractionKey(candidates[i]));
                heap.Enqueue(i, (initialCounts[i], random.Next()));
            }

            for (int k = 0; k < newReservationCount; k++)
            {
                int idx = heap.Dequeue();
                assigned[idx]++;
                heap.Enqueue(idx, (initialCounts[idx] + assigned[idx], random.Next()));
            }

            var result = new double[newReservationCount][];
            int writeIndex = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                for (int j = 0; j < assigned[i]; j++)
                    result[writeIndex++] = candidates[i];
            }

            // 260528Claude: manifest 上の simulationId 順が組成順にならないよう、Fisher-Yates で全体をシャッフルします。
            for (int i = result.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }

            return result;
        }

        // 260528Claude: 直接サンプリング。bars-and-stars 法で 1 セット乱択 → constraint チェック → 既存重複チェック を newReservationCount 件揃うまで繰り返します。試行上限到達は構成不能としてエラー扱いにします。
        private const int MaxAttemptsPerSample = 10000;

        private static double[][] SampleUniqueFractions(
            SolidSolution solution,
            double resolutionStep,
            IEnumerable<string> initialExistingKeys,
            int newReservationCount,
            Random random)
        {
            var existingKeys = new HashSet<string>(initialExistingKeys);
            var result = new double[newReservationCount][];

            for (int k = 0; k < newReservationCount; k++)
            {
                bool found = false;
                for (int attempt = 0; attempt < MaxAttemptsPerSample; attempt++)
                {
                    double[] fractions = solution.SampleRandomFraction(resolutionStep, random);
                    if (!solution.CapableComposition(fractions))
                        continue;
                    string key = solution.ComposeFractionKey(fractions);
                    if (!existingKeys.Add(key))
                        continue;

                    result[k] = fractions;
                    found = true;
                    break;
                }

                if (!found)
                    throw new InvalidOperationException(
                        $"組成のランダムサンプリングが {MaxAttemptsPerSample} 回連続で失敗しました。constraint が厳しすぎるか、候補空間が枯渇しています。");
            }

            return result;
        }

        // 260527Codex: 古い manifest でも末尾から追加できるよう、NextSimulationId を既存最大値の次へそろえます。
        private static bool EnsureNextSimulationIdAfterManifest(SpectrumPoolManifest manifest)
        {
            int nextSimulationId = manifest.Spectra.Count == 0
                ? 0
                : manifest.Spectra.Max(entry => entry.SimulationId) + 1;
            if (manifest.NextSimulationId >= nextSimulationId)
                return false;

            manifest.NextSimulationId = nextSimulationId;
            return true;
        }

        // 260513Codex: manifest の endmemberFractions を鉱物定義順の配列へ戻して DTSA-II 入力を再構築します。
        private static SpectrumSimulationReservation CreateReservation(
            SolidSolution solution,
            SpectrumPoolHandle handle,
            SpectrumManifestEntry entry,
            double resolutionStep,
            // 260901Codex: ジョブ構築は要求条件を使い、ハンドルは解決した互換poolの識別情報を渡す。
            SemEdxCondition semEdxCondition,
            int targetSpectrumCount)
        {
            var fractions = solution.NormalizeEndmemberFractions(
                ReadEndmemberFractions(solution, entry),
                resolutionStep);

            return new SpectrumSimulationReservation(
                solution.Name,
                handle.PoolFolder,
                handle.ManifestPath,
                entry.SimulationId,
                entry.FileName,
                solution.CalculateCompositionWeights(fractions),
                handle.ConditionKey,
                semEdxCondition,
                targetSpectrumCount);
        }

        // 260620Codex: 削除処理とDTSA-II予約入力で、同じmanifest検索経路を再利用する。
        private static double[] ReadEndmemberFractions(SolidSolution solution, SpectrumManifestEntry entry) =>
            solution.Members
                .Select(member => TryGetFraction(entry.EndmemberFractions, member.Name, out double fraction)
                    ? fraction
                    : 0)
                .ToArray();

        // 260620Codex: 過去の微小な負値も含め、旧manifest項目をその場で正規化する。
        private static bool NormalizeManifestEntryFractions(
            SolidSolution solution,
            SpectrumManifestEntry entry,
            double resolutionStep)
        {
            var normalized = solution.NormalizeEndmemberFractions(
                ReadEndmemberFractions(solution, entry),
                resolutionStep);
            if (ManifestFractionsMatch(solution, entry, normalized))
                return false;

            entry.EndmemberFractions = solution.CreateEndmemberFractionMap(normalized, resolutionStep);
            return true;
        }

        // 260620Codex: 比較を厳密に保ち、整えたJSON値を一度だけ書き戻す。
        private static bool ManifestFractionsMatch(
            SolidSolution solution,
            SpectrumManifestEntry entry,
            double[] normalized)
        {
            if (entry.EndmemberFractions.Count != solution.Members.Length)
                return false;

            for (int i = 0; i < solution.Members.Length && i < normalized.Length; i++)
                if (!TryGetFraction(entry.EndmemberFractions, solution.Members[i].Name, out double value)
                    || !value.Equals(normalized[i]))
                    return false;

            return true;
        }

        // 260620Codex: 旧形式の大文字・小文字の違いを許容し、端成分キーの重複は残さない。
        private static bool TryGetFraction(
            IReadOnlyDictionary<string, double> fractions,
            string memberName,
            out double fraction)
        {
            if (fractions.TryGetValue(memberName, out fraction))
                return true;

            foreach (var pair in fractions)
            {
                if (!string.Equals(pair.Key, memberName, StringComparison.OrdinalIgnoreCase))
                    continue;

                fraction = pair.Value;
                return true;
            }

            fraction = 0;
            return false;
        }

        // 260901Codex: 保存マーカーを個別に適用し、Completedのチェックポイントにする前にEMSA全体を検証する。
        private static void ApplyResultToEntry(
            SimulationExecutionResult result,
            SpectrumSimulationReservation reservation,
            SpectrumManifestEntry entry)
        {
            // 260606Claude: 正常終了の全件保存に加え、保存完了マーカーが出た spectrum も「保存済み」とみなします。
            bool cleanExit = !result.IsCanceled && result.ExitCode == 0 && result.ExceptionMessage is null;
            bool saved = cleanExit || (result.SavedSpectrumFiles?.Contains(reservation.FileName) ?? false);

            string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
            // 260901Codex: 保存マーカーも正常終了もなく検証できない場合は、決定的なMissing理由を残す。
            string validationFailureReason = "DTSA-II の保存完了を確認できませんでした。";
            // 260901Codex: 不完全または物理条件の異なるEMSAを、Completedとして保存しない。
            if (saved && EmsaSpectrumIntegrityValidator.TryValidate(
                outputPath,
                reservation.SemEdxCondition,
                out validationFailureReason))
            {
                entry.Status = SpectrumManifestStatus.Completed;
                entry.FailureReason = null;
                entry.LastFailureKind = null;
                return;
            }

            if (result.IsCanceled)
            {
                entry.Status = SpectrumManifestStatus.Pending;
                entry.FailureReason = null;
                entry.LastFailureKind = "Canceled";
                return;
            }

            if (!cleanExit)
            {
                entry.Status = SpectrumManifestStatus.Failed;
                entry.FailureReason = BuildFailureReason(result);
                entry.LastFailureKind = "ProcessFailed";
                return;
            }

            // 260901Codex: プロセスが正常終了してもEMSAが欠損・不正なら、Missing経路で再試行する。
            entry.Status = SpectrumManifestStatus.Missing;
            entry.FailureReason = validationFailureReason;
            entry.LastFailureKind = "InvalidOutput";
        }

        // 260507Codex: manifest に保存する失敗理由は長くなりすぎないよう要点だけにします。
        private static string BuildFailureReason(SimulationExecutionResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ExceptionMessage))
                return result.ExceptionMessage;

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                return TrimFailureReason(result.StandardError);

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                return TrimFailureReason(result.StandardOutput);

            return $"DTSA-II が終了コード {result.ExitCode} で終了しました。";
        }

        // 260507Codex: 巨大な標準出力を manifest に抱え込まないよう先頭だけを残します。
        private static string TrimFailureReason(string value) =>
            value.Length <= 1000 ? value : value[..1000];

        // 260507Codex: pool の読み込み結果を内部処理用にまとめます。
        private sealed record PoolState(
            SpectrumPoolHandle Handle,
            SpectrumPoolManifest Manifest,
            IReadOnlyList<SpectrumManifestEntry> CompletedEntries);

        // 260901Codex: 厳密な複数時間の学習選択全体で、検証済みの1つのmanifestスナップショットを再利用する。
        private sealed record TrainingPoolSelectionState(
            SolidSolution Solution,
            SpectrumTimeAllocation Allocation,
            PoolState State);

        // 260907Codex: 鉱物ごとの全体計画で、条件が均一な通常poolを保ち、組成予定表を1つ共有する。
        private sealed record CompositionTimePlanState(
            SpectrumCompositionTimePlan Plan,
            IReadOnlyList<CompositionTimePoolState> Pools);

        private sealed record CompositionTimePoolState(
            SpectrumTimeAllocation Allocation,
            PoolState State);

        private sealed record CompositionTimePlanBinding(
            SpectrumCompositionTimePlanEntry PlanEntry,
            CompositionTimePoolState Pool,
            SpectrumManifestEntry? Entry);
    }
}
