namespace io.github.hatayama.UnityCliLoop
{
    internal static class CliInstallRefreshPolicy
    {
        internal static bool ShouldRefreshSkillsAfterCliInstall(bool wasCliInstalledBeforeInstall)
        {
            return !wasCliInstalledBeforeInstall;
        }
    }
}
