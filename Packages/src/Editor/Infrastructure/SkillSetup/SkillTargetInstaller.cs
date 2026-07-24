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
        // Cleanup names for previously installed skill directories. Keep them here
        // and in the dispatcher's deprecatedSkillNames (cli/dispatcher/internal/
        // dispatcher/skills.go); agent-facing V2-to-V3 CLI migration guidance lives
        // in Packages/src/TemporarySkills~/v3-cli-invocation-migration.
        private static readonly string[] DeprecatedSkillNames =
        {
            "uloop-wait-for-pause-point",
            "uloop-capture-window",
            "uloop-get-provider-details",
            "uloop-unity-search",
            "uloop-get-menu-items",
            "uloop-get-unity-search-providers",
            "uloop-execute-menu-item"
        };

        private enum SkillSyncScope
        {
            FullSync,
            SpecificSkillsOnly
        }

        internal static void InstallSkillsForTarget(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> enabledSkills,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            InstallSkillsForTargetCore(
                projectRoot,
                target,
                disabledSkills,
                enabledSkills,
                groupSkillsUnderUnityCliLoop,
                SkillSyncScope.FullSync,
                ct);
        }

        internal static void InstallSpecificSkillsForTarget(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> skills,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            InstallSkillsForTargetCore(
                projectRoot,
                target,
                disabledSkills,
                skills,
                groupSkillsUnderUnityCliLoop,
                SkillSyncScope.SpecificSkillsOnly,
                ct);
        }

        private static void InstallSkillsForTargetCore(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> skills,
            bool groupSkillsUnderUnityCliLoop,
            SkillSyncScope syncScope,
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

            DeleteDeprecatedSkillDirectories(targetRoot, groupSkillsUnderUnityCliLoop, syncScope, ct);
            DeleteDisabledSkillDirectories(targetRoot, disabledSkills, groupSkillsUnderUnityCliLoop, syncScope, ct);

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

        private static void DeleteDeprecatedSkillDirectories(
            string targetRoot,
            bool groupSkillsUnderUnityCliLoop,
            SkillSyncScope syncScope,
            CancellationToken ct)
        {
            foreach (string deprecatedSkillName in DeprecatedSkillNames)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoriesForScope(
                    targetRoot,
                    deprecatedSkillName,
                    groupSkillsUnderUnityCliLoop,
                    syncScope,
                    ct);
            }
        }

        private static void DeleteDisabledSkillDirectories(
            string targetRoot,
            IReadOnlyCollection<SkillInstallLayout.SkillSourceInfo> disabledSkills,
            bool groupSkillsUnderUnityCliLoop,
            SkillSyncScope syncScope,
            CancellationToken ct)
        {
            foreach (SkillInstallLayout.SkillSourceInfo skill in disabledSkills)
            {
                ct.ThrowIfCancellationRequested();
                DeleteSkillDirectoriesForScope(
                    targetRoot,
                    skill.Name,
                    groupSkillsUnderUnityCliLoop,
                    syncScope,
                    ct);
            }
        }

        private static void DeleteSkillDirectoriesForScope(
            string targetRoot,
            string skillName,
            bool groupSkillsUnderUnityCliLoop,
            SkillSyncScope syncScope,
            CancellationToken ct)
        {
            if (syncScope == SkillSyncScope.FullSync)
            {
                DeleteSkillDirectoryIfExists(targetRoot, skillName, groupSkillsUnderUnityCliLoop: true, ct);
                DeleteSkillDirectoryIfExists(targetRoot, skillName, groupSkillsUnderUnityCliLoop: false, ct);
                return;
            }

            DeleteSkillDirectoryIfExists(targetRoot, skillName, groupSkillsUnderUnityCliLoop, ct);
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
                if (!SkillSetupFileExclusion.IsExcludedSkillFile(fileName))
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
