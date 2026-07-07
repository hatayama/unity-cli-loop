using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Installs and removes managed skill directories for one target root.
    /// </summary>
    internal static class SkillTargetInstaller
    {
        private static readonly string[] DeprecatedSkillNames =
        {
            "uloop-capture-window",
            "uloop-get-provider-details",
            "uloop-unity-search",
            "uloop-get-menu-items",
            "uloop-get-unity-search-providers",
            "uloop-execute-menu-item"
        };

        internal static void InstallSkillsForTarget(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
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

        internal static void InstallSpecificSkillsForTarget(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
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

        internal static void DeleteSkillDirectoryIfExists(
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
