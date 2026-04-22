namespace io.github.hatayama.uLoopMCP
{
    internal static class CliInstallRefreshPolicy
    {
        internal static bool ShouldRefreshSkillsAfterCliInstall(bool wasCliInstalledBeforeInstall)
        {
            return !wasCliInstalledBeforeInstall;
        }
    }
}
