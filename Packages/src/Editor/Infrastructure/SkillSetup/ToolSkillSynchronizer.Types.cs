using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    public static partial class ToolSkillSynchronizer
    {
        public readonly struct SkillInstallResult
        {
            public readonly int AttemptedTargets;
            public readonly int SucceededTargets;

            public SkillInstallResult(int attemptedTargets, int succeededTargets)
            {
                AttemptedTargets = attemptedTargets;
                SucceededTargets = succeededTargets;
            }

            public int FailedTargets => AttemptedTargets - SucceededTargets;
            public bool IsSuccessful => FailedTargets == 0;
        }

        public readonly struct SkillTargetInfo
        {
            public readonly string DisplayName;
            public readonly string DirName;
            public readonly string InstallFlag;
            public readonly bool HasSkillsDirectory;
            public readonly bool HasExistingSkills;
            public readonly bool HasDifferentLayoutSkills;
            public readonly SkillInstallState InstallState;

            public SkillTargetInfo(
                string displayName,
                string dirName,
                string installFlag,
                bool hasSkillsDirectory,
                bool hasExistingSkills,
                bool hasDifferentLayoutSkills = false,
                SkillInstallState installState = SkillInstallState.Missing)
            {
                DisplayName = displayName;
                DirName = dirName;
                InstallFlag = installFlag;
                HasSkillsDirectory = hasSkillsDirectory;
                HasExistingSkills = hasExistingSkills;
                HasDifferentLayoutSkills = hasDifferentLayoutSkills;
                InstallState = installState;
            }
        }
    }
}
