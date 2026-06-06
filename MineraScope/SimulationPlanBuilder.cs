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
        (string ElementName, double Weight)[] CompositionWeights);

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
        IReadOnlySet<string>? SavedSpectrumFiles = null);

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
        IReadOnlyList<SimulationExecutionBatch> Batches);

    // 260507Codex: manifest 予約済み spectrum から DTSA-II 実行 plan を組み立てます。
    internal sealed class SimulationPlanBuilder
    {
        // 260507Codex: 予約済み spectrum の配列を並列ジョブへ均等に分けます。
        public SimulationExecutionPlan CreatePlan(
            ModelCreationRequest request,
            IReadOnlyList<SpectrumSimulationReservation> reservations)
        {
            if (reservations.Count == 0)
                return new SimulationExecutionPlan([]);

            int parallelCount = Math.Max(1, request.Simulation.ParallelCount);
            var batches = new List<SimulationExecutionBatch>();

            foreach (var group in reservations.GroupBy(item => item.SolutionName))
            {
                var chunks = SplitIntoChunks(group.ToArray(), parallelCount);
                var jobs = new List<SimulationExecutionJob>(chunks.Length);
                int index = 0;

                foreach (var chunk in chunks.Where(chunk => chunk.Length > 0))
                {
                    // 260508Codex: 一度だけ使う薄い helper を避け、job 入力の形をここで明示します。
                    var elements = chunk.Select(item => item.CompositionWeights).ToArray();
                    var outputFiles = chunk.Select(item => item.FileName).ToArray();
                    var property = CreateSimulationProperty(
                        request.SemEdxCondition,
                        request.Simulation,
                        group.Key,
                        elements,
                        chunk[0].PoolFolder,
                        outputFiles);

                    jobs.Add(new SimulationExecutionJob(
                        Path.Combine(
                            request.Paths.ScriptOutputFolder,
                            $"{SpectrumPoolRepository.SanitizeFileName(group.Key)}_{index + 1}.py"),
                        request.Paths.DtsaFolder,
                        property,
                        index,
                        chunk));

                    index++;
                }

                batches.Add(new SimulationExecutionBatch(group.Key, jobs));
            }

            return new SimulationExecutionPlan(batches);
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
                DetectorName = semEdxCondition.DetectorName,
                CarbonCoatThickness = semEdxCondition.CarbonCoatThickness,
                BeamEnergy = semEdxCondition.BeamEnergy,
                Division = (int)(simulation.ResolutionStep * 100),
                LiveTime = semEdxCondition.LiveTime,
                ProbeCurrent = semEdxCondition.ProbeCurrent,
                ParallelCount = simulation.ParallelCount,
                OutputFolder = outputFolder,
                OutputFiles = outputFiles
            };

        // 260507Codex: index の剰余で分散し、各ジョブの予約数が偏りすぎないようにします。
        public static T[][] SplitIntoChunks<T>(T[] source, int chunkCount) =>
            source
                .Select((value, index) => new { value, index })
                .GroupBy(item => item.index % chunkCount)
                .Select(group => group.Select(item => item.value).ToArray())
                .ToArray();
    }
}
