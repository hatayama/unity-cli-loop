using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Discovers project skill source roots and turns SKILL.md files into skill source metadata.
    /// </summary>
    internal static class SkillSourceRootEnumerator
    {
        private const string EditorDirName = "Editor";
        private const string CliOnlyToolsDirName = "CliOnlyTools~";

        private sealed class SkillSourceDefinition
        {
            public readonly string Name;
            public readonly string ToolName;
            public readonly string Description;
            public readonly string SkillDirectoryPath;
            public readonly Dictionary<string, byte[]> SkillFiles;

            public SkillSourceDefinition(
                string name,
                string toolName,
                string description,
                string skillDirectoryPath,
                Dictionary<string, byte[]> skillFiles)
            {
                Name = name;
                ToolName = toolName;
                Description = description;
                SkillDirectoryPath = skillDirectoryPath;
                SkillFiles = skillFiles;
            }
        }

        internal static HashSet<string> GetInternalSkillToolNames(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            HashSet<string> toolNames = new(StringComparer.Ordinal);
            foreach (string searchRoot in EnumerateSkillSourceRoots(projectRoot))
            {
                if (!Directory.Exists(searchRoot))
                {
                    continue;
                }

                foreach (string skillFilePath in EnumerateSourceSkillFiles(searchRoot))
                {
                    string skillDirectory = Path.GetDirectoryName(skillFilePath);
                    if (skillDirectory == null)
                    {
                        continue;
                    }

                    string skillContent = File.ReadAllText(skillFilePath);
                    if (!SkillSourceFrontmatterReader.IsInternalSkill(skillContent))
                    {
                        continue;
                    }

                    string toolName = SkillSourceFrontmatterReader.GetToolNameFromSkillContent(skillContent);
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        toolNames.Add(toolName);
                    }
                }
            }

            return toolNames;
        }

        internal static List<SkillInstallLayout.SkillSourceInfo> GetSkillSourceInfos(string projectRoot)
        {
            return GetSkillSources(projectRoot)
                .Values
                .Select(source => new SkillInstallLayout.SkillSourceInfo(source.Name, source.ToolName, source.SkillFiles))
                .ToList();
        }

        internal static IReadOnlyDictionary<string, string> GetToolDescriptionsByToolName(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
            foreach (SkillSourceDefinition source in GetSkillSources(projectRoot).Values)
            {
                string toolName = SkillSourceFrontmatterReader.ResolveToolNameForSkillSource(
                    source.Name,
                    source.ToolName);
                if (string.IsNullOrEmpty(toolName) || string.IsNullOrWhiteSpace(source.Description))
                {
                    continue;
                }

                if (!descriptions.ContainsKey(toolName))
                {
                    descriptions[toolName] = source.Description;
                }
            }

            return descriptions;
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

                foreach (string skillFilePath in EnumerateSourceSkillFiles(searchRoot))
                {
                    string skillDirectory = Path.GetDirectoryName(skillFilePath);
                    if (skillDirectory == null)
                    {
                        continue;
                    }

                    string skillContent = File.ReadAllText(skillFilePath);
                    if (SkillSourceFrontmatterReader.IsInternalSkill(skillContent))
                    {
                        continue;
                    }

                    string skillName = SkillSourceFrontmatterReader.ParseNameFromFrontmatter(skillContent);
                    if (string.IsNullOrEmpty(skillName)
                        || !SkillInstallLayout.IsSafeSkillPathComponent(skillName)
                        || sources.ContainsKey(skillName))
                    {
                        continue;
                    }

                    sources[skillName] = new SkillSourceDefinition(
                        skillName,
                        SkillSourceFrontmatterReader.ParseToolNameFromFrontmatter(skillContent),
                        SkillSourceFrontmatterReader.ParseDescriptionFromFrontmatter(skillContent),
                        skillDirectory,
                        SkillInstallLayout.CollectSourceSkillFiles(skillDirectory, skillFilePath));
                }
            }

            return sources;
        }

        private static IEnumerable<string> EnumerateSourceSkillFiles(string searchRoot)
        {
            if (IsCliOnlySkillSourceRoot(searchRoot))
            {
                return Directory.EnumerateFiles(searchRoot, SkillInstallLayout.SkillFileName, SearchOption.AllDirectories);
            }

            return EnumerateEditorFolders(searchRoot, 3).SelectMany(editorFolder =>
                Directory.EnumerateFiles(editorFolder, SkillInstallLayout.SkillFileName, SearchOption.AllDirectories));
        }

        private static IEnumerable<string> EnumerateSkillSourceRoots(string projectRoot)
        {
            List<string> orderedRoots = new();
            HashSet<string> seenRoots = new(StringComparer.Ordinal);

            AddSkillSourceRoot(orderedRoots, seenRoots, GetCliOnlySkillSourceRoot(projectRoot));
            AddSkillSourceRoot(orderedRoots, seenRoots, Path.Combine(projectRoot, "Assets"));
            foreach (string packageRoot in EnumerateDirectProjectPackageRoots(projectRoot))
            {
                AddSkillSourceRoot(orderedRoots, seenRoots, packageRoot);
            }

            foreach (string packageRoot in EnumerateManifestLocalPackageRoots(projectRoot))
            {
                AddSkillSourceRoot(orderedRoots, seenRoots, packageRoot);
            }

            foreach (string packageRoot in EnumerateDependencyPackageCacheRoots(projectRoot))
            {
                AddSkillSourceRoot(orderedRoots, seenRoots, packageRoot);
            }

            foreach (string root in orderedRoots)
            {
                yield return root;
            }
        }

        private static void AddSkillSourceRoot(
            List<string> orderedRoots,
            HashSet<string> seenRoots,
            string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            string normalizedRoot = Path.GetFullPath(root);
            if (!seenRoots.Add(normalizedRoot))
            {
                return;
            }

            orderedRoots.Add(normalizedRoot);
        }

        private static string GetCliOnlySkillSourceRoot(string projectRoot)
        {
            string currentProjectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            if (!HaveSamePathIdentityAtPlatform(
                projectRoot,
                currentProjectRoot,
                UnityEngine.Application.platform))
            {
                return null;
            }

            return Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                EditorDirName,
                CliOnlyToolsDirName);
        }

        private static bool IsCliOnlySkillSourceRoot(string searchRoot)
        {
            return HaveSamePathIdentityAtPlatform(
                searchRoot,
                GetCliOnlySkillSourceRoot(UnityCliLoopPathResolver.GetProjectRoot()),
                UnityEngine.Application.platform);
        }

        internal static bool HaveSamePathIdentityAtPlatform(
            string leftPath,
            string rightPath,
            RuntimePlatform platform)
        {
            return string.Equals(
                Path.GetFullPath(leftPath),
                Path.GetFullPath(rightPath),
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
                EnumerateManifestDependencies(projectRoot)
                    .Where(dependency => ResolveLocalDependencyPath(dependency.Value, projectRoot) == null)
                    .Select(dependency => dependency.Key),
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
                if (string.Equals(directoryName, EditorDirName, StringComparison.Ordinal))
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
    }
}
