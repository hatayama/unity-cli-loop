using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    internal static class SkillInstallLayout
    {
        internal const string SkillsDirName = "skills";
        internal const string ManagedSkillsDirName = "unity-cli-loop";
        internal const string SkillFileName = "SKILL.md";
        private const string CliPackageDirName = "Cli~";
        private const string CliSkillDefinitionsDirName = "skill-definitions";
        private const string CliOnlySkillDefinitionsDirName = "cli-only";
        private static readonly HashSet<string> ExcludedFileNames = new()
        {
            ".meta",
            ".DS_Store",
            ".gitkeep"
        };

        private sealed class SkillSourceDefinition
        {
            public readonly string Name;
            public readonly string ToolName;
            public readonly string SkillDirectoryPath;
            public readonly Dictionary<string, byte[]> SkillFiles;

            public SkillSourceDefinition(
                string name,
                string toolName,
                string skillDirectoryPath,
                Dictionary<string, byte[]> skillFiles)
            {
                Name = name;
                ToolName = toolName;
                SkillDirectoryPath = skillDirectoryPath;
                SkillFiles = skillFiles;
            }
        }

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
                SkillFiles = skillFiles;
            }
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

        internal static bool HasInstalledSkills(string targetRoot)
        {
            return EnumerateInstalledSkillDirectories(targetRoot).Any();
        }

        internal static bool HasInstalledSkills(string targetRoot, bool groupSkillsUnderUnityCliLoop)
        {
            IEnumerable<string> skillDirs = groupSkillsUnderUnityCliLoop
                ? EnumerateManagedSkillDirectories(targetRoot)
                : EnumerateLegacyManagedSkillDirectories(targetRoot);
            return skillDirs.Any();
        }

        internal static SkillInstallState GetInstalledState(
            string projectRoot,
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            Dictionary<string, SkillSourceDefinition> expectedSkills = GetSkillSources(projectRoot);
            bool hasLayoutSkills = HasInstalledSkills(targetRoot, groupSkillsUnderUnityCliLoop);
            if (expectedSkills.Count == 0)
            {
                return hasLayoutSkills ? SkillInstallState.Installed : SkillInstallState.Missing;
            }

            bool hasInstalledExpectedSkill = false;
            bool hasMissingExpectedSkill = false;

            foreach (SkillSourceDefinition expectedSkill in expectedSkills.Values)
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

            if (HasUnexpectedInstalledSkillDirectories(
                targetRoot,
                expectedSkills.Keys,
                groupSkillsUnderUnityCliLoop))
            {
                return SkillInstallState.Outdated;
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
                string parsed = ParseToolNameFromFrontmatter(content);
                if (!string.IsNullOrEmpty(parsed))
                {
                    return parsed == toolName;
                }
            }

            string dirName = Path.GetFileName(skillDir);
            return dirName == $"{CliConstants.SKILL_DIR_PREFIX}{toolName}";
        }

        internal static List<SkillSourceInfo> GetSkillSourceInfos(string projectRoot)
        {
            return GetSkillSources(projectRoot)
                .Values
                .Select(source => new SkillSourceInfo(source.Name, source.ToolName, source.SkillFiles))
                .ToList();
        }

        internal static string GetInstalledSkillDirectoryPathForLayout(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop)
        {
            return GetInstalledSkillDirectoryPath(targetRoot, skillName, groupSkillsUnderUnityCliLoop);
        }

        internal static IEnumerable<string> EnumerateInstalledSkillDirectoryNamesForLayout(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            IEnumerable<string> installedSkillDirectories = groupSkillsUnderUnityCliLoop
                ? EnumerateManagedSkillDirectories(targetRoot)
                : EnumerateLegacyManagedSkillDirectories(targetRoot);
            return installedSkillDirectories
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name));
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
            if (!string.IsNullOrEmpty(ParseToolNameFromFrontmatter(content)))
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

        private static bool HasUnexpectedInstalledSkillDirectories(
            string targetRoot,
            IEnumerable<string> expectedSkillNames,
            bool groupSkillsUnderUnityCliLoop)
        {
            HashSet<string> expectedSkillNameSet = new(expectedSkillNames, StringComparer.Ordinal);
            IEnumerable<string> installedSkillDirectories = groupSkillsUnderUnityCliLoop
                ? EnumerateManagedSkillDirectories(targetRoot)
                : EnumerateLegacyManagedSkillDirectories(targetRoot);

            foreach (string installedSkillDirectory in installedSkillDirectories)
            {
                string installedSkillName = Path.GetFileName(installedSkillDirectory);
                if (string.IsNullOrEmpty(installedSkillName))
                {
                    continue;
                }

                if (!expectedSkillNameSet.Contains(installedSkillName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSkillDirectoryOutdated(
            Dictionary<string, byte[]> sourceFiles,
            string installedSkillDirectory)
        {
            Dictionary<string, byte[]> installedFiles = CollectSkillFiles(installedSkillDirectory);
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

        private static Dictionary<string, byte[]> CollectSkillFiles(string skillDirectory)
        {
            Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (IsExcludedFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                files[relativePath] = File.ReadAllBytes(filePath);
            }

            return files;
        }

        private static Dictionary<string, SkillSourceDefinition> GetSkillSources(string projectRoot)
        {
            Dictionary<string, SkillSourceDefinition> sources = new(StringComparer.Ordinal);
            foreach (string searchRoot in EnumerateSkillSourceRoots(projectRoot))
            {
                if (!Directory.Exists(searchRoot))
                {
                    continue;
                }

                IEnumerable<string> skillFilePaths = IsCliOnlySkillSourceRoot(searchRoot)
                    ? Directory.EnumerateFiles(searchRoot, SkillFileName, SearchOption.AllDirectories)
                    : EnumerateEditorFolders(searchRoot, 3).SelectMany(editorFolder =>
                        Directory.EnumerateFiles(editorFolder, SkillFileName, SearchOption.AllDirectories));
                foreach (string skillFilePath in skillFilePaths)
                {
                    string skillDirectory = Path.GetDirectoryName(skillFilePath);
                    if (skillDirectory == null || Path.GetFileName(skillDirectory) != "Skill")
                    {
                        continue;
                    }

                    string skillContent = File.ReadAllText(skillFilePath);
                    if (IsInternalSkill(skillContent))
                    {
                        continue;
                    }

                    string skillName = ParseNameFromFrontmatter(skillContent);
                    if (string.IsNullOrEmpty(skillName)
                        || !IsSafeSkillPathComponent(skillName)
                        || sources.ContainsKey(skillName))
                    {
                        continue;
                    }

                    sources[skillName] = new SkillSourceDefinition(
                        skillName,
                        ParseToolNameFromFrontmatter(skillContent),
                        skillDirectory,
                        CollectSkillFiles(skillDirectory));
                }
            }

            return sources;
        }

        private static IEnumerable<string> EnumerateSkillSourceRoots(string projectRoot)
        {
            HashSet<string> seenRoots = new(StringComparer.Ordinal);

            AddSkillSourceRoot(seenRoots, GetCliOnlySkillSourceRoot(projectRoot));
            AddSkillSourceRoot(seenRoots, Path.Combine(projectRoot, "Assets"));
            foreach (string packageRoot in EnumerateDirectProjectPackageRoots(projectRoot))
            {
                AddSkillSourceRoot(seenRoots, packageRoot);
            }

            foreach (string packageRoot in EnumerateManifestLocalPackageRoots(projectRoot))
            {
                AddSkillSourceRoot(seenRoots, packageRoot);
            }

            foreach (string packageRoot in EnumerateDependencyPackageCacheRoots(projectRoot))
            {
                AddSkillSourceRoot(seenRoots, packageRoot);
            }

            foreach (string root in seenRoots)
            {
                yield return root;
            }
        }

        private static void AddSkillSourceRoot(HashSet<string> roots, string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            roots.Add(Path.GetFullPath(root));
        }

        private static string GetCliOnlySkillSourceRoot(string projectRoot)
        {
            string currentProjectRoot = UnityMcpPathResolver.GetProjectRoot();
            if (!string.Equals(
                Path.GetFullPath(projectRoot),
                Path.GetFullPath(currentProjectRoot),
                StringComparison.Ordinal))
            {
                return null;
            }

            return Path.Combine(
                McpConstants.PackageResolvedPath,
                CliPackageDirName,
                McpConstants.SRC_DIR,
                SkillsDirName,
                CliSkillDefinitionsDirName,
                CliOnlySkillDefinitionsDirName);
        }

        private static bool IsCliOnlySkillSourceRoot(string searchRoot)
        {
            return string.Equals(
                Path.GetFullPath(searchRoot),
                Path.GetFullPath(GetCliOnlySkillSourceRoot(UnityMcpPathResolver.GetProjectRoot())),
                StringComparison.Ordinal);
        }

        private static IEnumerable<string> EnumerateDirectProjectPackageRoots(string projectRoot)
        {
            string packagesRoot = Path.Combine(projectRoot, "Packages");
            if (!Directory.Exists(packagesRoot))
            {
                yield break;
            }

            foreach (string packageDirectory in Directory.EnumerateDirectories(packagesRoot))
            {
                yield return ResolveSkillSearchRootCandidate(packageDirectory);
            }
        }

        private static IEnumerable<string> EnumerateManifestLocalPackageRoots(string projectRoot)
        {
            foreach (KeyValuePair<string, string> dependency in EnumerateManifestDependencies(projectRoot))
            {
                string localPath = ResolveLocalDependencyPath(dependency.Value, projectRoot);
                if (string.IsNullOrEmpty(localPath))
                {
                    continue;
                }

                yield return ResolveSkillSearchRootCandidate(localPath);
            }
        }

        private static IEnumerable<string> EnumerateDependencyPackageCacheRoots(string projectRoot)
        {
            HashSet<string> dependencyNames = new(
                EnumerateManifestDependencies(projectRoot).Select(dependency => dependency.Key),
                StringComparer.OrdinalIgnoreCase);
            if (dependencyNames.Count == 0)
            {
                yield break;
            }

            string packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCacheRoot))
            {
                yield break;
            }

            foreach (string packageDirectory in Directory.EnumerateDirectories(packageCacheRoot))
            {
                string packageName = Path.GetFileName(packageDirectory);
                if (string.IsNullOrEmpty(packageName))
                {
                    continue;
                }

                int separatorIndex = packageName.IndexOf('@');
                string dependencyName = separatorIndex >= 0 ? packageName.Substring(0, separatorIndex) : packageName;
                if (!dependencyNames.Contains(dependencyName))
                {
                    continue;
                }

                yield return ResolveSkillSearchRootCandidate(packageDirectory);
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateManifestDependencies(string projectRoot)
        {
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                yield break;
            }

            string manifestContent = File.ReadAllText(manifestPath);
            Match dependenciesMatch = Regex.Match(
                manifestContent,
                "\"dependencies\"\\s*:\\s*\\{(?<body>[\\s\\S]*?)\\}",
                RegexOptions.Multiline);
            if (!dependenciesMatch.Success)
            {
                yield break;
            }

            MatchCollection dependencyMatches = Regex.Matches(
                dependenciesMatch.Groups["body"].Value,
                "\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<value>[^\"]*)\"");
            foreach (Match dependencyMatch in dependencyMatches)
            {
                string dependencyName = dependencyMatch.Groups["name"].Value;
                string dependencyValue = dependencyMatch.Groups["value"].Value;
                if (string.IsNullOrEmpty(dependencyName) || string.IsNullOrEmpty(dependencyValue))
                {
                    continue;
                }

                yield return new KeyValuePair<string, string>(dependencyName, dependencyValue);
            }
        }

        private static string ResolveLocalDependencyPath(string dependencyValue, string projectRoot)
        {
            const string FilePrefix = "file:";
            const string PathPrefix = "path:";

            if (dependencyValue.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                return ResolveDependencyPath(dependencyValue.Substring(FilePrefix.Length), projectRoot);
            }

            if (dependencyValue.StartsWith(PathPrefix, StringComparison.Ordinal))
            {
                return ResolveDependencyPath(dependencyValue.Substring(PathPrefix.Length), projectRoot);
            }

            return null;
        }

        private static string ResolveDependencyPath(string rawPath, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            string normalizedPath = rawPath.Trim();
            if (normalizedPath.StartsWith("//", StringComparison.Ordinal))
            {
                normalizedPath = normalizedPath.Substring(2);
            }

            if (Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));
        }

        private static string ResolveSkillSearchRootCandidate(string candidate)
        {
            string nestedRoot = Path.Combine(candidate, "Packages", "src");
            if (Directory.Exists(nestedRoot))
            {
                return nestedRoot;
            }

            return candidate;
        }

        private static IEnumerable<string> EnumerateEditorFolders(string basePath, int maxDepth)
        {
            return EnumerateEditorFoldersRecursive(basePath, depth: 0, maxDepth);
        }

        private static IEnumerable<string> EnumerateEditorFoldersRecursive(
            string currentPath,
            int depth,
            int maxDepth)
        {
            if (depth > maxDepth || !Directory.Exists(currentPath))
            {
                yield break;
            }

            foreach (string directory in Directory.EnumerateDirectories(currentPath))
            {
                string directoryName = Path.GetFileName(directory);
                if (string.Equals(directoryName, "Editor", StringComparison.Ordinal))
                {
                    yield return directory;
                    continue;
                }

                foreach (string editorDirectory in EnumerateEditorFoldersRecursive(directory, depth + 1, maxDepth))
                {
                    yield return editorDirectory;
                }
            }
        }

        private static string ParseToolNameFromFrontmatter(string content)
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

        private static string ParseNameFromFrontmatter(string content)
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

        private static bool IsInternalSkill(string content)
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

        private static bool IsExcludedFile(string fileName)
        {
            if (ExcludedFileNames.Contains(fileName))
            {
                return true;
            }

            foreach (string excludedPattern in ExcludedFileNames)
            {
                if (fileName.EndsWith(excludedPattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSafeSkillPathComponent(string skillName)
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
