// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MineraScope
{
    // 260507Codex: manifest に予約済みの spectrum を DTSA-II 実行単位へ渡します。
    internal sealed record SpectrumSimulationReservation(
        string SolutionName,
        string PoolFolder,
        string ManifestPath,
        int SimulationId,
        string FileName,
        (string ElementName, double Weight)[] CompositionWeights,
        // 260901Codex: 1つの鉱物バッチに複数のライブタイムが含まれるため、予約に条件を持たせる。
        string ConditionKey,
        SemEdxCondition SemEdxCondition,
        int TargetSpectrumCount);

    // 260507Codex: 1 つの Python スクリプトで実行する予約 spectrum 群です。
    internal sealed record SimulationExecutionJob(
        string ScriptPath,
        string DtsaFolder,
        SimulationProperty Property,
        int ParallelIndex,
        IReadOnlyList<SpectrumSimulationReservation> Reservations);

    // 260507Codex: 鉱物ごとにジョブを束ね、manifest 更新時に単位を追いやすくします。
    internal sealed record SimulationExecutionBatch(
        string SolutionName,
        IReadOnlyList<SimulationExecutionJob> Jobs);

    // 260513Codex: キャンセル結果も manifest 更新側へ返し、対象 entry を Pending に戻せるようにします。
    // 260606Claude: SavedSpectrumFiles は DTSA-II が保存完了マーカーを出した spectrum ファイル名集合で、ジョブが途中失敗/キャンセルでも保存済み分だけ Completed として残すために使います。
    internal sealed record SimulationExecutionResult(
        IReadOnlyList<SpectrumSimulationReservation> Reservations,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string? ExceptionMessage,
        bool IsCanceled = false,
        IReadOnlySet<string>? SavedSpectrumFiles = null)
    {
        // 261105Claude: manifest の Pending 復帰はこの形を読むので、scheduler と service で作り方を共有する。
        public static SimulationExecutionResult Canceled(IReadOnlyList<SpectrumSimulationReservation> reservations) =>
            new(reservations, ExitCode: -1, StandardOutput: string.Empty, StandardError: string.Empty, ExceptionMessage: null, IsCanceled: true);
    }

    // 260528Codex: DTSA-II 実行中の進捗を UI へ渡すため、外部実行 service から段階ごとに通知します。
    internal enum SimulationExecutionProgressKind
    {
        BatchStarted,
        JobStarted,
        ScriptWritten,
        ProcessStarted,
        SpectrumSaved,
        JobCompleted,
        JobFailed,
        JobCanceled,
        BatchCompleted
    }

    // 260528Codex: status strip と訓練ログの両方で使えるよう、job 件数と spectrum 件数をまとめて運びます。
    internal sealed record SimulationExecutionProgress(
        SimulationExecutionProgressKind Kind,
        string SolutionName,
        int BatchIndex,
        int BatchCount,
        int JobIndex,
        int TotalJobCount,
        int CompletedJobCount,
        int TotalSpectrumCount,
        int CompletedSpectrumCount,
        int SpectrumCount,
        string Message,
        int? ExitCode = null,
        TimeSpan? Elapsed = null);

    // 260507Codex: 実行 plan は予約済み spectrum を並列数で分割した batch の集合です。
    internal sealed record SimulationExecutionPlan(
        IReadOnlyList<SimulationExecutionBatch> Batches,
        // 261103Codex: 分割済みjob数とは別にrun全体の同時実行上限を伝える。
        // 261105Claude: 既定値を持たせず、書き忘れた呼び出しが黙って直列にならないようにする。
        int ParallelCount);

    // 260901Codex: 試行確認は処理量だけを報告し、予約・スクリプト・フォルダー・manifestを作らない。
    internal sealed record SimulationExecutionPlanPreview(
        int BatchCount,
        int JobCount,
        int SpectrumCount);

    // 260507Codex: manifest 予約済み spectrum から DTSA-II 実行 plan を組み立てます。
    internal sealed class SimulationPlanBuilder
    {
        // 260507Codex: 予約済み spectrum の配列を並列ジョブへ均等に分けます。
        public SimulationExecutionPlan CreatePlan(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumSimulationReservation> reservations)
        {
            // 261103Codex: 空計画でも要求された実行上限を保持する。
            int parallelCount = Math.Max(1, request.Simulation.ParallelCount);
            var batches = new List<SimulationExecutionBatch>();

            foreach (var group in reservations.GroupBy(item => item.SolutionName))
            {
                // 260901Codex: スクリプト内の条件を均一にし、全時間で1つの並列ジョブ枠を共有する。
                var conditionGroups = group
                    .GroupBy(item => item.ConditionKey, StringComparer.Ordinal)
                    .OrderBy(items => items.First().SemEdxCondition.LiveTime)
                    .ThenBy(items => items.Key, StringComparer.Ordinal)
                    .Select(items => new ConditionReservationGroup(items.Key, items.ToArray()))
                    .ToArray();
                if (conditionGroups.Length > parallelCount)
                    throw new InvalidOperationException(
                        $"{group.Key} の測定時間条件数 {conditionGroups.Length} が並列数 {parallelCount} を超えています。");

                int[] jobCounts = AllocateJobCounts(conditionGroups, parallelCount);
                var jobs = new List<SimulationExecutionJob>(jobCounts.Sum());
                int parallelIndex = 0;

                for (int conditionIndex = 0; conditionIndex < conditionGroups.Length; conditionIndex++)
                {
                    var conditionGroup = conditionGroups[conditionIndex];
                    var chunks = SplitIntoChunks(conditionGroup.Reservations, jobCounts[conditionIndex]);
                    int conditionJobIndex = 0;

                    foreach (var chunk in chunks.Where(chunk => chunk.Length > 0))
                    {
                        // 260901Codex: プロパティのライブタイムは、同じバッチの別ジョブではなくこの条件群から取得する。
                        var property = CreateSimulationProperty(
                            chunk[0].SemEdxCondition,
                            request.Simulation,
                            group.Key,
                            chunk.Select(item => item.CompositionWeights).ToArray(),
                            chunk[0].PoolFolder,
                            chunk.Select(item => item.FileName).ToArray());

                        jobs.Add(new SimulationExecutionJob(
                            Path.Combine(
                                request.Paths.ScriptOutputFolder,
                                $"{SpectrumPoolRepository.SanitizeFileName(group.Key)}_{SpectrumPoolRepository.SanitizeFileName(conditionGroup.ConditionKey)}_{conditionJobIndex + 1:D2}.py"),
                            request.Paths.DtsaFolder,
                            property,
                            parallelIndex,
                            chunk));

                        parallelIndex++;
                        conditionJobIndex++;
                    }
                }

                batches.Add(new SimulationExecutionBatch(group.Key, jobs));
            }

            // 261103Codex: 予約・ジョブ分割を変えず、全バッチで共有する実行上限だけを添える。
            return new SimulationExecutionPlan(batches, parallelCount);
        }

        // 260901Codex: 本番のジョブ枠配分を再利用し、副作用のない不足プレビューを行う。
        public SimulationExecutionPlanPreview CreatePreview(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            int parallelCount = Math.Max(1, request.Simulation.ParallelCount);
            var activeMinerals = shortages
                .Where(item => item.MissingCount > 0)
                .GroupBy(item => item.MineralName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int jobCount = 0;

            foreach (var mineral in activeMinerals)
            {
                var demands = mineral
                    .OrderBy(item => item.LiveTime)
                    .ThenBy(item => item.ConditionKey, StringComparer.Ordinal)
                    .Select(item => new ConditionJobDemand(
                        item.LiveTime,
                        item.RequiredCount,
                        item.MissingCount))
                    .ToArray();
                jobCount += AllocateJobCounts(demands, parallelCount).Sum();
            }

            return new SimulationExecutionPlanPreview(
                activeMinerals.Length,
                jobCount,
                shortages.Sum(item => item.MissingCount));
        }

        // 260901Codex: 可能なら再試行用のジョブサイズを保ち、複数時間の作業を比率に応じて1鉱物バッチへ圧縮する。
        private static int[] AllocateJobCounts(
            IReadOnlyList<ConditionReservationGroup> conditionGroups,
            int parallelCount) =>
            AllocateJobCounts(
                conditionGroups
                    .Select(group => new ConditionJobDemand(
                        group.Reservations[0].SemEdxCondition.LiveTime,
                        group.Reservations[0].TargetSpectrumCount,
                        group.Reservations.Length))
                    .ToArray(),
                parallelCount);

        // 260901Codex: 共通の需要形式で、実計画と試行確認のジョブ件数を一致させる。
        private static int[] AllocateJobCounts(
            IReadOnlyList<ConditionJobDemand> demands,
            int parallelCount)
        {
            if (demands.Count > parallelCount)
                throw new InvalidOperationException("測定時間条件数を並列数以下にしてください。");

            var desiredJobCounts = demands
                .Select(demand =>
                {
                    int standardJobSize = Math.Max(1, DivideRoundUp(demand.TargetSpectrumCount, parallelCount));
                    return Math.Min(parallelCount, DivideRoundUp(demand.ReservationCount, standardJobSize));
                })
                .ToArray();
            int jobBudget = Math.Min(parallelCount, desiredJobCounts.Sum());
            if (jobBudget < demands.Count)
                throw new InvalidOperationException("各測定時間へ1 job以上を割り当てられる並列数が必要です。");

            var allocated = Enumerable.Repeat(1, demands.Count).ToArray();
            while (allocated.Sum() < jobBudget)
            {
                int next = Enumerable.Range(0, demands.Count)
                    .Where(index => allocated[index] < desiredJobCounts[index])
                    .OrderByDescending(index => (double)desiredJobCounts[index] / allocated[index])
                    .ThenBy(index => demands[index].LiveTime)
                    .First();
                allocated[next]++;
            }

            return allocated;
        }

        // 260507Codex: 既存 script generator が必要とする DTO だけを予約情報から作ります。
        private static SimulationProperty CreateSimulationProperty(
            SemEdxCondition semEdxCondition,
            SimulationExecutionSettings simulation,
            string mineralGroupName,
            (string ElementName, double Weight)[][] atoms,
            string outputFolder,
            string[] outputFiles) =>
            new()
            {
                MineralGroupName = mineralGroupName,
                Atoms1 = atoms,
                DetectorProfile = semEdxCondition.GetDetectorProfile(),
                // 261021Codex: 条件キーと同じ指定膜厚を全スペクトルへ渡す。
                CarbonCoatThickness = semEdxCondition.CarbonCoatThickness,
                BeamEnergy = semEdxCondition.BeamEnergy,
                Division = (int)(simulation.ResolutionStep * 100),
                LiveTime = semEdxCondition.LiveTime,
                ProbeCurrent = semEdxCondition.ProbeCurrent,
                ParallelCount = simulation.ParallelCount,
                OutputFolder = outputFolder,
                OutputFiles = outputFiles
            };

        // 260717Codex: 両方のジョブサイズ計算で使う、正整数の切上げ除算に名前を付ける。
        private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;

        // 260507Codex: index の剰余で分散し、各ジョブの予約数が偏りすぎないようにします。
        public static T[][] SplitIntoChunks<T>(T[] source, int chunkCount) =>
            source
                .Select((value, index) => new { value, index })
                .GroupBy(item => item.index % chunkCount)
                .Select(group => group.Select(item => item.value).ToArray())
                .ToArray();

        // 260901Codex: 条件キーのpoolを混ぜず、グループ情報でジョブ配分を決定的にする。
        private sealed record ConditionReservationGroup(
            string ConditionKey,
            SpectrumSimulationReservation[] Reservations);

        // 260901Codex: 制限付きジョブ配分に影響するのは、件数とライブタイムだけである。
        private sealed record ConditionJobDemand(
            double LiveTime,
            int TargetSpectrumCount,
            int ReservationCount);
    }
}
