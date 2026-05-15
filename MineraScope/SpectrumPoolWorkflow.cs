using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MineraScope
{
    // 260507Codex: モデル作成前に表示する鉱物ごとの不足数です。
    internal sealed record SpectrumPoolShortage(
        string MineralName,
        int RequiredCount,
        int CompletedCount)
    {
        public int MissingCount => Math.Max(0, RequiredCount - CompletedCount);
    }

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
        private readonly Random _random = new();

        public SpectrumPoolWorkflow(
            SpectrumPoolRepository repository,
            SimulationPlanBuilder simulationPlanBuilder)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _simulationPlanBuilder = simulationPlanBuilder ?? throw new ArgumentNullException(nameof(simulationPlanBuilder));
        }

        // 260507Codex: モデル作成前に Completed 件数だけを数え、不足があれば呼び出し元で学習を止めます。
        public IReadOnlyList<SpectrumPoolShortage> GetShortages(ModelCreationRequest request)
        {
            int targetCount = request.Simulation.TargetSpectrumCount;
            var shortages = new List<SpectrumPoolShortage>();

            foreach (var solution in request.SelectedMineralSolutions)
            {
                var state = LoadState(request, solution);
                int completedCount = state.CompletedEntries.Count;
                if (completedCount < targetCount)
                    shortages.Add(new SpectrumPoolShortage(solution.Name, targetCount, completedCount));
            }

            return shortages;
        }

        // 260513Codex: 選択中鉱物の pool manifest を読み直し、status ごとの件数を合算します。
        public SpectrumPoolStatusCounts GetStatusCounts(ModelCreationRequest request)
        {
            int completed = 0;
            int failed = 0;
            int missing = 0;
            int pending = 0;

            foreach (var solution in request.SelectedMineralSolutions)
            {
                var state = LoadState(request, solution);
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

            return new SpectrumPoolStatusCounts(completed, failed, missing, pending);
        }

        // 260507Codex: 全 pool が十分なときだけ、各鉱物から target 件をランダム抽出して学習入力にします。
        public IReadOnlyList<SpectrumTrainingPool> CreateTrainingPools(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            shortages = GetShortages(request);
            if (shortages.Count > 0)
                return [];

            int targetCount = request.Simulation.TargetSpectrumCount;
            var pools = new List<SpectrumTrainingPool>(request.SelectedMineralSolutions.Count);

            foreach (var solution in request.SelectedMineralSolutions)
            {
                var state = LoadState(request, solution);
                var selectedEntries = state.CompletedEntries
                    .OrderBy(_ => _random.Next())
                    .Take(targetCount)
                    .ToArray();

                var samples = selectedEntries
                    .Select(entry => new SpectrumTrainingSample(
                        Path.Combine(state.Handle.PoolFolder, entry.FileName),
                        entry.EndmemberFractions))
                    .ToArray();

                pools.Add(new SpectrumTrainingPool(
                    solution.Name,
                    solution.Members.Select(member => member.Name).ToArray(),
                    samples));
            }

            return pools;
        }

        // 260507Codex: 不足分だけ simulationId と出力ファイル名を予約し、実行 plan を返します。
        public SimulationExecutionPlan CreateMissingSimulationPlan(
            ModelCreationRequest request,
            out IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            shortages = GetShortages(request);
            var reservations = new List<SpectrumSimulationReservation>();

            foreach (var shortage in shortages.Where(item => item.MissingCount > 0))
            {
                var solution = request.SelectedMineralSolutions
                    .First(item => string.Equals(item.Name, shortage.MineralName, StringComparison.OrdinalIgnoreCase));
                var state = LoadState(request, solution);
                reservations.AddRange(ReserveMissingSpectra(solution, state, request.Simulation.ResolutionStep, shortage.MissingCount));
                _repository.Save(state.Handle, state.Manifest);
            }

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

        // 260507Codex: ユーザーへ出す不足メッセージを workflow 側で共通化します。
        public static string FormatShortageMessage(IEnumerable<SpectrumPoolShortage> shortages) =>
            string.Join(
                Environment.NewLine,
                shortages
                    .Where(shortage => shortage.MissingCount > 0)
                    .Select(shortage => $"{shortage.MineralName} は {shortage.MissingCount} 件不足しています。"));

        // 260507Codex: manifest を読み、Completed でも実体がないものは Missing に更新して保存します。
        private PoolState LoadState(ModelCreationRequest request, SolidSolution solution)
        {
            var handle = _repository.ResolvePool(
                request.Paths.SpectrumOutputFolder,
                solution,
                request.Simulation.ResolutionStep,
                request.SemEdxCondition);
            var manifest = _repository.LoadOrCreate(handle);
            bool changed = _repository.RefreshCompletedStatuses(handle, manifest);

            if (changed)
                _repository.Save(handle, manifest);

            var completedEntries = manifest.Spectra
                .Where(entry => entry.Status == SpectrumManifestStatus.Completed)
                .ToArray();

            return new PoolState(handle, manifest, completedEntries);
        }

        // 260513Codex: Pending/Failed/Missing を先に再利用し、不足分だけ新しい simulationId を発行します。
        private IReadOnlyList<SpectrumSimulationReservation> ReserveMissingSpectra(
            SolidSolution solution,
            PoolState state,
            double resolutionStep,
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
                reservations.Add(CreateReservation(solution, state.Handle, entry));
            }

            int newReservationCount = missingCount - reservations.Count;
            if (newReservationCount <= 0)
                return reservations;

            var candidates = solution.EnumerateCandidateFractions(resolutionStep);
            if (candidates.Length == 0)
                return reservations;

            for (int i = 0; i < newReservationCount; i++)
            {
                double[] fractions = candidates[_random.Next(candidates.Length)];
                int simulationId = state.Manifest.NextSimulationId++;
                string fileName = $"{SpectrumPoolRepository.SanitizeFileName(solution.Name)}_sim{simulationId:D6}.emsa";
                var entry = new SpectrumManifestEntry
                {
                    SimulationId = simulationId,
                    FileName = fileName,
                    Status = SpectrumManifestStatus.Pending,
                    EndmemberFractions = solution.CreateEndmemberFractionMap(fractions)
                };

                state.Manifest.Spectra.Add(entry);
                reservations.Add(CreateReservation(solution, state.Handle, entry));
            }

            return reservations;
        }

        // 260513Codex: manifest の endmemberFractions を鉱物定義順の配列へ戻して DTSA-II 入力を再構築します。
        private static SpectrumSimulationReservation CreateReservation(
            SolidSolution solution,
            SpectrumPoolHandle handle,
            SpectrumManifestEntry entry)
        {
            var fractions = solution.Members
                .Select(member => entry.EndmemberFractions.TryGetValue(member.Name, out double fraction)
                    ? fraction
                    : entry.EndmemberFractions
                        .Where(pair => string.Equals(pair.Key, member.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(pair => pair.Value)
                        .FirstOrDefault())
                .ToArray();

            return new SpectrumSimulationReservation(
                solution.Name,
                handle.PoolFolder,
                handle.ManifestPath,
                entry.SimulationId,
                entry.FileName,
                solution.CalculateCompositionWeights(fractions));
        }

        // 260513Codex: キャンセルは失敗扱いにせず、同じ entry を次回再生成できる Pending に戻します。
        private static void ApplyResultToEntry(
            SimulationExecutionResult result,
            SpectrumSimulationReservation reservation,
            SpectrumManifestEntry entry)
        {
            if (result.IsCanceled)
            {
                entry.Status = SpectrumManifestStatus.Pending;
                entry.FailureReason = null;
                return;
            }

            if (result.ExitCode != 0 || result.ExceptionMessage is not null)
            {
                entry.Status = SpectrumManifestStatus.Failed;
                entry.FailureReason = BuildFailureReason(result);
                return;
            }

            string outputPath = Path.Combine(reservation.PoolFolder, reservation.FileName);
            if (SpectrumDataLoader.LoadNormalizedSpectrum(outputPath) is null)
            {
                entry.Status = SpectrumManifestStatus.Missing;
                entry.FailureReason = "DTSA-II は正常終了しましたが、出力 spectrum を学習用に読み込めません。";
                return;
            }

            entry.Status = SpectrumManifestStatus.Completed;
            entry.FailureReason = null;
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
    }
}
