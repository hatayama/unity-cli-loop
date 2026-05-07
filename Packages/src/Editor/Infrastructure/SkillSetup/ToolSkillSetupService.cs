using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure adapter for project skill files managed by Unity CLI Loop.
    /// <summary>
    /// Provides Tool Skill Setup operations for its owning module.
    /// </summary>
    public sealed class ToolSkillSetupService : ISkillSetupPort
    {
        public void RemoveSkillFiles(string toolName)
        {
            ToolSkillSynchronizer.RemoveSkillFiles(toolName);
        }

        public bool IsSkillInstalled(string toolName)
        {
            return ToolSkillSynchronizer.IsSkillInstalled(toolName);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                ToolSkillSynchronizer.DetectTargetsForLayoutAtProjectRoot(
                    projectRoot,
                    groupSkillsUnderUnityCliLoop);
            return targets.Select(ToApplicationInfo).ToList();
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                ToolSkillSynchronizer.DetectTargetsForLayoutFastAtProjectRoot(
                    projectRoot,
                    groupSkillsUnderUnityCliLoop);
            return targets.Select(ToApplicationInfo).ToList();
        }

        public async Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            List<ToolSkillSynchronizer.SkillTargetInfo> synchronizerTargets = targets
                .Select(ToSynchronizerInfo)
                .ToList();
            await ToolSkillSynchronizer.InstallSkillFiles(
                synchronizerTargets,
                groupSkillsUnderUnityCliLoop);
            ct.ThrowIfCancellationRequested();
        }

        public async Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            ct.ThrowIfCancellationRequested();

            await ToolSkillSynchronizer.InstallSkillFilesForTool(
                toolName,
                groupSkillsUnderUnityCliLoop);
            ct.ThrowIfCancellationRequested();
        }

        private static SkillSetupTargetInfo ToApplicationInfo(ToolSkillSynchronizer.SkillTargetInfo target)
        {
            return new SkillSetupTargetInfo(
                target.DisplayName,
                target.DirName,
                target.InstallFlag,
                target.HasSkillsDirectory,
                target.HasExistingSkills,
                target.HasDifferentLayoutSkills,
                target.InstallState);
        }

        private static ToolSkillSynchronizer.SkillTargetInfo ToSynchronizerInfo(SkillSetupTargetInfo target)
        {
            return new ToolSkillSynchronizer.SkillTargetInfo(
                target.DisplayName,
                target.DirName,
                target.InstallFlag,
                target.HasSkillsDirectory,
                target.HasExistingSkills,
                target.HasDifferentLayoutSkills,
                target.InstallState);
        }
    }
}
