namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Decides when Settings should run CLI PATH visibility checks.
    /// </summary>
    public static class CliPathSetupCheckPolicy
    {
        /// <summary>
        /// PATH repair checks run only for non-Windows package-owned current-user installs.
        /// </summary>
        public static bool ShouldCheck(
            bool isWindowsEditor,
            bool hasPackageOwnedCurrentUserInstall)
        {
            return !isWindowsEditor && hasPackageOwnedCurrentUserInstall;
        }
    }
}
