
namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Defines the policy used to decide CLI Install Refresh behavior.
    /// </summary>
    internal static class CliInstallRefreshPolicy
    {
        internal static bool ShouldRefreshSkillsAfterCliInstall(bool wasCliInstalledBeforeInstall)
        {
            return !wasCliInstalledBeforeInstall;
        }
    }
}
