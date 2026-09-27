using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace MineraScope
{
    // 260901Codex: Include live time and condition identity because one mineral can now have several independent shortages.
    internal sealed record SpectrumPoolShortage(
        string MineralName,
        int RequiredCount,
        int CompletedCount,
        double LiveTime,
        string ConditionKey)
    {
        public int MissingCount => Math.Max(0, RequiredCount - CompletedCount);
    }

    // 260901Codex: Report explicit Pending recovery outcomes without changing invalid or absent reservations.
    internal sealed record PendingSpectrumRecoveryResult(
        int ExaminedCount,
        int RecoveredCount,
        int InvalidCount,
        int MissingFileCount);

    // 260902Codex: Show pool preparation progress while manifest and EMSA entries are inspected off the UI thread.
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
        // 260907Codex: Candidate checks are not EMSA reads; keep the preparation display explicit about its unit.
        string ItemName = "EMSA");

    // 260907Codex: Recovery and exact sample selection belong to one bounded preparation operation.
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

        // 260901Codex: Count each effective time allocation independently while preserving the legacy single-time path.
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

        // 260901Codex: Dry-run preview returns only deficient rows while keeping all filesystem access read-only.
        public IReadOnlyList<SpectrumPoolShortage> PreviewShortages(ModelCreationRequest request) =>
            PreviewPoolStates(request)
                .Where(item => item.MissingCount > 0)
                .ToArray();

        // 260901Codex: Include sufficient rows so headless output can show the actual reusable count for 20- and 50-second pools.
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

        // 260901Codex: Preview workload counts with production job allocation and no reservation side effects.
        public SimulationExecutionPlanPreview PreviewMissingSimulationPlan(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            shortages = PreviewShortages(request);
            return _simulationPlanBuilder.CreatePreview(request, shortages);
        }

        // 260901Codex: Aggregate status counts across every effective measurement-time pool.
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

        // 260907Codex: B status is reported for scheduled rows only, so retained legacy spectra cannot make a completed plan look failed or incomplete.
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

        // 260907Codex: The caller holds the pool mutation lease. B shares manifest reads across recovery/selection for one mineral at a time, bounding cache memory.
        public SpectrumTrainingPreparationResult PrepareTrainingPools(
            ModelCreationRequest request,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default,
            Action<string>? log = null)
        {
            var timer = Stopwatch.StartNew();
            var recovery = new PendingSpectrumRecoveryResult(0, 0, 0, 0);
            var pools = new List<SpectrumTrainingPool>();
            var shortages = new List<SpectrumPoolShortage>();
            int manifestReads = 0;
            int manifestCacheHits = 0;
            int poolOffset = 0;
            int timeCount = request.GetEffectiveSpectrumTimeAllocations().Count;
            int totalPoolCount = request.SelectedMineralSolutions.Count * timeCount;
            // 260907Codex: Legacy selection consumes one shared Random across minerals; do not partition or reseed that path.
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
                log?.Invoke($"学習準備 Pending回収: {string.Join(", ", batch.SelectedMineralSolutions.Select(item => item.Name))}, {recoveryTimer.Elapsed.TotalSeconds:F2}秒");
                recovery = new PendingSpectrumRecoveryResult(
                    recovery.ExaminedCount + recovered.ExaminedCount,
                    recovery.RecoveredCount + recovered.RecoveredCount,
                    recovery.InvalidCount + recovered.InvalidCount,
                    recovery.MissingFileCount + recovered.MissingFileCount);
                pools.AddRange(workflow.CreateTrainingPools(batch, out var batchShortages, batchProgress, cancellationToken, log));
                shortages.AddRange(batchShortages);
                manifestReads += repository.ManifestReadCount;
                manifestCacheHits += repository.ManifestCacheHitCount;
                poolOffset += batch.SelectedMineralSolutions.Count * timeCount;
            }

            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke($"学習準備完了: {timer.Elapsed.TotalSeconds:F2}秒, manifest読込 {manifestReads}回 / 再利用 {manifestCacheHits}回");
            return new SpectrumTrainingPreparationResult(recovery, shortages.Count == 0 ? pools : [], shortages);
        }

        // 260907Codex: Synchronously translate per-mineral counters; the caller's IProgress owns UI-thread dispatch.
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

        // 260901Codex: Select the exact target from each time pool; explicit schedules carry source metadata and composition stratification.
        public IReadOnlyList<SpectrumTrainingPool> CreateTrainingPools(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default,
            // 260907Codex: Phase timings use an injected logger; headless and fixture callers need not write logs.
            Action<string>? log = null)
        {
            if (request.UsesCompositionTimePlan)
                return CreateCompositionTimeTrainingPools(request, out shortages, progress, cancellationToken, log);

            var pools = new List<SpectrumTrainingPool>(request.SelectedMineralSolutions.Count);
            var allocations = request.GetEffectiveSpectrumTimeAllocations();
            bool hasExplicitAllocations = request.SpectrumTimeAllocations.Count > 0;
            int totalPoolCount = request.SelectedMineralSolutions.Count * allocations.Count;
            int poolNumber = 0;
            var preparedStates = new List<TrainingPoolSelectionState>(
                request.SelectedMineralSolutions.Count * allocations.Count);
            var shortageList = new List<SpectrumPoolShortage>();

            // 260901Codex: Load and validate every pool once, avoiding a second full EMSA scan before selection.
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
                        cancellationToken: cancellationToken);
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
                    // 260901Codex: A file can disappear between the initial shortage pass and selection; return a shortage instead of throwing.
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

        // 260901Codex: Reserve each time in its normal condition-key manifest, then build one bounded batch per mineral.
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
                    // 260902Codex: The GUI passes false here so a restart only checks file existence;
                    // callers retaining the default still receive the original full EMSA integrity check.
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

        // 260901Codex: Call only when no DTSA-II run is active; valid completed files are recovered while invalid or absent Pending entries remain retryable.
        public PendingSpectrumRecoveryResult RecoverPendingSpectra(
            ModelCreationRequest request,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
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

                    // 260907Codex: Read-only preparation may recover in memory but must not write a manifest repair.
                    if (changed && _persistManifestRepairs)
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

        // 260907Codex: A read-only preview derives the same deterministic plan but never creates a plan file, reservation, or manifest tag.
        private IReadOnlyList<SpectrumPoolShortage> PreviewCompositionTimePlanStates(ModelCreationRequest request) =>
            CollectCompositionTimePlanShortages(
                request,
                validateCompletedSpectra: true,
                persistPlan: false,
                includeLegacyCompleted: true);

        // 260907Codex: After a plan has been reserved, only plan-tagged Completed rows satisfy its shortage. Legacy rows are adopted during reservation, never silently at training time.
        private IReadOnlyList<SpectrumPoolShortage> GetCompositionTimePlanShortages(
            ModelCreationRequest request,
            bool validateCompletedSpectra)
            => CollectCompositionTimePlanShortages(
                request,
                validateCompletedSpectra,
                persistPlan: true,
                includeLegacyCompleted: false)
                // 260907Codex: Match the legacy contract: a completed plan has no shortages, not five zero-shortage rows.
                .Where(item => item.MissingCount > 0)
                .ToArray();

        // 260907Codex: Preview may count exact legacy rows before reservation; all later status and training checks require rows already bound to the saved plan.
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

        // 260907Codex: Reserve every missing planned row once. Existing Completed rows are adopted only when their composition and physical pool exactly match a scheduled row.
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

        // 260907Codex: Training consumes exactly the planned rows. A missing row remains a shortage instead of being replaced by another spectrum from the same pool.
        private IReadOnlyList<SpectrumTrainingPool> CreateCompositionTimeTrainingPools(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages,
            IProgress<SpectrumPoolPreparationProgress>? progress,
            CancellationToken cancellationToken,
            Action<string>? log)
        {
            var pools = new List<SpectrumTrainingPool>(request.SelectedMineralSolutions.Count);
            var shortageList = new List<SpectrumPoolShortage>();
            int totalPoolCount = request.SelectedMineralSolutions.Count * request.GetEffectiveSpectrumTimeAllocations().Count;
            int poolNumber = 0;

            foreach (var solution in request.SelectedMineralSolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 260907Codex: Resolve and verify plan identities before opening spectra; retained files outside this plan are not training inputs.
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
                    // 260907Codex: Keep each pool's identity and total in one progress value; only checked/completed counts change during validation.
                    var poolProgress = new SpectrumPoolPreparationProgress(
                        "採用EMSA確認", poolNumber, totalPoolCount, poolNumber - 1,
                        solution.Name, pool.Allocation.LiveTime, 0, entriesToCheck.Length, PoolCompleted: false);
                    progress?.Report(poolProgress);
                    bool changed = ValidateCompletedEntries(pool.State.Handle, entriesToCheck, validateCompletedSpectra: true,
                        onCompletedSpectrumChecked: (checkedCount, totalCount) =>
                        {
                            if (checkedCount == totalCount || checkedCount % 128 == 0)
                                progress?.Report(poolProgress with { FilesChecked = checkedCount });
                        }, cancellationToken);
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
                            // 260907Codex: Record the actual equal/unequal assignment algorithm with each selected source.
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

        // 260907Codex: Resolve all condition-key pools first because those keys are part of each planned physical row hash.
        private CompositionTimePlanState BuildCompositionTimePlanState(
            ModelCreationRequest request,
            SolidSolution solution,
            bool validateCompletedSpectra,
            bool persistPlan,
            // 260907Codex: Only exact-plan training defers physical checks until its bindings are known; generation and preview keep full pool checks.
            bool deferCompletedSpectrumValidation = false,
            IProgress<SpectrumPoolPreparationProgress>? progress = null,
            int completedPoolCount = 0,
            CancellationToken cancellationToken = default,
            Action<string>? log = null)
        {
            var pools = new List<CompositionTimePoolState>();
            var timer = Stopwatch.StartNew();
            // 260907Codex: Resolve the validated time allocation once for pool loading and deterministic plan construction.
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
            // 260907Codex: Candidate counts keep long constrained enumeration visible and cancellable without changing random draws or plan hashes.
            void ReportCandidateProgress(int count) => progress?.Report(new SpectrumPoolPreparationProgress(
                "組成・時間計画の再計算", completedPoolCount + 1, totalPoolCount, completedPoolCount,
                solution.Name, 0, count, 0, false, "組成候補"));
            SpectrumCompositionTimePlan proposed = planner.Create(
                solution,
                request.Simulation.ResolutionStep,
                allocations,
                conditionKeys,
                // 260907Codex: Equal ratios may round to unequal counts; only genuinely unequal weights select quota shuffling.
                useShuffledTimeSlots: request.SpectrumTimeSchedule!.TimeWeights.Select(item => item.Weight).Distinct().Skip(1).Any(),
                cancellationToken: cancellationToken, onCandidateExamined: ReportCandidateProgress);
            log?.Invoke($"学習準備 組成・時間計画の再計算: {solution.Name}, {timer.Elapsed.TotalSeconds:F2}秒");
            cancellationToken.ThrowIfCancellationRequested();
            ReportPreparationProgress(progress, "保存計画との照合", completedPoolCount + 1, totalPoolCount,
                completedPoolCount, solution.Name, 0, 0, 0, poolCompleted: false);
            timer.Restart();
            // 260907Codex: Both read-only and mutating paths validate persisted plans; only the latter may restore a missing document.
            SpectrumCompositionTimePlan plan = SpectrumCompositionTimePlanStore.LoadOrCreate(
                request.Paths.SpectrumOutputFolder,
                proposed,
                pools.Select(item => item.State.Manifest).ToArray(),
                persist: persistPlan && _persistManifestRepairs);
            log?.Invoke($"学習準備 保存計画との照合: {solution.Name}, {timer.Elapsed.TotalSeconds:F2}秒");
            cancellationToken.ThrowIfCancellationRequested();
            return new CompositionTimePlanState(plan, pools);
        }

        // 260907Codex: The headless and GUI preview must not create a manifest or write repair state while counting exact reusable planned rows.
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
            // 260907Codex: Downgrade stale Completed flags in memory so planned bindings cannot count missing/corrupt files in preview.
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

        // 260907Codex: Match already planned entries first, then optionally bind legacy Completed rows one-to-one by exact composition. Only generation mutates manifests or creates rows.
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
                // 260907Codex: Resolve every association for this plan while retaining associations to older plans.
                foreach (var row in pool.State.Manifest.Spectra.SelectMany(entry => entry.GetPlanBindings()
                    .Where(identity => string.Equals(identity.PlanId, state.Plan.PlanId, StringComparison.Ordinal))
                    .Select(identity => new { Entry = entry, Identity = identity })))
                {
                    var entry = row.Entry;
                    var identity = row.Identity;
                    if (identity.PlanEntryId is null
                        || !expectedById.TryGetValue(identity.PlanEntryId, out var planned)
                        || !string.Equals(identity.PlanRowHash, planned.RowHash, StringComparison.Ordinal)
                        // 260907Codex: Reject mislabeled manifest rows even if their copied plan hash still matches.
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
                        // 260907Codex: Reuse exact cells from prior plans too, once per new plan; their original associations remain intact.
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
            // 260907Codex: Keep the original fields backward-compatible and add a separate association for each later plan.
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

        // 260901Codex: Distinguish the same mineral's independent shortages by measurement time.
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
            // 260907Codex: Exact-plan training first loads metadata only, then validates its selected Completed rows with the same validator.
            bool deferCompletedSpectrumValidation = false)
        {
            var handle = _repository.ResolvePool(
                request.Paths.SpectrumOutputFolder,
                solution,
                request.Simulation.ResolutionStep,
                request.SemEdxCondition);
            var manifest = _repository.LoadOrCreate(handle);
            bool changed = EnsureNextSimulationIdAfterManifest(manifest);

            // 260620Codex: Clean existing manifest labels so retries and training use percent-grid fractions.
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
                    onCompletedSpectrumChecked, cancellationToken);

            // 260803Codex: 読み取り専用でも同じ修復後データを返し、永続化だけを止めて採取値を維持します。
            if (changed && _persistManifestRepairs)
                _repository.Save(handle, manifest);

            var completedEntries = manifest.Spectra
                .Where(entry => entry.Status == SpectrumManifestStatus.Completed)
                .ToArray();

            return new PoolState(handle, manifest, completedEntries);
        }

        // 260907Codex: Share identical integrity/error handling between legacy full-pool checks and B's exact selected-row checks.
        private static bool ValidateCompletedEntries(
            SpectrumPoolHandle handle,
            SpectrumManifestEntry[] completedEntriesToCheck,
            bool validateCompletedSpectra,
            Action<int, int>? onCompletedSpectrumChecked,
            CancellationToken cancellationToken)
        {
            bool changed = false;
            for (int completedIndex = 0; completedIndex < completedEntriesToCheck.Length; completedIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = completedEntriesToCheck[completedIndex];
                string spectrumPath = Path.Combine(handle.PoolFolder, entry.FileName);
                bool valid = File.Exists(spectrumPath);
                string failureReason = valid ? string.Empty : "出力ファイルが見つかりませんでした。";
                if (valid && validateCompletedSpectra)
                    // 260901Codex: Training and explicit integrity paths retain full EMSA validation; generation resumes use the fast existence check.
                    valid = EmsaSpectrumIntegrityValidator.TryValidate(
                        spectrumPath,
                        handle.Condition.SemEdxCondition,
                        out failureReason);

                if (!valid)
                {
                    entry.Status = SpectrumManifestStatus.Missing;
                    entry.FailureReason = failureReason;
                    changed = true;
                }

                onCompletedSpectrumChecked?.Invoke(completedIndex + 1, completedEntriesToCheck.Length);
            }

            return changed;
        }

        // 260902Codex: Keep preparation progress reporting in one place so worker-thread scans never touch WinForms controls.
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
            // 260901Codex: Preserve the allocation condition and target on every reservation for multi-time job packing.
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
            // 260901Codex: Job construction uses the request condition while the handle supplies the resolved compatible pool identity.
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

        // 260620Codex: Reuse the same manifest lookup path for cleanup and DTSA-II reservation inputs.
        private static double[] ReadEndmemberFractions(SolidSolution solution, SpectrumManifestEntry entry) =>
            solution.Members
                .Select(member => TryGetFraction(entry.EndmemberFractions, member.Name, out double fraction)
                    ? fraction
                    : 0)
                .ToArray();

        // 260620Codex: Normalize old manifest entries in place, including tiny negative values from past runs.
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

        // 260620Codex: Keep comparisons exact so cleaned JSON values are written back once.
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

        // 260620Codex: Accept legacy case differences without preserving duplicate endmember keys.
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

        // 260901Codex: Apply each save marker independently and validate the full EMSA before checkpointing it as Completed.
        private static void ApplyResultToEntry(
            SimulationExecutionResult result,
            SpectrumSimulationReservation reservation,
            SpectrumManifestEntry entry)
        {
            // 260606Claude: 正常終了の全件保存に加え、保存完了マーカーが出た spectrum も「保存済み」とみなします。
            bool cleanExit = !result.IsCanceled && result.ExitCode == 0 && result.ExceptionMessage is null;
            bool saved = cleanExit || (result.SavedSpectrumFiles?.Contains(reservation.FileName) ?? false);

            string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
            // 260901Codex: Keep a deterministic Missing reason when no save marker or clean exit permits validation.
            string validationFailureReason = "DTSA-II の保存完了を確認できませんでした。";
            // 260901Codex: Never checkpoint a partial or physically mismatched EMSA as Completed.
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

            // 260901Codex: A clean process exit with an absent or invalid EMSA is retried through the Missing path.
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

        // 260901Codex: Reuse the one validated manifest snapshot throughout exact multi-time training selection.
        private sealed record TrainingPoolSelectionState(
            SolidSolution Solution,
            SpectrumTimeAllocation Allocation,
            PoolState State);

        // 260907Codex: One mineral's global plan keeps the normal condition-homogeneous pools while sharing one composition schedule.
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
