namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Describes one agent tool target that can receive Unity CLI Loop skill files.
    /// </summary>
    public readonly struct SkillSetupTargetInfo
    {
        public readonly string DisplayName;
        public readonly string DirName;
        public readonly string InstallFlag;
        public readonly bool HasSkillsDirectory;
        public readonly bool HasExistingSkills;
        public readonly bool HasDifferentLayoutSkills;
        public readonly SkillInstallState InstallState;

        public SkillSetupTargetInfo(
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
