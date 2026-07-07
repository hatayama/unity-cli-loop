using System.Collections.Generic;
using System.IO;
using System.Linq;

using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Detects skill installation targets and their current install state.
    /// </summary>
    internal static class SkillTargetDetector
    {
        private readonly struct SkillTargetDefinition
        {
            internal readonly string DirName;
            internal readonly string Flag;
            internal readonly string DisplayName;

            internal SkillTargetDefinition(string dirName, string flag, string displayName)
            {
                DirName = dirName;
                Flag = flag;
                DisplayName = displayName;
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

        internal static readonly string[] SkillTargetDirs = SkillTargets.Select(t => t.DirName).ToArray();

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> DetectTargetsAcrossLayoutsAtProjectRoot(
            string projectRoot,
            bool requireSkillsDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new();

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

                bool hasULoopSkills = hasSkillsDirectory
                    && SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot);
                targets.Add(new ToolSkillSynchronizer.SkillTargetInfo(
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

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> DetectTargetsForLayoutStateAtProjectRoot(
            string projectRoot,
            bool requireSkillsDirectory,
            bool groupSkillsUnderUnityCliLoop,
            bool includeFreshnessCheck)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new();

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
                    && SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot, !groupSkillsUnderUnityCliLoop);
                targets.Add(new ToolSkillSynchronizer.SkillTargetInfo(
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

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> DetectTargetsWithSkillsDirectory(string projectRoot)
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new();
            foreach (SkillTargetDefinition target in SkillTargets)
            {
                string targetRoot = Path.Combine(projectRoot, target.DirName);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                bool hasSkillsDirectory = SkillInstallLayout.HasOptedInSkillsDirectory(targetRoot);
                if (!hasSkillsDirectory)
                {
                    continue;
                }

                targets.Add(new ToolSkillSynchronizer.SkillTargetInfo(
                    target.DisplayName,
                    target.DirName,
                    target.Flag,
                    hasSkillsDirectory,
                    hasExistingSkills: false));
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
                return SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop)
                    ? SkillInstallState.Checking
                    : SkillInstallState.Missing;
            }

            return SkillInstallLayout.GetInstalledState(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop);
        }
    }
}
