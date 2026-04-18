namespace io.github.hatayama.uLoopMCP
{
    using System;

    public readonly struct SkillsTargetSelection
    {
        public readonly string DisplayName;
        public readonly string DirectoryName;
        public readonly string InstallFlag;
        public readonly string InstallArguments;

        public SkillsTargetSelection(string displayName, string directoryName, string installFlag)
        {
            DisplayName = displayName;
            DirectoryName = directoryName;
            InstallFlag = installFlag;
            InstallArguments = $"skills install {installFlag}";
        }
    }

    public static class SkillsTargetSelectionResolver
    {
        public static SkillsTargetSelection Resolve(SkillsTarget target)
        {
            return target switch
            {
                SkillsTarget.Claude => new("Claude Code", ".claude", "--claude"),
                SkillsTarget.Cursor => new("Cursor", ".cursor", "--cursor"),
                SkillsTarget.Gemini => new("Gemini CLI", ".gemini", "--gemini"),
                SkillsTarget.Codex => new("Codex CLI", ".codex", "--codex"),
                SkillsTarget.Agents => new("Other (.agents)", ".agents", "--agents"),
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
    }
}
