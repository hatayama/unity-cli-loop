using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Checks whether project-local agent skill files have been installed for a target client.
    /// </summary>
    public sealed class SkillInstallationDetector
    {
        public bool AreSkillsInstalled(string targetDir)
        {
            Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return AreSkillsInstalled(projectRoot, targetDir);
        }

        public bool AreSkillsInstalled(string targetDir, bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return AreSkillsInstalled(projectRoot, targetDir, groupSkillsUnderUnityCliLoop);
        }

        internal bool AreSkillsInstalled(string projectRoot, string targetDir)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, targetDir);
            return SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot);
        }

        internal bool AreSkillsInstalled(
            string projectRoot,
            string targetDir,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, targetDir);
            return SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop);
        }
    }
}
