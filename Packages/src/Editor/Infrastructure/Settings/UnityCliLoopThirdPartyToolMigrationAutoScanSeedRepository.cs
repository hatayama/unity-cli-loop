using System;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores the migration auto-scan seed file paths in Unity SessionState.
    /// </summary>
    public sealed class UnityCliLoopThirdPartyToolMigrationAutoScanSeedRepository
        : IThirdPartyToolMigrationAutoScanSeedRepository
    {
        private const string SeedFilePathsKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "thirdPartyToolMigrationAutoScanSeedFilePaths";
        private const char SeedFilePathSeparator = '\n';

        public void StoreSeedFilePaths(string[] filePaths)
        {
            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            UnityCliLoopEditorSessionStateStorage.SetString(
                SeedFilePathsKey,
                string.Join(SeedFilePathSeparator, filePaths));
        }

        public string[] GetSeedFilePaths()
        {
            string stored = UnityCliLoopEditorSessionStateStorage.GetString(SeedFilePathsKey);
            if (string.IsNullOrEmpty(stored))
            {
                return Array.Empty<string>();
            }

            return stored
                .Split(SeedFilePathSeparator)
                .Where(filePath => filePath.Length > 0)
                .ToArray();
        }

        public void ClearSeedFilePaths()
        {
            UnityCliLoopEditorSessionStateStorage.SetString(SeedFilePathsKey, string.Empty);
        }
    }
}
