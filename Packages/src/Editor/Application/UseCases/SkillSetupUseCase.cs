using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Orchestrates skill setup workflows while target policy stays in the domain layer.
    /// </summary>
    public sealed class SkillSetupUseCase
    {
        private readonly SkillSetupService _skillSetupService;

        public SkillSetupUseCase(SkillSetupService skillSetupService)
        {
            Debug.Assert(skillSetupService != null, "skillSetupService must not be null");

            _skillSetupService = skillSetupService ?? throw new ArgumentNullException(nameof(skillSetupService));
        }

        public void RemoveSkillFiles(string toolName)
        {
            _skillSetupService.RemoveSkillFiles(toolName);
        }

        public bool IsSkillInstalled(string toolName)
        {
            return _skillSetupService.IsSkillInstalled(toolName);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupService.DetectSkillTargetsForLayoutAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupService.DetectSkillTargetsForLayoutFastAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop);
        }

        public Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");

            return _skillSetupService.InstallSkillFilesAsync(
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            return _skillSetupService.InstallSkillFilesForToolAsync(
                toolName,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillSetupTargetInfo target,
            bool groupSkillsUnderUnityCliLoop)
        {
            return _skillSetupService.GetV3MigrationSkillInstallStateAtProjectRoot(
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
            Debug.Assert(targets != null, "targets must not be null");

            return _skillSetupService.InstallV3MigrationSkillFilesAsync(
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
            Debug.Assert(targets != null, "targets must not be null");

            return _skillSetupService.RemoveV3MigrationSkillFilesAsync(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }
    }
}
