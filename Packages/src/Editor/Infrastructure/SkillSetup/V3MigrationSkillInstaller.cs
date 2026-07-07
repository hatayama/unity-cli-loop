using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Installs, detects, and removes the temporary v3 CLI invocation migration skill.
    /// </summary>
    internal static class V3MigrationSkillInstaller
    {
        private static readonly string V3MigrationSkillSourceDirectory = Path.Combine(
            UnityCliLoopConstants.PackageResolvedPath,
            CliConstants.TEMPORARY_SKILLS_DIR_NAME,
            CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
            "Skill");

        internal static SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
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

        internal static async Task<ToolSkillSynchronizer.SkillInstallResult> InstallV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            SkillInstallLayout.SkillSourceInfo skill = GetV3MigrationSkillSourceInfo();
            return await InstallSpecificSkillFilesAtProjectRoot(
                projectRoot,
                targets,
                skill,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        internal static async Task<ToolSkillSynchronizer.SkillInstallResult> RemoveV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            SkillInstallLayout.SkillSourceInfo skill = GetV3MigrationSkillSourceInfo();
            ToolSkillSynchronizer.SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (ToolSkillSynchronizer.SkillTargetInfo target in targetArray)
                {
                    ct.ThrowIfCancellationRequested();
                    string targetRoot = Path.Combine(projectRoot, target.DirName);
                    SkillTargetInstaller.DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skill.Name,
                        groupSkillsUnderUnityCliLoop,
                        ct);
                    SkillTargetInstaller.DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skill.Name,
                        !groupSkillsUnderUnityCliLoop,
                        ct);
                    succeeded++;
                }

                return new ToolSkillSynchronizer.SkillInstallResult(targetArray.Length, succeeded);
            }, ct);
        }

        internal static SkillInstallState GetSkillInstallStateAtProjectRoot(
            string projectRoot,
            ToolSkillSynchronizer.SkillTargetInfo target,
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

        internal static async Task<ToolSkillSynchronizer.SkillInstallResult> InstallSpecificSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            SkillInstallLayout.SkillSourceInfo skill,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            ToolSkillSynchronizer.SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (ToolSkillSynchronizer.SkillTargetInfo target in targetArray)
                {
                    ct.ThrowIfCancellationRequested();
                    SkillTargetInstaller.InstallSpecificSkillsForTarget(
                        projectRoot,
                        target,
                        Array.Empty<SkillInstallLayout.SkillSourceInfo>(),
                        new[] { skill },
                        groupSkillsUnderUnityCliLoop,
                        ct);
                    succeeded++;
                }

                return new ToolSkillSynchronizer.SkillInstallResult(targetArray.Length, succeeded);
            }, ct);
        }

        internal static async Task<ToolSkillSynchronizer.SkillInstallResult> RemoveSpecificSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            string skillName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            Debug.Assert(!string.IsNullOrEmpty(skillName), "skillName must not be null or empty");
            ct.ThrowIfCancellationRequested();

            ToolSkillSynchronizer.SkillTargetInfo[] targetArray = targets.ToArray();
            return await Task.Run(() =>
            {
                int succeeded = 0;
                foreach (ToolSkillSynchronizer.SkillTargetInfo target in targetArray)
                {
                    ct.ThrowIfCancellationRequested();
                    string targetRoot = Path.Combine(projectRoot, target.DirName);
                    SkillTargetInstaller.DeleteSkillDirectoryIfExists(
                        targetRoot,
                        skillName,
                        groupSkillsUnderUnityCliLoop,
                        ct);
                    succeeded++;
                }

                return new ToolSkillSynchronizer.SkillInstallResult(targetArray.Length, succeeded);
            }, ct);
        }

        private static SkillInstallLayout.SkillSourceInfo GetV3MigrationSkillSourceInfo()
        {
            SkillInstallLayout.SkillSourceInfo skill =
                SkillInstallLayout.GetSkillSourceInfoFromDirectory(V3MigrationSkillSourceDirectory);
            ValidateV3MigrationSkillSourceName(skill);
            return skill;
        }

        internal static void ValidateV3MigrationSkillSourceName(SkillInstallLayout.SkillSourceInfo skill)
        {
            if (string.Equals(
                    skill.Name,
                    CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"V3 migration skill source name must be '{CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME}', but was '{skill.Name}'.");
        }
    }
}
