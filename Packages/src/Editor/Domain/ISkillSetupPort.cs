using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the file-system boundary used by the skill setup domain service.
    /// </summary>
    public interface ISkillSetupPort
    {
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
