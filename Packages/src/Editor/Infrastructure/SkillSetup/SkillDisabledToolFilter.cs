using System;
using System.Collections.Generic;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Resolves tool names from skills and filters skills disabled by tool settings.
    /// </summary>
    internal static class SkillDisabledToolFilter
    {
        internal static bool IsSkillDisabledByToolSettings(
            SkillInstallLayout.SkillSourceInfo skill,
            IReadOnlyCollection<string> disabledTools)
        {
            if (disabledTools.Count == 0)
            {
                return false;
            }

            string toolName = GetToolNameForSkill(skill);
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            return disabledTools.Contains(toolName);
        }

        internal static string[] GetCurrentDisabledTools()
        {
            ToolSettingsRepository repository = new ToolSettingsRepository();
            return repository.GetDisabledTools();
        }

        internal static bool IsSkillForTool(
            SkillInstallLayout.SkillSourceInfo skill,
            string toolName)
        {
            string skillToolName = GetToolNameForSkill(skill);
            return string.Equals(skillToolName, toolName, StringComparison.Ordinal);
        }

        private static string GetToolNameForSkill(SkillInstallLayout.SkillSourceInfo skill)
        {
            string toolName = skill.ToolName;
            if (string.IsNullOrEmpty(toolName) && skill.Name.StartsWith(CliConstants.SKILL_DIR_PREFIX, StringComparison.Ordinal))
            {
                toolName = skill.Name.Substring(CliConstants.SKILL_DIR_PREFIX.Length);
            }

            return toolName;
        }
    }
}
