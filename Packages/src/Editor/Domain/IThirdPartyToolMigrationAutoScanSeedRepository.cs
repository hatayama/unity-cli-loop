namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Stores the seed file paths for a pending migration auto-scan (the compile-error-matched files
    /// that triggered it) for the current Unity Editor session.
    /// </summary>
    public interface IThirdPartyToolMigrationAutoScanSeedRepository
    {
        void StoreSeedFilePaths(string[] filePaths);
        string[] GetSeedFilePaths();
        void ClearSeedFilePaths();
    }
}
