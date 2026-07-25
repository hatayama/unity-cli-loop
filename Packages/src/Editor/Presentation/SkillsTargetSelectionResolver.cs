using System;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
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

    /// <summary>
    /// Resolves Skills Target Selection values from the available runtime context.
    /// </summary>
    public static class SkillsTargetSelectionResolver
    {
        public static SkillsTargetSelection Resolve(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            return target switch
            {
                SkillsTarget.Claude => new("Claude Code", ".claude", "--claude", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Codex => new("Codex CLI", ".codex", "--codex", groupSkillsUnderUnityCliLoop),
                SkillsTarget.Agents => new("Common", ".agents", "--agents", groupSkillsUnderUnityCliLoop),
                _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
            };
        }

        // Why keep: EditMode tests assert this mapping; production UI currently uses Resolve only.
        public static bool IsInstalled(CliSetupData data, SkillsTarget target)
        {
            return target switch
            {
                SkillsTarget.Claude => data.IsClaudeSkillsInstalled,
                SkillsTarget.Codex => data.IsCodexSkillsInstalled,
                SkillsTarget.Agents => data.IsAgentsSkillsInstalled,
                _ => false
            };
        }
    }
}
