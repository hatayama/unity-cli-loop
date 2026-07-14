using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Decides when the skills-installed success dialog should appear after an install attempt.
    /// </summary>
    public static class SkillInstallDialogPolicy
    {
        /// <summary>
        /// Returns true when Settings should show the success dialog for a single selected target.
        /// </summary>
        public static bool ShouldShowForSelectedTarget(SkillSetupTargetInfo targetInfo)
        {
            return targetInfo.InstallState != SkillInstallState.Outdated
                && !targetInfo.HasDifferentLayoutSkills;
        }

        /// <summary>
        /// Returns true when Setup Wizard should show the success dialog for all installable targets.
        /// </summary>
        public static bool ShouldShowForInstallableTargets(IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets.All(ShouldShowForSelectedTarget);
        }
    }
}
