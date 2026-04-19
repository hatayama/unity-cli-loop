using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UnityEngine;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.uLoopMCP
{
    public enum SkillInstallState
    {
        Missing,
        Checking,
        Installed,
        Outdated
    }

    /// <summary>
    /// Synchronizes skill files when tools are enabled/disabled.
    /// Removes skill directories on disable, re-installs on enable.
    /// </summary>
    public static class ToolSkillSynchronizer
    {
        public readonly struct SkillTargetDefinition
        {
            public readonly string DirName;
            public readonly string Flag;
            public readonly string DisplayName;

            public SkillTargetDefinition(string dirName, string flag, string displayName)
            {
                DirName = dirName;
                Flag = flag;
                DisplayName = displayName;
            }
        }

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

        private static readonly SkillTargetDefinition[] SkillTargets =
        {
            new(".claude", "--claude", "Claude Code"),
            new(".cursor", "--cursor", "Cursor"),
            new(".gemini", "--gemini", "Gemini CLI"),
            new(".codex", "--codex", "Codex CLI"),
            new(".agents", "--agents", "Other (.agents)"),
            new(".agent", "--antigravity", "Antigravity")
        };

        private static readonly string[] DeprecatedSkillNames =
        {
            "uloop-capture-window",
            "uloop-get-provider-details",
            "uloop-unity-search",
            "uloop-get-menu-items",
            "uloop-get-unity-search-providers"
        };
        private static readonly string[] ExcludedSkillFileNames =
        {
            ".meta",
            ".DS_Store",
            ".gitkeep"
        };

        internal static readonly string[] SkillTargetDirs = SkillTargets.Select(t => t.DirName).ToArray();

        public static void RemoveSkillFiles(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            RemoveSkillFilesAtProjectRoot(projectRoot, toolName);
        }

        internal static void RemoveSkillFilesAtProjectRoot(string projectRoot, string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            foreach (string targetDir in SkillTargetDirs)
            {
                string targetRoot = Path.Combine(projectRoot, targetDir);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                foreach (string skillDir in SkillInstallLayout.EnumerateInstalledSkillDirectories(targetRoot))
                {
                    if (SkillInstallLayout.SkillMatchesTool(skillDir, toolName))
                    {
                        Debug.Log($"[uLoopMCP] Removing skill '{toolName}' from '{skillDir}'");
                        Directory.Delete(skillDir, true);
                    }
                }
            }
        }

        public static bool IsSkillInstalled(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();

            foreach (string targetDir in SkillTargetDirs)
            {
                string targetRoot = Path.Combine(projectRoot, targetDir);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                foreach (string skillDir in SkillInstallLayout.EnumerateInstalledSkillDirectories(targetRoot))
                {
                    if (SkillInstallLayout.SkillMatchesTool(skillDir, toolName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static List<SkillTargetInfo> DetectTargets()
        {
            return DetectTargets(requireSkillsDirectory: false);
        }

        public static List<SkillTargetInfo> DetectTargetsForLayout(bool groupSkillsUnderUnityCliLoop)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargetsForLayoutAtProjectRoot(projectRoot, groupSkillsUnderUnityCliLoop);
        }

        public static List<SkillTargetInfo> DetectTargetsForLayoutFast(bool groupSkillsUnderUnityCliLoop)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargetsForLayoutFastAtProjectRoot(projectRoot, groupSkillsUnderUnityCliLoop);
        }

        internal static List<SkillTargetInfo> DetectTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(
                projectRoot,
                requireSkillsDirectory: false,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck: true);
        }

        internal static List<SkillTargetInfo> DetectTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(
                projectRoot,
                requireSkillsDirectory: false,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck: false);
        }

        internal static List<SkillTargetInfo> DetectTargets(bool requireSkillsDirectory)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(projectRoot, requireSkillsDirectory);
        }

        internal static List<SkillTargetInfo> DetectTargets(
            bool requireSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(
                projectRoot,
                requireSkillsDirectory,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck: true);
        }

        internal static List<SkillTargetInfo> DetectTargets(
            bool requireSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop,
            bool includeFreshnessCheck)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(
                projectRoot,
                requireSkillsDirectory,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck);
        }

        internal static List<SkillTargetInfo> DetectTargets(string projectRoot, bool requireSkillsDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<SkillTargetInfo> targets = new();

            foreach (SkillTargetDefinition target in SkillTargets)
            {
                string targetRoot = Path.Combine(projectRoot, target.DirName);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                bool hasSkillsDirectory = SkillInstallLayout.HasOptedInSkillsDirectory(targetRoot);
                if (requireSkillsDirectory && !hasSkillsDirectory)
                {
                    continue;
                }

                bool hasULoopSkills = hasSkillsDirectory && SkillInstallLayout.HasInstalledSkills(targetRoot);
                targets.Add(new SkillTargetInfo(
                    target.DisplayName,
                    target.DirName,
                    target.Flag,
                    hasSkillsDirectory,
                    hasULoopSkills,
                    installState: hasULoopSkills
                        ? SkillInstallState.Installed
                        : SkillInstallState.Missing));
            }

            return targets;
        }

        internal static List<SkillTargetInfo> DetectTargets(
            string projectRoot,
            bool requireSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop)
        {
            return DetectTargets(
                projectRoot,
                requireSkillsDirectory,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck: true);
        }

        internal static List<SkillTargetInfo> DetectTargets(
            string projectRoot,
            bool requireSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop,
            bool includeFreshnessCheck)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<SkillTargetInfo> targets = new();

            foreach (SkillTargetDefinition target in SkillTargets)
            {
                string targetRoot = Path.Combine(projectRoot, target.DirName);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                bool hasSkillsDirectory = SkillInstallLayout.HasOptedInSkillsDirectory(targetRoot);
                if (requireSkillsDirectory && !hasSkillsDirectory)
                {
                    continue;
                }

                SkillInstallState installState = ResolveInstallState(
                    projectRoot,
                    targetRoot,
                    hasSkillsDirectory,
                    groupSkillsUnderUnityCliLoop,
                    includeFreshnessCheck);
                bool hasULoopSkills = installState == SkillInstallState.Installed
                    || installState == SkillInstallState.Checking
                    || installState == SkillInstallState.Outdated;
                bool hasDifferentLayoutSkills = hasSkillsDirectory
                    && SkillInstallLayout.HasInstalledSkills(targetRoot, !groupSkillsUnderUnityCliLoop);
                targets.Add(new SkillTargetInfo(
                    target.DisplayName,
                    target.DirName,
                    target.Flag,
                    hasSkillsDirectory,
                    hasULoopSkills,
                    hasDifferentLayoutSkills,
                    installState));
            }

            return targets;
        }

        private static SkillInstallState ResolveInstallState(
            string projectRoot,
            string targetRoot,
            bool hasSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop,
            bool includeFreshnessCheck)
        {
            if (!hasSkillsDirectory)
            {
                return SkillInstallState.Missing;
            }

            if (!includeFreshnessCheck)
            {
                return SkillInstallLayout.HasInstalledSkills(targetRoot, groupSkillsUnderUnityCliLoop)
                    ? SkillInstallState.Checking
                    : SkillInstallState.Missing;
            }

            return SkillInstallLayout.GetInstalledState(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop);
        }

        /// <summary>
        /// Re-installs skills only for targets that already opted in via an existing skills directory.
        /// </summary>
        public static async Task<SkillInstallResult> InstallSkillFiles()
        {
            return await InstallSkillFiles(groupSkillsUnderUnityCliLoop: true);
        }

        public static async Task<SkillInstallResult> InstallSkillFiles(bool groupSkillsUnderUnityCliLoop)
        {
            List<SkillTargetInfo> targets = DetectTargets(requireSkillsDirectory: true);
            return await InstallSkillFiles(targets, groupSkillsUnderUnityCliLoop);
        }

        public static async Task<SkillInstallResult> InstallSkillFiles(List<SkillTargetInfo> targets)
        {
            return await InstallSkillFiles(targets, groupSkillsUnderUnityCliLoop: true);
        }

        public static async Task<SkillInstallResult> InstallSkillFiles(
            List<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(targets != null, "targets must not be null");
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return await InstallSkillFilesAtProjectRoot(projectRoot, targets, groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> InstallSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");

            SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                string[] disabledTools = ToolSettings.GetDisabledTools();
                List<SkillInstallLayout.SkillSourceInfo> allSkills = SkillInstallLayout.GetSkillSourceInfos(projectRoot);
                List<SkillInstallLayout.SkillSourceInfo> disabledSkills = allSkills
                    .Where(skill => IsSkillDisabled(skill, disabledTools))
                    .ToList();
                List<SkillInstallLayout.SkillSourceInfo> enabledSkills = allSkills
                    .Except(disabledSkills)
                    .ToList();

                int succeeded = 0;
                foreach (SkillTargetInfo target in targetArray)
                {
                    InstallSkillsForTarget(
                        projectRoot,
                        target,
                        disabledSkills,
                        enabledSkills,
                        groupSkillsUnderUnityCliLoop);
                    succeeded++;
                }

                return new SkillInstallResult(targetArray.Length, succeeded);
            });
        }

        private static void InstallSkillsForTarget(
            string projectRoot,
            SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> enabledSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            string targetRoot = Path.Combine(projectRoot, target.DirName);
            string skillsRoot = SkillInstallLayout.GetSkillsRoot(targetRoot);
            Directory.CreateDirectory(skillsRoot);

            if (groupSkillsUnderUnityCliLoop)
            {
                Directory.CreateDirectory(SkillInstallLayout.GetManagedSkillsRoot(targetRoot));
            }

            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: true);
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: false);
            }

            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: true);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: false);
            }

            foreach (SkillInstallLayout.SkillSourceInfo skill in enabledSkills)
            {
                string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop);
                SyncInstalledSkillDirectory(installedSkillDirectory, skill.SkillFiles);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, !groupSkillsUnderUnityCliLoop);
            }

            DeleteUnexpectedInstalledSkillDirectories(
                targetRoot,
                enabledSkills.Select(skill => skill.Name),
                groupSkillsUnderUnityCliLoop);
        }

        private static bool IsSkillDisabled(
            SkillInstallLayout.SkillSourceInfo skill,
            IReadOnlyCollection<string> disabledTools)
        {
            if (disabledTools.Count == 0)
            {
                return false;
            }

            string toolName = skill.ToolName;
            if (string.IsNullOrEmpty(toolName) && skill.Name.StartsWith(CliConstants.SKILL_DIR_PREFIX, StringComparison.Ordinal))
            {
                toolName = skill.Name.Substring(CliConstants.SKILL_DIR_PREFIX.Length);
            }

            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            return disabledTools.Contains(toolName);
        }

        private static void DeleteUnexpectedInstalledSkillDirectories(
            string targetRoot,
            IEnumerable<string> expectedSkillNames,
            bool groupSkillsUnderUnityCliLoop)
        {
            HashSet<string> expectedSkillNameSet = new(expectedSkillNames, StringComparer.Ordinal);
            foreach (string installedSkillName in SkillInstallLayout.EnumerateInstalledSkillDirectoryNamesForLayout(
                         targetRoot,
                         groupSkillsUnderUnityCliLoop))
            {
                if (expectedSkillNameSet.Contains(installedSkillName))
                {
                    continue;
                }

                DeleteSkillDirectoryIfExists(targetRoot, installedSkillName, groupSkillsUnderUnityCliLoop);
            }
        }

        private static void SyncInstalledSkillDirectory(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles)
        {
            Debug.Assert(!string.IsNullOrEmpty(skillDirectory), "skillDirectory must not be null or empty");
            Debug.Assert(skillFiles != null, "skillFiles must not be null");
            Debug.Assert(skillFiles.ContainsKey(SkillInstallLayout.SkillFileName),
                "skillFiles must contain SKILL.md");

            string parentDirectory = Path.GetDirectoryName(skillDirectory);
            Debug.Assert(!string.IsNullOrEmpty(parentDirectory), "parentDirectory must not be null or empty");
            Directory.CreateDirectory(parentDirectory);
            bool skillDirectoryExisted = Directory.Exists(skillDirectory);
            Dictionary<string, byte[]> backupFiles = skillDirectoryExisted
                ? ReadSkillFiles(skillDirectory)
                : new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Directory.CreateDirectory(skillDirectory);

            bool syncCompleted = false;
            try
            {
                WriteSkillFiles(skillDirectory, skillFiles);
                DeleteUnexpectedSkillFiles(skillDirectory, skillFiles.Keys);
                DeleteEmptyDirectories(skillDirectory);
                syncCompleted = true;
            }
            finally
            {
                if (!syncCompleted)
                {
                    RollbackSkillDirectory(skillDirectory, backupFiles, skillDirectoryExisted);
                }
            }
        }

        private static void WriteSkillFiles(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles)
        {
            foreach (KeyValuePair<string, byte[]> skillFile in skillFiles)
            {
                string fullPath = Path.Combine(skillDirectory, skillFile.Key);
                string fileDirectory = Path.GetDirectoryName(fullPath);
                Debug.Assert(!string.IsNullOrEmpty(fileDirectory), "fileDirectory must not be null or empty");
                Directory.CreateDirectory(fileDirectory);
                if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).SequenceEqual(skillFile.Value))
                {
                    continue;
                }

                WriteFileAtomically(fullPath, skillFile.Value);
            }
        }

        private static Dictionary<string, byte[]> ReadSkillFiles(string skillDirectory)
        {
            Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                files[relativePath] = File.ReadAllBytes(filePath);
            }

            return files;
        }

        private static void WriteFileAtomically(string fullPath, byte[] content)
        {
            string fileDirectory = Path.GetDirectoryName(fullPath);
            Debug.Assert(!string.IsNullOrEmpty(fileDirectory), "fileDirectory must not be null or empty");

            string tempPath = Path.Combine(
                fileDirectory,
                $"{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
            File.WriteAllBytes(tempPath, content);

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, null, true);
                    return;
                }

                File.Move(tempPath, fullPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void DeleteUnexpectedSkillFiles(
            string skillDirectory,
            IEnumerable<string> expectedRelativePaths)
        {
            HashSet<string> expectedPaths = new(expectedRelativePaths, StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                if (expectedPaths.Contains(relativePath))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }

        private static void DeleteEmptyDirectories(string skillDirectory)
        {
            foreach (string directoryPath in Directory.EnumerateDirectories(skillDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    continue;
                }

                Directory.Delete(directoryPath);
            }
        }

        private static void RollbackSkillDirectory(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> backupFiles,
            bool skillDirectoryExisted)
        {
            if (!skillDirectoryExisted)
            {
                if (Directory.Exists(skillDirectory))
                {
                    Directory.Delete(skillDirectory, true);
                }

                return;
            }

            Directory.CreateDirectory(skillDirectory);
            WriteSkillFiles(skillDirectory, backupFiles);
            DeleteUnexpectedSkillFiles(skillDirectory, backupFiles.Keys);
            DeleteEmptyDirectories(skillDirectory);
        }

        private static bool IsExcludedSkillFile(string fileName)
        {
            if (ExcludedSkillFileNames.Contains(fileName))
            {
                return true;
            }

            foreach (string excludedPattern in ExcludedSkillFileNames)
            {
                if (fileName.EndsWith(excludedPattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DeleteSkillDirectoryIfExists(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop)
        {
            string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                skillName,
                groupSkillsUnderUnityCliLoop);
            if (!Directory.Exists(installedSkillDirectory))
            {
                return;
            }

            Directory.Delete(installedSkillDirectory, true);
        }
    }
}
