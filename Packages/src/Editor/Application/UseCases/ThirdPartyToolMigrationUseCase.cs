using System;
using System.Diagnostics;

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

        public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return _migrationPort.ApplyMigration(projectRoot);
        }
    }
}
