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
    public static partial class ToolSkillSynchronizer
    {
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
                    SkillTargetInstaller.InstallSkillsForTarget(
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
                    SkillTargetInstaller.InstallSpecificSkillsForTarget(
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
            return V3MigrationSkillInstaller.GetV3MigrationSkillInstallStateAtProjectRoot(
                projectRoot,
                target,
                groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> InstallV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            return await V3MigrationSkillInstaller.InstallV3MigrationSkillFilesAtProjectRoot(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop);
        }

        internal static async Task<SkillInstallResult> RemoveV3MigrationSkillFilesAtProjectRoot(
            string projectRoot,
            IEnumerable<SkillTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop)
        {
            return await V3MigrationSkillInstaller.RemoveV3MigrationSkillFilesAtProjectRoot(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop);
        }

    }
}
