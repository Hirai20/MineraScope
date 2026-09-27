using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineraScope.Tests
{
    // 260907Codex: Every fixture owns a unique TEMP child; request paths can never address the user's Documents or research pools.
    internal sealed class TestFixture : IDisposable
    {
        private const string DirectoryPrefix = "MineraScope-PresetB-tests-";
        private readonly string _temporaryParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);

        public string Root { get; }
        public ModelCreationRequest Request { get; }
        public SpectrumPoolRepository Repository { get; } = new(new SpectrumConditionKeyBuilder());
        public SpectrumPoolWorkflow Workflow { get; }

        // 260907Codex: Small binary solids and five-time schedules cover pool behavior with only a few synthetic files.
        public TestFixture(SpectrumTimeSchedule? schedule = null, params SolidSolution[] solutions)
        {
            Root = Path.Combine(_temporaryParent, DirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            schedule ??= new SpectrumTimeSchedule(8, [new(1, 1), new(5, 1), new(10, 1), new(20, 1), new(50, 1)]);
            Request = new ModelCreationRequest(
                new ModelCreationPaths(Path.Combine(Root, "spectra"), Path.Combine(Root, "scripts"), Path.Combine(Root, "dtsa"), Path.Combine(Root, "models")),
                "SyntheticTestModel",
                new SemEdxCondition(DetectorProfile.CreateLegacyTest(), 0, 15, 1, 1),
                new SimulationExecutionSettings(schedule.TotalSpectrumCount, 0.5, Math.Max(5, schedule.TimeWeights.Count), 0),
                new ModelTrainingSettings(1, 4, 1, 0.2f, 1.5),
                solutions.Length == 0 ? [CreateSolution()] : solutions)
            {
                SpectrumTimeSchedule = schedule
            };
            Workflow = new SpectrumPoolWorkflow(Repository, new SimulationPlanBuilder());
        }

        // 260907Codex: Real formula parsing is exercised using common endmembers; no mineral database file is loaded.
        public static SolidSolution CreateSolution(string name = "SyntheticOlivine") =>
            new(name, "(Mg,Fe)2SiO4", [new Mineral("Forsterite", "Mg2SiO4"), new Mineral("Fayalite", "Fe2SiO4")], []);

        // 260907Codex: Resolve fixtures through the production repository so condition keys and manifest paths follow the real workflow.
        public SpectrumPoolHandle Handle(SolidSolution solution, double liveTime) =>
            Repository.ResolvePool(Request.Paths.SpectrumOutputFolder, solution, Request.Simulation.ResolutionStep, Request.SemEdxCondition with { LiveTime = liveTime });

        // 260907Codex: Flatten reservations once for cancellation, retry, and saved-file assertions.
        public static SpectrumSimulationReservation[] Reservations(SimulationExecutionPlan plan) =>
            plan.Batches.SelectMany(batch => batch.Jobs).SelectMany(job => job.Reservations).ToArray();

        // 260907Codex: The fixture contains full 2048-channel finite spectra and physical headers, not placeholder files accepted only by existence checks.
        public void WriteValidSpectrum(string path, SemEdxCondition condition)
        {
            AssertOwnedPath(path);
            var detector = condition.GetDetectorProfile();
            string Header(string key, double value) => $"#{key}: {value.ToString("G17", CultureInfo.InvariantCulture)}";
            var lines = new List<string>
            {
                "#FORMAT: EMSA/MAS Spectral Data File",
                Header("NPOINTS", SpectrumDataLoader.SpectrumLength),
                Header("BEAMKV", condition.BeamEnergy),
                Header("XPERCHAN", detector.ChannelWidth),
                Header("OFFSET", detector.ZeroOffset),
                Header("ELEVANGLE", detector.Elevation),
                Header("AZIMANGLE", detector.Azimuth),
                "#DATATYPE: Y",
                "#SPECTRUM:"
            };
            lines.AddRange(Enumerable.Range(0, SpectrumDataLoader.SpectrumLength).Select(index => (index % 17 + 1).ToString(CultureInfo.InvariantCulture)));
            lines.Add("#ENDOFDATA:");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            TestAssert.True(EmsaSpectrumIntegrityValidator.TryValidate(path, condition, out string reason), $"Synthetic EMSA must be valid: {reason}");
        }

        // 260907Codex: Each result refers to one reservation, avoiding filename collisions across independent time pools.
        public void Complete(IEnumerable<SpectrumSimulationReservation> reservations)
        {
            foreach (var reservation in reservations)
            {
                WriteValidSpectrum(Path.Combine(reservation.PoolFolder, reservation.FileName), reservation.SemEdxCondition);
                Workflow.ApplySimulationResults([new SimulationExecutionResult([reservation], 0, string.Empty, string.Empty, null)]);
            }
        }

        // 260907Codex: Capture file content, write time, and directory inventory to prove preview does not persist repairs or create plan files.
        public string[] Snapshot() =>
            Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
                .Select(path => $"D:{Path.GetRelativePath(Root, path)}")
                .Concat(Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(path =>
                    $"F:{Path.GetRelativePath(Root, path)}:{File.GetLastWriteTimeUtc(path).Ticks}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}"))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        // 260907Codex: Read the exact persisted plan to assert that source identities survive restoration and training.
        public SpectrumCompositionTimePlan ReadPlan(SolidSolution solution)
        {
            string folder = Path.Combine(Request.Paths.SpectrumOutputFolder, SpectrumPoolRepository.SanitizeFileName(solution.Name), "_plans");
            string path = Directory.GetFiles(folder, "*.json").Single();
            return JsonSerializer.Deserialize<SpectrumCompositionTimePlan>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("The fixture plan could not be deserialized.");
        }

        // 260907Codex: Only named descendants of this fixture may be deliberately damaged or deleted by a test.
        public void AssertOwnedPath(string path) =>
            TestAssert.True(Path.GetFullPath(path).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Fixture operation escaped its unique TEMP directory.");

        // 260907Codex: Verify the resolved TEMP parent and unique GUID leaf before removing only this fixture's files.
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Root);
            TestAssert.Equal(_temporaryParent, Path.GetDirectoryName(resolved), "Fixture cleanup parent must remain TEMP.");
            string leaf = Path.GetFileName(resolved);
            TestAssert.True(leaf.StartsWith(DirectoryPrefix, StringComparison.Ordinal)
                && Guid.TryParseExact(leaf[DirectoryPrefix.Length..], "N", out _), "Fixture cleanup requires its exact unique directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }
}
