using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

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
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
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

            DeleteDeprecatedSkillDirectoriesFromAllLayouts(targetRoot, ct);
            DeleteDisabledSkillDirectoriesFromAllLayouts(targetRoot, disabledSkills, ct);

            foreach (SkillInstallLayout.SkillSourceInfo skill in enabledSkills)
            {
                ct.ThrowIfCancellationRequested();
                string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop);
                SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(installedSkillDirectory, skill.SkillFiles, ct);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, !groupSkillsUnderUnityCliLoop, ct);
            }

            DeleteUnexpectedInstalledSkillDirectories(
                targetRoot,
                enabledSkills.Select(skill => skill.Name),
                managedSkillNames,
                groupSkillsUnderUnityCliLoop,
                ct);

            if (!groupSkillsUnderUnityCliLoop)
            {
                DeleteUnexpectedInstalledSkillDirectories(
                    targetRoot,
                    enabledSkills.Select(skill => skill.Name),
                    managedSkillNames,
                    groupSkillsUnderUnityCliLoop: true,
                    ct);
                DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
                    targetRoot,
                    groupSkillsUnderUnityCliLoop: true,
                    ct);
            }
        }

        internal static void InstallSpecificSkillsForTarget(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> skills,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string targetRoot = Path.Combine(projectRoot, target.DirName);
            string skillsRoot = SkillInstallLayout.GetSkillsRoot(targetRoot);
            Directory.CreateDirectory(skillsRoot);

            if (groupSkillsUnderUnityCliLoop)
            {
                Directory.CreateDirectory(SkillInstallLayout.GetManagedSkillsRoot(targetRoot));
            }

            DeleteDeprecatedSkillDirectoriesForLayout(targetRoot, groupSkillsUnderUnityCliLoop, ct);
            DeleteDisabledSkillDirectoriesForLayout(targetRoot, disabledSkills, groupSkillsUnderUnityCliLoop, ct);

            foreach (SkillInstallLayout.SkillSourceInfo skill in skills)
            {
                ct.ThrowIfCancellationRequested();
                string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop);
                SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(installedSkillDirectory, skill.SkillFiles, ct);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, !groupSkillsUnderUnityCliLoop, ct);
            }

            if (!groupSkillsUnderUnityCliLoop)
            {
                DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
                    targetRoot,
                    groupSkillsUnderUnityCliLoop: true,
                    ct);
            }
        }

        internal static void DeleteSkillDirectoryIfExists(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string installedSkillDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                skillName,
                groupSkillsUnderUnityCliLoop);
            if (!Directory.Exists(installedSkillDirectory))
            {
                return;
            }

            Directory.Delete(installedSkillDirectory, true);
            DeleteEmptyManagedSkillsParentDirectoryIfNeeded(targetRoot, groupSkillsUnderUnityCliLoop, ct);
        }

        private static void DeleteUnexpectedInstalledSkillDirectories(
            string targetRoot,
            IEnumerable<string> expectedSkillNames,
            IEnumerable<string> removableSkillNames,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            HashSet<string> expectedSkillNameSet = new(expectedSkillNames, StringComparer.Ordinal);
            HashSet<string> removableSkillNameSet = new(removableSkillNames, StringComparer.Ordinal);
            foreach (string installedSkillName in SkillInstallLayout.EnumerateInstalledSkillDirectoryNamesForLayout(
                         targetRoot,
                         groupSkillsUnderUnityCliLoop))
            {
                ct.ThrowIfCancellationRequested();
                if (expectedSkillNameSet.Contains(installedSkillName))
                {
                    continue;
                }

                if (!removableSkillNameSet.Contains(installedSkillName))
                {
                    continue;
                }

                DeleteSkillDirectoryIfExists(targetRoot, installedSkillName, groupSkillsUnderUnityCliLoop, ct);
            }
        }

        private static void DeleteDeprecatedSkillDirectoriesFromAllLayouts(string targetRoot, CancellationToken ct)
        {
            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: true, ct);
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop: false, ct);
            }
        }

        private static void DeleteDisabledSkillDirectoriesFromAllLayouts(
            string targetRoot,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            CancellationToken ct)
        {
            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: true, ct);
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop: false, ct);
            }
        }

        private static void DeleteDeprecatedSkillDirectoriesForLayout(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoryIfExists(targetRoot, deprecatedSkillName, groupSkillsUnderUnityCliLoop, ct);
            }
        }

        private static void DeleteDisabledSkillDirectoriesForLayout(
            string targetRoot,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoryIfExists(targetRoot, skill.Name, groupSkillsUnderUnityCliLoop, ct);
            }
        }

        private static void DeleteEmptyManagedSkillsParentDirectoryIfNeeded(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!groupSkillsUnderUnityCliLoop)
            {
                return;
            }

            string managedSkillsRoot = SkillInstallLayout.GetManagedSkillsRoot(targetRoot);
            if (!Directory.Exists(managedSkillsRoot))
            {
                return;
            }

            DeleteExcludedFilesAtRoot(managedSkillsRoot, ct);
            DeleteEmptyDirectoriesAtRoot(managedSkillsRoot, ct);
            if (Directory.EnumerateFileSystemEntries(managedSkillsRoot).Any())
            {
                return;
            }

            Directory.Delete(managedSkillsRoot);
        }

        private static void DeleteExcludedFilesAtRoot(string directoryPath, CancellationToken ct)
        {
            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                ct.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(filePath);
                if (!SkillDirectoryContentSynchronizer.IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }

        private static void DeleteEmptyDirectoriesAtRoot(string directoryPath, CancellationToken ct)
        {
            foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
            {
                ct.ThrowIfCancellationRequested();
                SkillDirectoryContentSynchronizer.DeleteEmptyDirectories(childDirectoryPath, ct);
                if (Directory.EnumerateFileSystemEntries(childDirectoryPath).Any())
                {
                    continue;
                }

                Directory.Delete(childDirectoryPath);
            }
        }
    }
}
