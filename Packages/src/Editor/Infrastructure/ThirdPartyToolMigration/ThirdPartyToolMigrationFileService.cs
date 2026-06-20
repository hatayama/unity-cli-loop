using System;
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
        private readonly object _previewCacheLock = new();
        private bool _hasCachedPreview;
        private string _cachedPreviewProjectRoot = string.Empty;
        private ThirdPartyToolMigrationPreview _cachedPreview;

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
            MigrationPlan plan = await ThirdPartyToolMigrationPlanBuilder.CreateAsync(normalizedProjectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            ThirdPartyToolMigrationPreview preview = new(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
            StoreCachedPreview(normalizedProjectRoot, preview);
            return preview;
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
            InvalidatePreviewCache();
            MigrationPlan plan = ThirdPartyToolMigrationPlanBuilder.Create(normalizedProjectRoot);
            foreach (MigrationFileChange change in plan.Changes)
            {
                ThirdPartyToolMigrationFileWriter.Write(change.FilePath, change.Content);
            }

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
            InvalidatePreviewCache();
            MigrationPlan plan = await ThirdPartyToolMigrationPlanBuilder.CreateAsync(normalizedProjectRoot, progress, ct);
            // A canceled operation must not start mutating files, but an active write batch must finish as one plan.
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>());
            }

            for (int index = 0; index < plan.Changes.Count; index++)
            {
                MigrationFileChange change = plan.Changes[index];
                ThirdPartyToolMigrationFileWriter.Write(change.FilePath, change.Content);
                if ((index + 1) % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        internal void InvalidatePreviewCache()
        {
            lock (_previewCacheLock)
            {
                _hasCachedPreview = false;
                _cachedPreviewProjectRoot = string.Empty;
                _cachedPreview = default;
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

            lock (_previewCacheLock)
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

            lock (_previewCacheLock)
            {
                _cachedPreviewProjectRoot = projectRoot;
                _cachedPreview = preview;
                _hasCachedPreview = true;
            }
        }

        internal static string NormalizeProjectRoot(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
