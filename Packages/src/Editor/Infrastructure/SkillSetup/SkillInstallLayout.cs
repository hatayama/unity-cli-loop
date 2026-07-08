using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Provides Skill Install Layout behavior for Unity CLI Loop.
    /// </summary>
    internal static class SkillInstallLayout
    {
        internal const string SkillsDirName = "skills";
        internal const string ManagedSkillsDirName = "unity-cli-loop";
        internal const string SkillFileName = "SKILL.md";
        private const byte Utf16LittleEndianBomFirstByte = 0xFF;
        private const byte Utf16LittleEndianBomSecondByte = 0xFE;
        private const byte Utf16BigEndianBomFirstByte = 0xFE;
        private const byte Utf16BigEndianBomSecondByte = 0xFF;
        private const int Utf16CodeUnitByteCount = 2;
        private const ushort CarriageReturnCodeUnit = 0x000D;
        private const ushort LineFeedCodeUnit = 0x000A;
        private static readonly HashSet<string> TextSkillFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".json",
            ".md",
            ".ps1",
            ".sh",
            ".txt",
            ".yaml",
            ".yml"
        };

        internal readonly struct SkillSourceInfo
        {
            public readonly string Name;
            public readonly string ToolName;
            public readonly Dictionary<string, byte[]> SkillFiles;

            public SkillSourceInfo(
                string name,
                string toolName,
                Dictionary<string, byte[]> skillFiles)
            {
                Name = name;
                ToolName = toolName;
                SkillFiles = skillFiles.ToDictionary(
                    pair => NormalizeSkillRelativePath(pair.Key),
                    pair => pair.Value,
                    StringComparer.Ordinal);
            }
        }

        private static string NormalizeSkillRelativePath(string relativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(relativePath), "relativePath must not be null or empty");

            return relativePath.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
        }

        internal static string GetSkillsRoot(string targetRoot)
        {
            return Path.Combine(targetRoot, SkillsDirName);
        }

        internal static string GetManagedSkillsRoot(string targetRoot)
        {
            return Path.Combine(GetSkillsRoot(targetRoot), ManagedSkillsDirName);
        }

        internal static bool HasOptedInSkillsDirectory(string targetRoot)
        {
            return Directory.Exists(GetSkillsRoot(targetRoot));
        }

        internal static IEnumerable<string> EnumerateInstalledSkillDirectories(string targetRoot)
        {
            foreach (string skillDir in EnumerateManagedSkillDirectories(targetRoot))
            {
                yield return skillDir;
            }

            foreach (string skillDir in EnumerateLegacyManagedSkillDirectories(targetRoot))
            {
                yield return skillDir;
            }
        }

        internal static bool HasInstalledSkillsInAnyLayout(string projectRoot, string targetRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(targetRoot), "targetRoot must not be null or empty");

            return HasInstalledSkillsForLayout(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop: false)
                || HasInstalledSkillsForLayout(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop: true);
        }

        internal static bool HasInstalledSkillsForLayout(
            string projectRoot,
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(targetRoot), "targetRoot must not be null or empty");

            if (!groupSkillsUnderUnityCliLoop && EnumerateLegacyManagedSkillDirectories(targetRoot).Any())
            {
                return true;
            }

            List<SkillSourceInfo> expectedSkills = SkillSourceRootEnumerator.GetSkillSourceInfos(projectRoot);
            if (expectedSkills.Count == 0)
            {
                return false;
            }

            return expectedSkills.Any(skill =>
                Directory.Exists(GetInstalledSkillDirectoryPath(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop)));
        }

        internal static SkillInstallState GetInstalledState(
            string projectRoot,
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            List<SkillSourceInfo> expectedSkills = SkillSourceRootEnumerator.GetSkillSourceInfos(projectRoot);
            bool hasLayoutSkills = HasInstalledSkillsForLayout(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop);
            if (expectedSkills.Count == 0)
            {
                return hasLayoutSkills ? SkillInstallState.Installed : SkillInstallState.Missing;
            }

            bool hasInstalledExpectedSkill = false;
            bool hasMissingExpectedSkill = false;

            foreach (SkillSourceInfo expectedSkill in expectedSkills)
            {
                string installedSkillDirectory = GetInstalledSkillDirectoryPath(
                    targetRoot,
                    expectedSkill.Name,
                    groupSkillsUnderUnityCliLoop);
                if (!Directory.Exists(installedSkillDirectory))
                {
                    hasMissingExpectedSkill = true;
                    continue;
                }

                hasInstalledExpectedSkill = true;
                if (IsSkillDirectoryOutdated(expectedSkill.SkillFiles, installedSkillDirectory))
                {
                    return SkillInstallState.Outdated;
                }
            }

            if (!hasInstalledExpectedSkill)
            {
                return hasLayoutSkills ? SkillInstallState.Outdated : SkillInstallState.Missing;
            }

            return hasMissingExpectedSkill ? SkillInstallState.Outdated : SkillInstallState.Installed;
        }

        internal static bool SkillMatchesTool(string skillDir, string toolName)
        {
            string skillMdPath = Path.Combine(skillDir, SkillFileName);
            if (File.Exists(skillMdPath))
            {
                string content = File.ReadAllText(skillMdPath);
                if (SkillSourceFrontmatterReader.SkillContentMatchesTool(content, skillDir, toolName))
                {
                    return true;
                }
            }

            string dirName = Path.GetFileName(skillDir);
            return dirName == $"{CliConstants.SKILL_DIR_PREFIX}{toolName}";
        }

        internal static HashSet<string> GetInternalSkillToolNames(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return SkillSourceRootEnumerator.GetInternalSkillToolNames(projectRoot);
        }

        internal static List<SkillSourceInfo> GetSkillSourceInfos(string projectRoot)
        {
            return SkillSourceRootEnumerator.GetSkillSourceInfos(projectRoot);
        }

        internal static IReadOnlyDictionary<string, string> GetToolDescriptionsByToolName(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return SkillSourceRootEnumerator.GetToolDescriptionsByToolName(projectRoot);
        }

        internal static SkillSourceInfo GetSkillSourceInfoFromDirectory(string skillDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(skillDirectory), "skillDirectory must not be null or empty");

            string skillFilePath = Path.Combine(skillDirectory, SkillFileName);
            Debug.Assert(File.Exists(skillFilePath), "skill source must contain SKILL.md");

            string skillContent = File.ReadAllText(skillFilePath);
            string skillName = SkillSourceFrontmatterReader.ParseNameFromFrontmatter(skillContent);
            Debug.Assert(IsSafeSkillPathComponent(skillName), "skillName must be a single safe path component");

            return new SkillSourceInfo(
                skillName,
                SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(skillContent),
                CollectSourceSkillFiles(skillDirectory, skillFilePath));
        }

        internal static SkillInstallState GetInstalledStateForSkillSource(
            string targetRoot,
            SkillSourceInfo skill,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(targetRoot), "targetRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(skill.Name), "skill name must not be null or empty");

            string installedSkillDirectory = GetInstalledSkillDirectoryPath(
                targetRoot,
                skill.Name,
                groupSkillsUnderUnityCliLoop);
            if (!Directory.Exists(installedSkillDirectory))
            {
                return SkillInstallState.Missing;
            }

            string installedSkillFilePath = Path.Combine(installedSkillDirectory, SkillFileName);
            if (!File.Exists(installedSkillFilePath))
            {
                return SkillInstallState.Missing;
            }

            return IsSkillDirectoryOutdated(skill.SkillFiles, installedSkillDirectory)
                ? SkillInstallState.Outdated
                : SkillInstallState.Installed;
        }

        internal static string GetInstalledSkillDirectoryPathForLayout(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop)
        {
            return GetInstalledSkillDirectoryPath(targetRoot, skillName, groupSkillsUnderUnityCliLoop);
        }

        private static IEnumerable<string> EnumerateManagedSkillDirectories(string targetRoot)
        {
            string managedSkillsRoot = GetManagedSkillsRoot(targetRoot);
            if (!Directory.Exists(managedSkillsRoot))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateDirectories(managedSkillsRoot);
        }

        private static IEnumerable<string> EnumerateLegacyManagedSkillDirectories(string targetRoot)
        {
            string skillsRoot = GetSkillsRoot(targetRoot);
            if (!Directory.Exists(skillsRoot))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateDirectories(skillsRoot)
                .Where(skillDir => Path.GetFileName(skillDir) != ManagedSkillsDirName)
                .Where(IsLegacyManagedSkillDirectory);
        }

        private static bool IsLegacyManagedSkillDirectory(string skillDir)
        {
            string dirName = Path.GetFileName(skillDir);
            if (dirName.StartsWith(CliConstants.SKILL_DIR_PREFIX, StringComparison.Ordinal))
            {
                return true;
            }

            string skillMdPath = Path.Combine(skillDir, SkillFileName);
            if (!File.Exists(skillMdPath))
            {
                return false;
            }

            string content = File.ReadAllText(skillMdPath);
            if (!string.IsNullOrEmpty(SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(content)))
            {
                return true;
            }

            return false;
        }

        private static string GetInstalledSkillDirectoryPath(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(IsSafeSkillPathComponent(skillName), "skillName must be a single safe path component");

            string skillsRoot = groupSkillsUnderUnityCliLoop
                ? GetManagedSkillsRoot(targetRoot)
                : GetSkillsRoot(targetRoot);
            return Path.Combine(skillsRoot, skillName);
        }

        private static bool IsSkillDirectoryOutdated(
            Dictionary<string, byte[]> sourceFiles,
            string installedSkillDirectory)
        {
            Dictionary<string, byte[]> installedFiles = CollectInstalledSkillFiles(installedSkillDirectory);
            if (sourceFiles.Count != installedFiles.Count)
            {
                return true;
            }

            foreach (KeyValuePair<string, byte[]> sourceFile in sourceFiles)
            {
                if (!installedFiles.TryGetValue(sourceFile.Key, out byte[] installedContent))
                {
                    return true;
                }

                if (!sourceFile.Value.SequenceEqual(installedContent))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, byte[]> CollectInstalledSkillFiles(string skillDirectory)
        {
            Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (SkillSetupFileExclusion.IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                files[relativePath] = NormalizeSkillFileContent(relativePath, File.ReadAllBytes(filePath));
            }

            return files;
        }

        internal static Dictionary<string, byte[]> CollectSourceSkillFiles(
            string skillDirectory,
            string skillFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(skillDirectory), "skillDirectory must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(skillFilePath), "skillFilePath must not be null or empty");

            if (string.Equals(Path.GetFileName(skillDirectory), "Skill", StringComparison.Ordinal))
            {
                return CollectInstalledSkillFiles(skillDirectory);
            }

            return new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SkillFileName] = NormalizeSkillFileContent(SkillFileName, File.ReadAllBytes(skillFilePath))
            };
        }

        internal static byte[] NormalizeSkillFileContent(string relativePath, byte[] content)
        {
            Debug.Assert(!string.IsNullOrEmpty(relativePath), "relativePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            if (!ShouldNormalizeLineEndings(relativePath) || Array.IndexOf(content, (byte)'\r') < 0)
            {
                return content;
            }

            if (HasUtf16LittleEndianBom(content) || HasUtf16LittleEndianLineEnding(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: true);
            }

            if (HasUtf16BigEndianBom(content) || HasUtf16BigEndianLineEnding(content))
            {
                return NormalizeUtf16LineEndings(content, isLittleEndian: false);
            }

            if (Array.IndexOf(content, (byte)0) >= 0)
            {
                return content;
            }

            return NormalizeSingleByteLineEndings(content);
        }

        private static bool ShouldNormalizeLineEndings(string relativePath)
        {
            string extension = Path.GetExtension(relativePath);
            return TextSkillFileExtensions.Contains(extension);
        }

        private static byte[] NormalizeSingleByteLineEndings(byte[] content)
        {
            List<byte> normalized = new(content.Length);
            for (int index = 0; index < content.Length; index++)
            {
                byte current = content[index];
                if (current != (byte)'\r')
                {
                    normalized.Add(current);
                    continue;
                }

                normalized.Add((byte)'\n');
                if (index + 1 < content.Length && content[index + 1] == (byte)'\n')
                {
                    index++;
                }
            }

            return normalized.ToArray();
        }

        private static byte[] NormalizeUtf16LineEndings(byte[] content, bool isLittleEndian)
        {
            List<byte> normalized = new(content.Length);
            int index = 0;
            if (HasMatchingUtf16Bom(content, isLittleEndian))
            {
                normalized.Add(content[0]);
                normalized.Add(content[1]);
                index = Utf16CodeUnitByteCount;
            }

            while (index + 1 < content.Length)
            {
                ushort codeUnit = ReadUtf16CodeUnit(content, index, isLittleEndian);
                if (codeUnit == CarriageReturnCodeUnit)
                {
                    WriteUtf16CodeUnit(normalized, LineFeedCodeUnit, isLittleEndian);
                    int nextIndex = index + Utf16CodeUnitByteCount;
                    if (nextIndex + 1 < content.Length &&
                        ReadUtf16CodeUnit(content, nextIndex, isLittleEndian) == LineFeedCodeUnit)
                    {
                        index += Utf16CodeUnitByteCount * 2;
                        continue;
                    }

                    index += Utf16CodeUnitByteCount;
                    continue;
                }

                WriteUtf16CodeUnit(normalized, codeUnit, isLittleEndian);
                index += Utf16CodeUnitByteCount;
            }

            if (index < content.Length)
            {
                normalized.Add(content[index]);
            }

            return normalized.ToArray();
        }

        private static bool HasUtf16LittleEndianBom(byte[] content)
        {
            return content.Length >= Utf16CodeUnitByteCount &&
                   content[0] == Utf16LittleEndianBomFirstByte &&
                   content[1] == Utf16LittleEndianBomSecondByte;
        }

        private static bool HasUtf16BigEndianBom(byte[] content)
        {
            return content.Length >= Utf16CodeUnitByteCount &&
                   content[0] == Utf16BigEndianBomFirstByte &&
                   content[1] == Utf16BigEndianBomSecondByte;
        }

        private static bool HasMatchingUtf16Bom(byte[] content, bool isLittleEndian)
        {
            return isLittleEndian ? HasUtf16LittleEndianBom(content) : HasUtf16BigEndianBom(content);
        }

        private static bool HasUtf16LittleEndianLineEnding(byte[] content)
        {
            return HasUtf16LineEnding(content, isLittleEndian: true);
        }

        private static bool HasUtf16BigEndianLineEnding(byte[] content)
        {
            return HasUtf16LineEnding(content, isLittleEndian: false);
        }

        private static bool HasUtf16LineEnding(byte[] content, bool isLittleEndian)
        {
            int startIndex = HasMatchingUtf16Bom(content, isLittleEndian) ? Utf16CodeUnitByteCount : 0;
            for (int index = startIndex; index + 1 < content.Length; index += Utf16CodeUnitByteCount)
            {
                ushort codeUnit = ReadUtf16CodeUnit(content, index, isLittleEndian);
                if (codeUnit == CarriageReturnCodeUnit || codeUnit == LineFeedCodeUnit)
                {
                    return true;
                }
            }

            return false;
        }

        private static ushort ReadUtf16CodeUnit(byte[] content, int index, bool isLittleEndian)
        {
            return isLittleEndian
                ? (ushort)(content[index] | (content[index + 1] << 8))
                : (ushort)((content[index] << 8) | content[index + 1]);
        }

        private static void WriteUtf16CodeUnit(List<byte> output, ushort codeUnit, bool isLittleEndian)
        {
            if (isLittleEndian)
            {
                output.Add((byte)(codeUnit & 0xFF));
                output.Add((byte)(codeUnit >> 8));
                return;
            }

            output.Add((byte)(codeUnit >> 8));
            output.Add((byte)(codeUnit & 0xFF));
        }

        internal static bool IsSafeSkillPathComponent(string skillName)
        {
            if (string.IsNullOrEmpty(skillName))
            {
                return false;
            }

            if (skillName == "." || skillName == "..")
            {
                return false;
            }

            if (skillName.Contains('/') || skillName.Contains('\\'))
            {
                return false;
            }

            if (Path.IsPathRooted(skillName))
            {
                return false;
            }

            if (skillName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            return string.Equals(Path.GetFileName(skillName), skillName, StringComparison.Ordinal);
        }
    }
}
