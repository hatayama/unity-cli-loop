using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Describes one agent tool target that can receive Unity CLI Loop skill files.
    /// </summary>
    public readonly struct SkillSetupTargetInfo
    {
        public readonly string DisplayName;
        public readonly string DirName;
        public readonly string InstallFlag;
        public readonly bool HasSkillsDirectory;
        public readonly bool HasExistingSkills;
        public readonly bool HasDifferentLayoutSkills;
        public readonly SkillInstallState InstallState;

        public SkillSetupTargetInfo(
            string displayName,
            string dirName,
            string installFlag,
            bool hasSkillsDirectory,
            bool hasExistingSkills,
            bool hasDifferentLayoutSkills = false,
            SkillInstallState installState = SkillInstallState.Missing)
        {
            DisplayName = displayName;
            DirName = dirName;
            InstallFlag = installFlag;
            HasSkillsDirectory = hasSkillsDirectory;
            HasExistingSkills = hasExistingSkills;
            HasDifferentLayoutSkills = hasDifferentLayoutSkills;
            InstallState = installState;
        }
    }

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

    /// <summary>
    /// Coordinates skill setup policy while infrastructure owns project file-system changes.
    /// </summary>
    public sealed class SkillSetupService
    {
        private readonly ISkillSetupPort _skillSetupPort;

        public SkillSetupService(ISkillSetupPort skillSetupPort)
        {
            Debug.Assert(skillSetupPort != null, "skillSetupPort must not be null");

            _skillSetupPort = skillSetupPort ?? throw new ArgumentNullException(nameof(skillSetupPort));
        }

        public void RemoveSkillFiles(string toolName)
        {
            _skillSetupPort.RemoveSkillFiles(toolName);
        }

        public bool IsSkillInstalled(string toolName)
        {
            return _skillSetupPort.IsSkillInstalled(toolName);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupPort.DetectSkillTargetsForLayoutAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupPort.DetectSkillTargetsForLayoutFastAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.InstallSkillFilesAsync(
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.InstallSkillFilesForToolAsync(
                toolName,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillSetupTargetInfo target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return _skillSetupPort.GetV3MigrationSkillInstallStateAtProjectRoot(
                projectRoot,
                target,
                groupSkillsUnderUnityCliLoop);
        }

        public Task InstallV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.InstallV3MigrationSkillFilesAsync(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public Task RemoveV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            return _skillSetupPort.RemoveV3MigrationSkillFilesAsync(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }
    }
}
