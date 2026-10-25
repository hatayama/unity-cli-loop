using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the file-system boundary for skill setup operations.
    /// Every operation reads skill sources from the Unity project root and writes agent skill
    /// folders under the saved skill install root, so callers never resolve the install root.
    /// Installs into an unavailable install root (the Git repository root when no Git repository
    /// contains the project) throw InvalidOperationException; detection reports no targets there.
    /// </summary>
    public interface ISkillSetupPort
    {
        SkillInstallRootSelection GetSkillInstallRootSelection(string projectRoot);
        void SaveSkillInstallRoot(string projectRoot, SkillInstallRoot root);
        void RemoveSkillFiles(string toolName);
        bool IsSkillInstalled(string toolName);
        List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop);
        List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop);
        Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
        Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
        SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillSetupTargetInfo target,
            bool groupSkillsUnderUnityCliLoop);
        Task InstallV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
        Task RemoveV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct);
    }
}
