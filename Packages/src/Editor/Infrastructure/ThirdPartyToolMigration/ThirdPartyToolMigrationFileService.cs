using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Scans Unity project files and rewrites V2 custom tool source to the V3 public contract API.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFileService : IThirdPartyToolMigrationPort
    {
        private readonly object _migrationCacheLock = new();
        private bool _hasCachedPreview;
        private string _cachedPreviewProjectRoot = string.Empty;
        private ThirdPartyToolMigrationPreview _cachedPreview;
        private bool _hasCachedPlan;
        private string _cachedPlanProjectRoot = string.Empty;
        private MigrationPlan _cachedPlan;

        public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            if (TryGetCachedPreview(normalizedProjectRoot, out ThirdPartyToolMigrationPreview cachedPreview))
            {
                return cachedPreview;
            }

            MigrationPlan plan = ThirdPartyToolMigrationPlanBuilder.Create(normalizedProjectRoot);
            ThirdPartyToolMigrationPreview preview = new(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
            StoreCachedPlan(normalizedProjectRoot, plan);
            StoreCachedPreview(normalizedProjectRoot, preview);
            return preview;
        }

        public async Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            MigrationPlan plan = await GetCurrentMigrationPlanAsync(normalizedProjectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            ThirdPartyToolMigrationPreview preview = new(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
            StoreCachedPlan(normalizedProjectRoot, plan);
            StoreCachedPreview(normalizedProjectRoot, preview);
            return preview;
        }

        /// <summary>
        /// Builds a preview scanning only the given assembly directories (e.g. the assemblies containing
        /// compile-error-matched files), instead of the whole project. Deliberately does not read or
        /// write the full-scan preview/plan cache above: a scope-limited plan is only a partial view of
        /// the project and must never be mistaken for (or substituted into) the full plan that
        /// ApplyMigration relies on.
        /// </summary>
        public async Task<ThirdPartyToolMigrationPreview> PreviewMigrationInScopeAsync(
            string projectRoot,
            List<string> scopeDirectories,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(scopeDirectories != null, "scopeDirectories must not be null");
            Debug.Assert(progress != null, "progress must not be null");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            MigrationPlan plan = await ThirdPartyToolMigrationPlanBuilder.CreateInScopeAsync(
                normalizedProjectRoot,
                scopeDirectories,
                progress,
                ct);
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            return new ThirdPartyToolMigrationPreview(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        public async Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            if (!Directory.Exists(normalizedProjectRoot))
            {
                throw new DirectoryNotFoundException(normalizedProjectRoot);
            }

            if (!Directory.Exists(Path.Combine(normalizedProjectRoot, "Assets")))
            {
                return false;
            }

            return await ThirdPartyToolMigrationTargetScanner.HasMigrationTargetAsync(normalizedProjectRoot, ct);
        }

        public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            MigrationPlan plan = GetCurrentMigrationPlan(normalizedProjectRoot);
            InvalidatePreviewCache();
            ThirdPartyToolMigrationFileWriter.WriteBatch(plan.Changes);

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        public async Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            MigrationPlan plan = await GetCurrentMigrationPlanAsync(normalizedProjectRoot, progress, ct);
            // A canceled operation must not start mutating files, but an active write batch must finish as one plan.
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>());
            }

            InvalidatePreviewCache();
            await ThirdPartyToolMigrationFileWriter.WriteBatchAsync(plan.Changes);

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        internal void InvalidatePreviewCache()
        {
            InvalidateMigrationCaches();
        }

        private MigrationPlan GetCurrentMigrationPlan(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            CachedMigrationPlanLookup cachedPlan = GetCurrentCachedPlan(projectRoot);
            if (cachedPlan.Found)
            {
                return cachedPlan.Plan;
            }

            return ThirdPartyToolMigrationPlanBuilder.Create(projectRoot);
        }

        private async Task<MigrationPlan> GetCurrentMigrationPlanAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            CachedMigrationPlanLookup cachedPlan = await GetCurrentCachedPlanAsync(projectRoot, progress, ct);
            if (cachedPlan.Found)
            {
                return cachedPlan.Plan;
            }

            return await ThirdPartyToolMigrationPlanBuilder.CreateAsync(projectRoot, progress, ct);
        }

        private CachedMigrationPlanLookup GetCurrentCachedPlan(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            (bool found, MigrationPlan cachedPlan) = TryGetCachedPlanForFingerprintCheck(projectRoot);
            if (!found)
            {
                return CachedMigrationPlanLookup.NotFound;
            }

            ProjectFileInventory inventory = ProjectFileInventory.Create(projectRoot);
            return ResolveCachedPlanAgainstInventory(cachedPlan, inventory, wasCanceled: false);
        }

        private async Task<CachedMigrationPlanLookup> GetCurrentCachedPlanAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            (bool found, MigrationPlan cachedPlan) = TryGetCachedPlanForFingerprintCheck(projectRoot);
            if (!found)
            {
                return CachedMigrationPlanLookup.NotFound;
            }

            ProjectFileInventory inventory = await ProjectFileInventory.CreateAsync(projectRoot, progress, ct);
            // A canceled walk returns a partial inventory that would never match the cached fingerprint;
            // treat that as "unknown" instead of invalidating a cache that may still be valid.
            return ResolveCachedPlanAgainstInventory(cachedPlan, inventory, ct.IsCancellationRequested);
        }

        private (bool Found, MigrationPlan Plan) TryGetCachedPlanForFingerprintCheck(string projectRoot)
        {
            lock (_migrationCacheLock)
            {
                if (!_hasCachedPlan ||
                    !string.Equals(_cachedPlanProjectRoot, projectRoot, StringComparison.Ordinal))
                {
                    return (false, default);
                }

                return (true, _cachedPlan);
            }
        }

        private CachedMigrationPlanLookup ResolveCachedPlanAgainstInventory(
            MigrationPlan cachedPlan,
            ProjectFileInventory inventory,
            bool wasCanceled)
        {
            if (wasCanceled)
            {
                return CachedMigrationPlanLookup.NotFound;
            }

            if (!cachedPlan.ProjectFingerprint.Matches(inventory))
            {
                InvalidateMigrationCaches();
                return CachedMigrationPlanLookup.NotFound;
            }

            return CachedMigrationPlanLookup.FoundPlan(cachedPlan);
        }

        private void InvalidateMigrationCaches()
        {
            lock (_migrationCacheLock)
            {
                _hasCachedPreview = false;
                _cachedPreviewProjectRoot = string.Empty;
                _cachedPreview = default;
                _hasCachedPlan = false;
                _cachedPlanProjectRoot = string.Empty;
                _cachedPlan = default;
            }
        }

        internal static bool TryReadJsonObjectForMigration(
            string filePath,
            Func<string, string> readAllText,
            out JObject jsonObject)
        {
            return ThirdPartyToolMigrationAssemblyReferenceResolver.TryReadJsonObjectForMigration(
                filePath,
                readAllText,
                out jsonObject);
        }

        private bool TryGetCachedPreview(
            string projectRoot,
            out ThirdPartyToolMigrationPreview preview)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            lock (_migrationCacheLock)
            {
                if (_hasCachedPreview &&
                    string.Equals(_cachedPreviewProjectRoot, projectRoot, StringComparison.Ordinal))
                {
                    preview = _cachedPreview;
                    return true;
                }
            }

            preview = default;
            return false;
        }

        private void StoreCachedPreview(string projectRoot, ThirdPartyToolMigrationPreview preview)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            lock (_migrationCacheLock)
            {
                _cachedPreviewProjectRoot = projectRoot;
                _cachedPreview = preview;
                _hasCachedPreview = true;
            }
        }

        private void StoreCachedPlan(string projectRoot, MigrationPlan plan)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            lock (_migrationCacheLock)
            {
                _cachedPlanProjectRoot = projectRoot;
                _cachedPlan = plan;
                _hasCachedPlan = true;
            }
        }

        private readonly struct CachedMigrationPlanLookup
        {
            public static CachedMigrationPlanLookup NotFound => new(false, default);

            public static CachedMigrationPlanLookup FoundPlan(MigrationPlan plan)
            {
                return new CachedMigrationPlanLookup(true, plan);
            }

            private CachedMigrationPlanLookup(bool found, MigrationPlan plan)
            {
                Found = found;
                Plan = plan;
            }

            public bool Found { get; }
            public MigrationPlan Plan { get; }
        }

        internal static string NormalizeProjectRoot(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
