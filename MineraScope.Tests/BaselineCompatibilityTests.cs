using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 260907Codex: Compare the actual pre-change binary's planner with the current implementation using only synthetic in-memory inputs.
    internal static class BaselineCompatibilityTests
    {
        public static void Run(string assemblyPath)
        {
            string path = Path.GetFullPath(assemblyPath);
            TestAssert.True(File.Exists(path), "The explicitly supplied historical assembly must exist.");
            TestAssert.True(!string.Equals(path, typeof(SpectrumCompositionTimePlanner).Assembly.Location, StringComparison.OrdinalIgnoreCase), "The historical comparison must not compare the current assembly to itself.");
            string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Console.WriteLine($"Historical assembly SHA256: {originalHash}");
            var context = new AssemblyLoadContext("PresetBHistoricalComparison", isCollectible: true);
            try
            {
                var baseline = context.LoadFromAssemblyPath(path);
                var plannerType = baseline.GetType("MineraScope.SpectrumCompositionTimePlanner", throwOnError: true)!;
                var solutionType = baseline.GetType("MineraScope.SolidSolution", throwOnError: true)!;
                var allocationType = baseline.GetType("MineraScope.SpectrumTimeAllocation", throwOnError: true)!;
                var create = plannerType.GetMethod("Create", BindingFlags.Public | BindingFlags.Instance)!;
                TestAssert.Equal(5, create.GetParameters().Length, "The baseline must expose the original five-argument planner API.");
                var historicalPlanner = Activator.CreateInstance(plannerType, nonPublic: true)!;
                foreach (var (total, grid, timeCount) in new[] { (8, 10, 5), (8, 3, 5), (11, 4, 3), (1000, 101, 5) })
                {
                    var solution = TestFixture.CreateSolution();
                    double resolution = 1.0 / (grid - 1);
                    var allocations = new SpectrumTimeSchedule(total,
                        Enumerable.Range(1, timeCount).Select(time => new SpectrumTimeWeight(time, 1)).ToArray()).CreateAllocations();
                    var conditionKeys = allocations.ToDictionary(item => item.LiveTime,
                        item => "test-condition-" + item.LiveTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var historicalSolution = JsonSerializer.Deserialize(JsonSerializer.Serialize(solution), solutionType)!;
                    var historicalAllocations = JsonSerializer.Deserialize(JsonSerializer.Serialize(allocations), allocationType.MakeArrayType())!;
                    var historicalResult = create.Invoke(historicalPlanner, [historicalSolution, resolution, historicalAllocations, conditionKeys, 42])!;
                    var historicalPlan = JsonSerializer.Deserialize<SpectrumCompositionTimePlan>(JsonSerializer.Serialize(historicalResult, historicalResult.GetType()))!;
                    var currentPlan = new SpectrumCompositionTimePlanner().Create(solution, resolution, allocations, conditionKeys);
                    string description = $"N={total}, G={grid}, K={timeCount}";
                    TestAssert.Equal(historicalPlan.PlanId, currentPlan.PlanId, $"{description}: the original equal-plan ID changed.");
                    TestAssert.Equal(historicalPlan.PlanFingerprint, currentPlan.PlanFingerprint, $"{description}: the original equal-plan fingerprint changed.");
                    TestAssert.Equal(JsonSerializer.Serialize(historicalPlan), JsonSerializer.Serialize(currentPlan), $"{description}: historical equal-plan metadata, row order, hashes, fractions, or repeat IDs changed.");
                    Console.WriteLine($"Historical equal plan {description}: {historicalPlan.PlanFingerprint}");
                }
            }
            finally
            {
                context.Unload();
            }

            TestAssert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), "Historical assembly contents must remain unchanged.");
        }
    }
}
