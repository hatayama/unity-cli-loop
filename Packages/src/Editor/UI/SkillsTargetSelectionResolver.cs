namespace io.github.hatayama.uLoopMCP
{
    using System;

    public readonly struct SkillsTargetSelection
    {
        public readonly string DisplayName;
        public readonly string DirectoryName;
        public readonly string InstallFlag;
        public readonly string InstallArguments;

        public SkillsTargetSelection(
            string displayName,
            string directoryName,
            string installFlag,
            bool groupSkillsUnderUnityCliLoop)
        {
            DisplayName = displayName;
            DirectoryName = directoryName;
            InstallFlag = installFlag;
            InstallArguments = groupSkillsUnderUnityCliLoop
                ? $"skills install {installFlag}"
                : $"skills install {installFlag} --flat";
        }
    }

    public static class SkillsTargetSelectionResolver
    {
        public static SkillsTargetSelection Resolve(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            return target switch
            {
                SkillsTarget.Claude => new("Claude Code", ".claude", "--claude", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Cursor => new("Cursor", ".cursor", "--cursor", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Gemini => new("Gemini CLI", ".gemini", "--gemini", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Codex => new("Codex CLI", ".codex", "--codex", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Agents => new("Other (.agents)", ".agents", "--agents", groupSkillsUnderUnityCliLoop),
                _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
            };
        }

        public static bool IsInstalled(CliSetupData data, SkillsTarget target)
        {
            return target switch
            {
                SkillsTarget.Claude => data.IsClaudeSkillsInstalled,
                SkillsTarget.Cursor => data.IsCursorSkillsInstalled,
                SkillsTarget.Gemini => data.IsGeminiSkillsInstalled,
                SkillsTarget.Codex => data.IsCodexSkillsInstalled,
                SkillsTarget.Agents => data.IsAgentsSkillsInstalled,
                _ => false
            };
        }

        public static SkillInstallState GetInstallState(CliSetupData data, SkillsTarget target)
        {
            return data.SelectedTarget == target ? data.SelectedTargetInstallState : SkillInstallState.Missing;
        }
    }
}
