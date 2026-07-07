using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Synchronizes skill files when tools are enabled/disabled.
    /// Removes skill directories on disable, re-installs on enable.
    /// </summary>
    public static class ToolSkillSynchronizer
    {
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

        private static readonly string[] DeprecatedSkillNames =
        {
            "uloop-capture-window",
            "uloop-get-provider-details",
            "uloop-unity-search",
            "uloop-get-menu-items",
            "uloop-get-unity-search-providers",
            "uloop-execute-menu-item"
        };
        private static readonly string V3MigrationSkillSourceDirectory = Path.Combine(
            UnityCliLoopConstants.PackageResolvedPath,
            CliConstants.TEMPORARY_SKILLS_DIR_NAME,
            CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
            "Skill");

        public static void RemoveSkillFiles(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            RemoveSkillFilesAtProjectRoot(projectRoot, toolName);
        }

        internal static void RemoveSkillFilesAtProjectRoot(string projectRoot, string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            foreach (string targetDir in SkillTargetDetector.SkillTargetDirs)
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
                        Debug.Log($"[UnityCliLoop] Removing skill '{toolName}' from '{skillDir}'");
                        Directory.Delete(skillDir, true);
                    }
                }
            }
        }

        public static bool IsSkillInstalled(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();

            foreach (string targetDir in SkillTargetDetector.SkillTargetDirs)
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

        public static List<SkillTargetInfo> DetectTargetsForLayout(bool groupSkillsUnderUnityCliLoop)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargetsForLayoutAtProjectRoot(projectRoot, groupSkillsUnderUnityCliLoop);
        }

        public static List<SkillTargetInfo> DetectTargetsForLayoutFast(bool groupSkillsUnderUnityCliLoop)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargetsForLayoutFastAtProjectRoot(projectRoot, groupSkillsUnderUnityCliLoop);
        }

        internal static List<SkillTargetInfo> DetectTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
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

            return SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                projectRoot,
                requireSkillsDirectory: false,
                groupSkillsUnderUnityCliLoop,
                includeFreshnessCheck: false);
        }

        public static async Task<SkillInstallResult> InstallSkillFilesForTool(
            string toolName,
            bool groupSkillsUnderUnityCliLoop)
        {
            return await InstallSkillFilesForToolWithDisabledTools(
                toolName,
                groupSkillsUnderUnityCliLoop,
                SkillDisabledToolFilter.GetCurrentDisabledTools());
        }

        internal static async Task<SkillInstallResult> InstallSkillFilesForToolWithDisabledTools(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            string[] disabledTools)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            Debug.Assert(disabledTools != null, "disabledTools must not be null");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return await InstallSkillFilesForToolAtProjectRoot(
                projectRoot,
                toolName,
                groupSkillsUnderUnityCliLoop,
                disabledTools);
        }

        internal static async Task<SkillInstallResult> InstallSkillFiles(
            List<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            string[] disabledTools)
        {
            Debug.Assert(targets != null, "targets must not be null");
            Debug.Assert(disabledTools != null, "disabledTools must not be null");
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return await InstallSkillFilesAtProjectRoot(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop,
                disabledTools);
        }

        internal static async Task<SkillInstallResult> InstallSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            string[] disabledTools)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            Debug.Assert(disabledTools != null, "disabledTools must not be null");

            SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                List<SkillInstallLayout.SkillSourceInfo> allSkills = SkillInstallLayout.GetSkillSourceInfos(projectRoot);
                List<SkillInstallLayout.SkillSourceInfo> disabledSkills = allSkills
                    .Where(skill => SkillDisabledToolFilter.IsSkillDisabledByToolSettings(
                        skill,
                        disabledTools))
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

        internal static async Task<SkillInstallResult> InstallSkillFilesForToolAtProjectRoot(
            string projectRoot,
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            string[] disabledTools)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            Debug.Assert(disabledTools != null, "disabledTools must not be null");

            if (disabledTools.Contains(toolName))
            {
                return new SkillInstallResult(0, 0);
            }

            SkillTargetInfo[] targetArray = SkillTargetDetector.DetectTargetsWithSkillsDirectory(projectRoot).ToArray();
            return await Task.Run(() =>
            {
                List<SkillInstallLayout.SkillSourceInfo> allSkills = SkillInstallLayout.GetSkillSourceInfos(projectRoot);
                List<SkillInstallLayout.SkillSourceInfo> disabledSkills = allSkills
                    .Where(skill => SkillDisabledToolFilter.IsSkillDisabledByToolSettings(
                        skill,
                        disabledTools))
                    .ToList();
                List<SkillInstallLayout.SkillSourceInfo> toolSkills = allSkills
                    .Where(skill => SkillDisabledToolFilter.IsSkillForTool(skill, toolName))
                    .ToList();
                if (toolSkills.Count == 0)
                {
                    return new SkillInstallResult(0, 0);
                }

                int succeeded = 0;
                foreach (SkillTargetInfo target in targetArray)
                {
                    InstallSpecificSkillsForTarget(
                        projectRoot,
                        target,
                        disabledSkills,
                        toolSkills,
                        groupSkillsUnderUnityCliLoop);
                    succeeded++;
                }

                return new SkillInstallResult(targetArray.Length, succeeded);
            });
        }

        internal static SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillTargetInfo target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillInstallLayout.SkillSourceInfo skill = GetV3MigrationSkillSourceInfo();
            SkillInstallState preferredLayoutState = GetSkillInstallStateAtProjectRoot(
                projectRoot,
                target,
                skill,
                groupSkillsUnderUnityCliLoop);
            if (preferredLayoutState != SkillInstallState.Missing)
            {
                return preferredLayoutState;
            }

            return GetSkillInstallStateAtProjectRoot(
                projectRoot,
                target,
                skill,
                !groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> InstallV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillInstallLayout.SkillSourceInfo skill = GetV3MigrationSkillSourceInfo();
            return await InstallSpecificSkillFilesAtProjectRoot(
                projectRoot,
                targets,
                skill,
                groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> RemoveV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");

            SkillInstallLayout.SkillSourceInfo skill = GetV3MigrationSkillSourceInfo();
            SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (SkillTargetInfo target in targetArray)
                {
                    string targetRoot = Path.Combine(projectRoot, target.DirName);
                    DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skill.Name,
                        groupSkillsUnderUnityCliLoop);
                    DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skill.Name,
                        !groupSkillsUnderUnityCliLoop);
                    succeeded++;
                }

                return new SkillInstallResult(targetArray.Length, succeeded);
            });
        }

        internal static SkillInstallState GetSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillTargetInfo target,
            SkillInstallLayout.SkillSourceInfo skill,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(target.DirName), "target dir name must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, target.DirName);
            if (!Directory.Exists(targetRoot))
            {
                return SkillInstallState.Missing;
            }

            return SkillInstallLayout.GetInstalledStateForSkillSource(
                targetRoot,
                skill,
                groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> InstallSpecificSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            SkillInstallLayout.SkillSourceInfo skill,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");

            SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (SkillTargetInfo target in targetArray)
                {
                    InstallSpecificSkillsForTarget(
                        projectRoot,
                        target,
                        Array.Empty<SkillInstallLayout.SkillSourceInfo>(),
                        new[] { skill },
                        groupSkillsUnderUnityCliLoop);
                    succeeded++;
                }

                return new SkillInstallResult(targetArray.Length, succeeded);
            });
        }

        internal static async Task<SkillInstallResult> RemoveSpecificSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            string skillName,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            Debug.Assert(!string.IsNullOrEmpty(skillName), "skillName must not be null or empty");

            SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (SkillTargetInfo target in targetArray)
                {
                    string targetRoot = Path.Combine(projectRoot, target.DirName);
                    DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skillName,
                        groupSkillsUnderUnityCliLoop);
                    succeeded++;
                }

                return new SkillInstallResult(targetArray.Length, succeeded);
            });
        }

        private static SkillInstallLayout.SkillSourceInfo GetV3MigrationSkillSourceInfo()
        {
            return SkillInstallLayout.GetSkillSourceInfoFromDirectory(V3MigrationSkillSourceDirectory);
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
            HashSet<string> managedSkillNames = new(
                enabledSkills.Select(skill => skill.Name),
                StringComparer.Ordinal);
            managedSkillNames.UnionWith(disabledSkills.Select(skill => skill.Name));
            managedSkillNames.UnionWith(DeprecatedSkillNames);
            Directory.CreateDirectory(skillsRoot);

            if (groupSkillsUnderUnityCliLoop)
            {
                Directory.CreateDirectory(SkillInstallLayout.GetManagedSkillsRoot(targetRoot));
            }

            DeleteDeprecatedSkillDirectoriesFromAllLayouts(targetRoot);
            DeleteDisabledSkillDirectoriesFromAllLayouts(targetRoot, disabledSkills);

            foreach (SkillInstallLayout.SkillSourceInfo skill in enabledSkills)
            {
                string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop);
                SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(installedSkillDirectory, skill.SkillFiles);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, !groupSkillsUnderUnityCliLoop);
            }

            DeleteUnexpectedInstalledSkillDirectories(
                targetRoot,
                enabledSkills.Select(skill => skill.Name),
                managedSkillNames,
                groupSkillsUnderUnityCliLoop);

            if (!groupSkillsUnderUnityCliLoop)
            {
                DeleteUnexpectedInstalledSkillDirectories(
                    targetRoot,
                    enabledSkills.Select(skill => skill.Name),
                    managedSkillNames,
                    groupSkillsUnderUnityCliLoop: true);
                DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
                    targetRoot,
                    groupSkillsUnderUnityCliLoop: true);
            }
        }

        private static void InstallSpecificSkillsForTarget(
            string projectRoot,
            SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> skills,
            bool groupSkillsUnderUnityCliLoop)
        {
            string targetRoot = Path.Combine(projectRoot, target.DirName);
            string skillsRoot = SkillInstallLayout.GetSkillsRoot(targetRoot);
            Directory.CreateDirectory(skillsRoot);

            if (groupSkillsUnderUnityCliLoop)
            {
                Directory.CreateDirectory(SkillInstallLayout.GetManagedSkillsRoot(targetRoot));
            }

            DeleteDeprecatedSkillDirectoriesForLayout(targetRoot, groupSkillsUnderUnityCliLoop);
            DeleteDisabledSkillDirectoriesForLayout(targetRoot, disabledSkills, groupSkillsUnderUnityCliLoop);

            foreach (SkillInstallLayout.SkillSourceInfo skill in skills)
            {
                string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop);
                SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(installedSkillDirectory, skill.SkillFiles);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, !groupSkillsUnderUnityCliLoop);
            }

            if (!groupSkillsUnderUnityCliLoop)
            {
                DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
                    targetRoot,
                    groupSkillsUnderUnityCliLoop: true);
            }
        }

        private static void DeleteUnexpectedInstalledSkillDirectories(
            string targetRoot,
            IEnumerable<string> expectedSkillNames,
            IEnumerable<string> removableSkillNames,
            bool groupSkillsUnderUnityCliLoop)
        {
            HashSet<string> expectedSkillNameSet = new(expectedSkillNames, StringComparer.Ordinal);
            HashSet<string> removableSkillNameSet = new(removableSkillNames, StringComparer.Ordinal);
            foreach (string installedSkillName in SkillInstallLayout.EnumerateInstalledSkillDirectoryNamesForLayout(
                         targetRoot,
                         groupSkillsUnderUnityCliLoop))
            {
                if (expectedSkillNameSet.Contains(installedSkillName))
                {
                    continue;
                }

                if (!removableSkillNameSet.Contains(installedSkillName))
                {
                    continue;
                }

                DeleteSkillDirectoryIfExists(targetRoot, installedSkillName, groupSkillsUnderUnityCliLoop);
            }
        }

        private static void DeleteDeprecatedSkillDirectoriesFromAllLayouts(string targetRoot)
        {
            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: true);
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: false);
            }
        }

        private static void DeleteDisabledSkillDirectoriesFromAllLayouts(
            string targetRoot,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills)
        {
            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: true);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: false);
            }
        }

        private static void DeleteDeprecatedSkillDirectoriesForLayout(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop);
            }
        }

        private static void DeleteDisabledSkillDirectoriesForLayout(
            string targetRoot,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop);
            }
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
            DeleteEmptyManagedSkillsParentDirectoryIfNeeded(targetRoot, groupSkillsUnderUnityCliLoop);
        }

        private static void DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            if (!groupSkillsUnderUnityCliLoop)
            {
                return;
            }

            string managedSkillsRoot = SkillInstallLayout.GetManagedSkillsRoot(targetRoot);
            if (!Directory.Exists(managedSkillsRoot))
            {
                return;
            }

            DeleteExcludedFilesAtRoot(managedSkillsRoot);
            DeleteEmptyDirectoriesAtRoot(managedSkillsRoot);
            if (Directory.EnumerateFileSystemEntries(managedSkillsRoot).Any())
            {
                return;
            }

            Directory.Delete(managedSkillsRoot);
        }

        private static void DeleteExcludedFilesAtRoot(string directoryPath)
        {
            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                string fileName = Path.GetFileName(filePath);
                if (!SkillDirectoryContentSynchronizer.IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }

        private static void DeleteEmptyDirectoriesAtRoot(string directoryPath)
        {
            foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
            {
                SkillDirectoryContentSynchronizer.DeleteEmptyDirectories(childDirectoryPath);
                if (Directory.EnumerateFileSystemEntries(childDirectoryPath).Any())
                {
                    continue;
                }

                Directory.Delete(childDirectoryPath);
            }
        }
    }
}
