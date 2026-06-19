using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Coordinates V3 migration for third-party custom tools without owning file-system details.
    /// </summary>
    public sealed class ThirdPartyToolMigrationUseCase
    {
        private readonly IThirdPartyToolMigrationPort _migrationPort;

        public ThirdPartyToolMigrationUseCase(IThirdPartyToolMigrationPort migrationPort)
        {
            Debug.Assert(migrationPort != null, "migrationPort must not be null");

            _migrationPort = migrationPort ?? throw new ArgumentNullException(nameof(migrationPort));
        }

        public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return _migrationPort.PreviewMigration(projectRoot);
        }

        public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            return _migrationPort.PreviewMigrationAsync(projectRoot, progress, ct);
        }

        public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return _migrationPort.HasMigrationTargetsAsync(projectRoot, ct);
        }

        public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return _migrationPort.ApplyMigration(projectRoot);
        }

        public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            return _migrationPort.ApplyMigrationAsync(projectRoot, progress, ct);
        }
    }
}
