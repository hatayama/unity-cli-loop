using System;
using System.IO;
using System.Text.RegularExpressions;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads Unity CLI Loop skill metadata from SKILL.md frontmatter.
    /// </summary>
    internal static class SkillSourceFrontmatterReader
    {
        internal static string ParseToolNameFromFrontmatter(string content)
        {
            Match frontmatterMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
            if (!frontmatterMatch.Success)
            {
                return null;
            }

            string frontmatter = frontmatterMatch.Groups[1].Value;
            Match toolNameMatch = Regex.Match(frontmatter, @"^toolName:\s*(.+)$", RegexOptions.Multiline);
            if (!toolNameMatch.Success)
            {
                return null;
            }

            return toolNameMatch.Groups[1].Value.Trim();
        }

        internal static string ParseNameFromFrontmatter(string content)
        {
            Match frontmatterMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
            if (!frontmatterMatch.Success)
            {
                return null;
            }

            string frontmatter = frontmatterMatch.Groups[1].Value;
            Match nameMatch = Regex.Match(frontmatter, @"^name:\s*(.+)$", RegexOptions.Multiline);
            if (!nameMatch.Success)
            {
                return null;
            }

            return nameMatch.Groups[1].Value.Trim().Trim('"');
        }

        internal static string ParseDescriptionFromFrontmatter(string content)
        {
            Match frontmatterMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
            if (!frontmatterMatch.Success)
            {
                return null;
            }

            string frontmatter = frontmatterMatch.Groups[1].Value;
            Match descriptionMatch = Regex.Match(frontmatter, @"^description:\s*(.+)$", RegexOptions.Multiline);
            if (!descriptionMatch.Success)
            {
                return null;
            }

            return descriptionMatch.Groups[1].Value.Trim().Trim('"');
        }

        internal static string ResolveToolNameForSkillSource(string skillName, string toolName)
        {
            if (!string.IsNullOrEmpty(toolName))
            {
                return toolName;
            }

            if (string.IsNullOrEmpty(skillName)
                || !skillName.StartsWith(CliConstants.SKILL_DIR_PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            return skillName.Substring(CliConstants.SKILL_DIR_PREFIX.Length);
        }

        internal static bool SkillContentMatchesTool(string content, string skillDirectory, string toolName)
        {
            string parsedToolName = ParseToolNameFromFrontmatter(content);
            if (!string.IsNullOrEmpty(parsedToolName))
            {
                return parsedToolName == toolName;
            }

            string parsedSkillName = ParseNameFromFrontmatter(content);
            if (!string.IsNullOrEmpty(parsedSkillName))
            {
                return parsedSkillName == $"{CliConstants.SKILL_DIR_PREFIX}{toolName}";
            }

            string dirName = Path.GetFileName(skillDirectory);
            return dirName == $"{CliConstants.SKILL_DIR_PREFIX}{toolName}";
        }

        internal static string GetToolNameFromSkillContent(string content)
        {
            string parsedToolName = ParseToolNameFromFrontmatter(content);
            if (!string.IsNullOrEmpty(parsedToolName))
            {
                return parsedToolName;
            }

            string parsedSkillName = ParseNameFromFrontmatter(content);
            if (string.IsNullOrEmpty(parsedSkillName)
                || !parsedSkillName.StartsWith(CliConstants.SKILL_DIR_PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            return parsedSkillName.Substring(CliConstants.SKILL_DIR_PREFIX.Length);
        }

        internal static bool IsInternalSkill(string content)
        {
            Match frontmatterMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
            if (!frontmatterMatch.Success)
            {
                return false;
            }

            string frontmatter = frontmatterMatch.Groups[1].Value;
            Match internalMatch = Regex.Match(frontmatter, @"^internal:\s*(.+)$", RegexOptions.Multiline);
            if (!internalMatch.Success)
            {
                return false;
            }

            return string.Equals(
                internalMatch.Groups[1].Value.Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
