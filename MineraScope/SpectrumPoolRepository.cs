using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
// 260907Codex: Preparation-scoped reads can be canceled between manifests and physical-file checks.
using System.Threading;

namespace MineraScope
{
    // 260513Codex: manifest.json の保存場所決定と読み書きを一か所に集約します。
    internal sealed class SpectrumPoolRepository
    {
        private const string ManifestFileName = "manifest.json";
        private const double ConditionTolerance = 1e-9;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly SpectrumConditionKeyBuilder _conditionKeyBuilder;

        // 260907Codex: Cache only one preparation scope, never the long-lived generation/preview repository.
        private readonly Dictionary<string, CachedManifest>? _preparationManifestCache;
        private readonly CancellationToken _preparationCancellationToken;
        public int ManifestReadCount { get; private set; }
        public int ManifestCacheHitCount { get; private set; }

        public SpectrumPoolRepository(SpectrumConditionKeyBuilder conditionKeyBuilder)
        {
            _conditionKeyBuilder = conditionKeyBuilder ?? throw new ArgumentNullException(nameof(conditionKeyBuilder));
        }

        // 260907Codex: The caller owns this single-threaded, short-lived scope and the mutable manifests it shares.
        private SpectrumPoolRepository(SpectrumConditionKeyBuilder conditionKeyBuilder, CancellationToken cancellationToken)
            : this(conditionKeyBuilder)
        {
            _preparationManifestCache = new(StringComparer.OrdinalIgnoreCase);
            _preparationCancellationToken = cancellationToken;
        }

        // 260907Codex: Use a fresh repository for each mineral's recovery and training preparation, then release it.
        public SpectrumPoolRepository CreatePreparationRepository(CancellationToken cancellationToken = default) =>
            new(_conditionKeyBuilder, cancellationToken);

        // 260513Codex: 鉱物名と conditionKey から pool フォルダを決め、ファイル名に使えない文字を除きます。
        public SpectrumPoolHandle ResolvePool(
            string spectrumOutputFolder,
            SolidSolution solution,
            double resolutionStep,
            SemEdxCondition semEdxCondition)
        {
            // 260907Codex: Do not start catalog discovery after preparation has been canceled.
            _preparationCancellationToken.ThrowIfCancellationRequested();
            var snapshot = _conditionKeyBuilder.CreateSnapshot(solution, resolutionStep, semEdxCondition);
            string conditionKey = _conditionKeyBuilder.BuildKey(snapshot);
            string mineralFolder = Path.Combine(
                spectrumOutputFolder,
                SanitizeFileName(solution.Name));
            var exactHandle = CreateHandle(mineralFolder, conditionKey, snapshot);

            return ResolveReusableHandle(mineralFolder, exactHandle, snapshot);
        }

        // 260513Codex: 既存 manifest があれば読み込み、なければ新規の空 manifest を作ります。
        public SpectrumPoolManifest LoadOrCreate(SpectrumPoolHandle handle)
        {
            // 260907Codex: Also honor cancellation when a pool has no existing manifest.
            _preparationCancellationToken.ThrowIfCancellationRequested();
            var manifest = File.Exists(handle.ManifestPath)
                ? Load(handle.ManifestPath)
                : null;

            if (manifest is not null)
                return manifest;

            return new SpectrumPoolManifest
            {
                ConditionKey = handle.ConditionKey,
                MineralName = handle.Condition.MineralName,
                Condition = handle.Condition
            };
        }

        // 260513Codex: 実行結果反映では handle がなくても manifest path から直接読めるようにします。
        // 260907Codex: Repeated compatible-pool scans reuse JSON only while its on-disk stamp remains unchanged.
        public SpectrumPoolManifest? Load(string manifestPath)
        {
            _preparationCancellationToken.ThrowIfCancellationRequested();
            string? cacheKey = _preparationManifestCache is null ? null : Path.GetFullPath(manifestPath);
            if (!File.Exists(manifestPath))
            {
                if (cacheKey is not null)
                    _preparationManifestCache!.Remove(cacheKey);
                return null;
            }

            var beforeRead = cacheKey is null ? null : new FileInfo(cacheKey);
            // 260907Codex: FileInfo is lazy, so capture the stamp before reading rather than at the later comparison.
            beforeRead?.Refresh();
            if (cacheKey is not null
                && _preparationManifestCache!.TryGetValue(cacheKey, out var cached)
                && cached.LastWriteTimeUtc == beforeRead!.LastWriteTimeUtc
                && cached.Length == beforeRead.Length)
            {
                ManifestCacheHitCount++;
                return cached.Manifest;
            }

            if (cacheKey is not null)
                _preparationManifestCache!.Remove(cacheKey);
            string json = File.ReadAllText(manifestPath);
            ManifestReadCount++;
            _preparationCancellationToken.ThrowIfCancellationRequested();
            var manifest = JsonSerializer.Deserialize<SpectrumPoolManifest>(json);
            if (cacheKey is not null && manifest is not null)
            {
                var afterRead = new FileInfo(cacheKey);
                if (afterRead.Exists
                    && beforeRead!.LastWriteTimeUtc == afterRead.LastWriteTimeUtc
                    && beforeRead.Length == afterRead.Length)
                    _preparationManifestCache![cacheKey] = new(manifest, afterRead.LastWriteTimeUtc, afterRead.Length);
            }

            return manifest;
        }

        // 260513Codex: pool フォルダを作成してから manifest を保存します。
        public void Save(SpectrumPoolHandle handle, SpectrumPoolManifest manifest)
        {
            Directory.CreateDirectory(handle.PoolFolder);
            Save(handle.ManifestPath, manifest);
        }

        // 260513Codex: 実行結果反映時の manifest 保存も同じ JSON 設定に揃えます。
        public void Save(string manifestPath, SpectrumPoolManifest manifest)
        {
            // 260907Codex: Invalidate even if replacement fails; a later read must not reuse a mutated unsaved object.
            if (_preparationManifestCache is not null)
                _preparationManifestCache.Remove(Path.GetFullPath(manifestPath));
            string? folder = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            // 260901Codex: Batch checkpoints must survive interruption without leaving a truncated manifest.
            DurableJsonFile.Replace(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        }

        // 260513Codex: ファイルシステム上で安全に使える名前へ鉱物名を変換します。
        public static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Mineral" : sanitized.Trim();
        }

        // 260606Codex: Reuse legacy pools when formula text changed but the DTSA-II inputs are chemically identical.
        // 260606Claude: 互換 pool のうち完了スペクトル最多のものを再利用する。完了0(=再利用価値なし)なら新規 handle へフォールバック。
        private SpectrumPoolHandle ResolveReusableHandle(
            string mineralFolder,
            SpectrumPoolHandle exactHandle,
            SpectrumGenerationConditionSnapshot requestedCondition)
        {
            // 260907Codex: Directory enumeration and duplicate ranking remain cancelable without caching EMSA checks.
            _preparationCancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(mineralFolder))
                return exactHandle;

            // 260809Claude: 完了0件の互換 pool も引き継ぐ。検出器名を conditionKey から外したことで既存 pool の
            //   フォルダ名と新しいキーが必ず食い違うため、ここで拾わないと中断済み (Pending/Failed だけの) pool が
            //   孤児になり、予約済み simulationId と失敗履歴が失われる。完了数の多い順で選ぶ方針は変えない。
            //   列挙順は OS 依存なので、同数のときはフォルダ名で決めて解決先を再現可能にする。
            var candidates = Directory.EnumerateDirectories(mineralFolder)
                .Select(poolFolder => CreateReusableCandidate(poolFolder, requestedCondition))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .ToArray();
            if (candidates.Length == 0)
                return exactHandle;
            if (candidates.Length == 1)
                return candidates[0].Handle;

            // 260901Codex: Only duplicate compatible folders require the more expensive physical-file ranking.
            var bestCandidate = candidates
                .OrderByDescending(CountValidCompletedSpectra)
                .ThenBy(candidate => candidate.Handle.PoolFolder, StringComparer.OrdinalIgnoreCase)
                .First();

            return bestCandidate.Handle;
        }

        private ReusablePoolCandidate? CreateReusableCandidate(
            string poolFolder,
            SpectrumGenerationConditionSnapshot requestedCondition)
        {
            // 260907Codex: Check on every enumerated folder, including incompatible conditions.
            _preparationCancellationToken.ThrowIfCancellationRequested();
            string manifestPath = Path.Combine(poolFolder, ManifestFileName);
            var manifest = TryLoadForReuse(manifestPath);
            if (manifest?.Condition is null
                || manifest.Spectra is null
                || !IsGenerationCompatible(requestedCondition, manifest.Condition))
                return null;

            string conditionKey = string.IsNullOrWhiteSpace(manifest.ConditionKey)
                ? new DirectoryInfo(poolFolder).Name
                : manifest.ConditionKey;
            var handle = new SpectrumPoolHandle(poolFolder, manifestPath, conditionKey, manifest.Condition);
            return new ReusablePoolCandidate(handle, manifest);
        }

        // 260901Codex: Prefer the duplicate compatible pool with the most physically valid EMSA files, not a stale Completed count.
        // 260907Codex: Preserve the same physical-file ranking and inspect files anew, with cancellation between entries.
        private int CountValidCompletedSpectra(ReusablePoolCandidate candidate)
        {
            int count = 0;
            foreach (var entry in candidate.Manifest.Spectra)
            {
                _preparationCancellationToken.ThrowIfCancellationRequested();
                if (entry.Status == SpectrumManifestStatus.Completed
                    && EmsaSpectrumIntegrityValidator.TryValidate(
                        Path.Combine(candidate.Handle.PoolFolder, entry.FileName),
                        candidate.Manifest.Condition.SemEdxCondition,
                        out _))
                    count++;
            }

            return count;
        }

        private SpectrumPoolManifest? TryLoadForReuse(string manifestPath)
        {
            try
            {
                return Load(manifestPath);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static SpectrumPoolHandle CreateHandle(
            string mineralFolder,
            string conditionKey,
            SpectrumGenerationConditionSnapshot condition)
        {
            string poolFolder = Path.Combine(mineralFolder, conditionKey);
            return new SpectrumPoolHandle(
                poolFolder,
                Path.Combine(poolFolder, ManifestFileName),
                conditionKey,
                condition);
        }

        private static bool IsGenerationCompatible(
            SpectrumGenerationConditionSnapshot requestedCondition,
            SpectrumGenerationConditionSnapshot manifestCondition) =>
            SameText(requestedCondition.MineralName, manifestCondition.MineralName)
            && IsGenerationSchemaCompatible(requestedCondition.DtsaGenerationSchema, manifestCondition.DtsaGenerationSchema)
            && SameDouble(requestedCondition.CompositionResolution, manifestCondition.CompositionResolution)
            && SameSemEdxCondition(requestedCondition.SemEdxCondition, manifestCondition.SemEdxCondition)
            && SameConstraints(requestedCondition.Constraints, manifestCondition.Constraints)
            && SameEndmembers(requestedCondition.Endmembers, manifestCondition.Endmembers);

        // 260628Claude: 旧 v1 pool は検出器プロファイルを persist せず findDetector("test") で生成された。
        // 実 emsa ヘッダー (ELEVANGLE=35 / AZIMANGLE=90 / XPERCHAN=10 / OFFSET=0 / TDEADLYR=0.2µm / TACTLYR=0.045 / windowless SDWLS) が
        // v2 既定 "test" プロファイルと一致するため、両 schema を同一条件とみなして再生成なしで再利用する。
        // 検出器が既定 test と異なる場合は SameSemEdxCondition の SameDetectorProfile が別途弾くので、この緩和は test 既定のときだけ効く。
        // 260803Codex: ここでいう差は物理フィールドの差を指し、DetectorName / profile Name だけの差は意図どおり許容します。
        private const string LegacyUnknownDetectorSchema = "dtsa2-emsa-v1";
        private const string DetectorProfileSchema = "dtsa2-emsa-v2-detector-profile";

        private static bool IsGenerationSchemaCompatible(string? requested, string? manifest) =>
            SameText(requested, manifest)
            || IsLegacyTestSchemaPair(requested, manifest)
            || IsLegacyTestSchemaPair(manifest, requested);

        private static bool IsLegacyTestSchemaPair(string? detectorProfileSchema, string? legacySchema) =>
            SameText(detectorProfileSchema, DetectorProfileSchema)
            && SameText(legacySchema, LegacyUnknownDetectorSchema);

        private static bool SameSemEdxCondition(SemEdxCondition? left, SemEdxCondition? right) =>
            left is not null
            && right is not null
            // 260803Codex: DetectorName は表示ラベルなので pool の物理的な互換性には含めません。
            && SameDetectorProfile(left.GetDetectorProfile(), right.GetDetectorProfile())
            && SameDouble(left.CarbonCoatThickness, right.CarbonCoatThickness)
            && SameDouble(left.BeamEnergy, right.BeamEnergy)
            && SameDouble(left.LiveTime, right.LiveTime)
            && SameDouble(left.ProbeCurrent, right.ProbeCurrent);

        // 260626Codex: Detector physics affects spectra, so reusable pool checks must match every persisted profile field.
        private static bool SameDetectorProfile(DetectorProfile left, DetectorProfile right) =>
            // 260803Codex: Name だけを除外し、全ての物理フィールド比較は従来どおり維持します。
            left.ChannelCount == right.ChannelCount
            && SameDouble(left.ChannelWidth, right.ChannelWidth)
            && SameDouble(left.ZeroOffset, right.ZeroOffset)
            && SameDouble(left.ResolutionFwhmAtMnKa, right.ResolutionFwhmAtMnKa)
            && SameDouble(left.DetectorArea, right.DetectorArea)
            && SameDouble(left.Elevation, right.Elevation)
            && SameDouble(left.Azimuth, right.Azimuth)
            && SameDouble(left.SpecimenToDetectorDistance, right.SpecimenToDetectorDistance)
            && SameDouble(left.OptimalWorkingDistance, right.OptimalWorkingDistance)
            && SameDouble(left.SiThickness, right.SiThickness)
            && SameDouble(left.AluminumLayer, right.AluminumLayer)
            && SameDouble(left.GoldLayer, right.GoldLayer)
            && SameDouble(left.NickelLayer, right.NickelLayer)
            && SameDouble(left.DeadLayer, right.DeadLayer)
            && left.Window == right.Window;

        private static bool SameConstraints(List<string>? left, List<string>? right)
        {
            string[] leftValues = NormalizeConstraints(left);
            string[] rightValues = NormalizeConstraints(right);
            return leftValues.SequenceEqual(rightValues, StringComparer.Ordinal);
        }

        private static string[] NormalizeConstraints(List<string>? values) =>
            values?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
            ?? [];

        private static bool SameEndmembers(
            List<EndmemberConditionSnapshot>? left,
            List<EndmemberConditionSnapshot>? right)
        {
            var leftValues = NormalizeEndmembers(left);
            var rightValues = NormalizeEndmembers(right);
            if (leftValues.Length != rightValues.Length)
                return false;

            for (int i = 0; i < leftValues.Length; i++)
            {
                if (!SameText(leftValues[i].Name, rightValues[i].Name)
                    || leftValues[i].CompositionKey != rightValues[i].CompositionKey)
                    return false;
            }

            return true;
        }

        private static EndmemberCompatibilityKey[] NormalizeEndmembers(List<EndmemberConditionSnapshot>? values) =>
            values?
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .Select(value => new EndmemberCompatibilityKey(
                    value.Name,
                    BuildCompositionKey(value.Formula)))
                .ToArray()
            ?? [];

        private static string BuildCompositionKey(string? formula)
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (name, mol) in CompositionParser.ParseComposition(formula ?? string.Empty))
                totals[name] = totals.GetValueOrDefault(name) + mol;

            return string.Join(
                "|",
                totals
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{Math.Round(pair.Value, 6).ToString("F6", CultureInfo.InvariantCulture)}"));
        }

        private static bool SameText(string? left, string? right) =>
            string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static bool SameDouble(double left, double right) =>
            Math.Abs(left - right) <= ConditionTolerance;

        private sealed record ReusablePoolCandidate(
            SpectrumPoolHandle Handle,
            SpectrumPoolManifest Manifest);

        // 260907Codex: Metadata avoids rereading JSON while still observing normal external replacement/deletion.
        private sealed record CachedManifest(
            SpectrumPoolManifest Manifest,
            DateTime LastWriteTimeUtc,
            long Length);

        private sealed record EndmemberCompatibilityKey(
            string Name,
            string CompositionKey);
    }

    // 260513Codex: pool の場所と生成条件 snapshot を一緒に運ぶ軽量ハンドルです。
    internal sealed record SpectrumPoolHandle(
        string PoolFolder,
        string ManifestPath,
        string ConditionKey,
        SpectrumGenerationConditionSnapshot Condition);
}
