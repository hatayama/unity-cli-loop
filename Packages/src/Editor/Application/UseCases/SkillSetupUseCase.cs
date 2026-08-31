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
        private readonly ISkillSetupPort _skillSetupPort;

        public SkillSetupUseCase(ISkillSetupPort skillSetupPort)
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
            Debug.Assert(targets != null, "targets must not be null");

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
            Debug.Assert(targets != null, "targets must not be null");

            return _skillSetupPort.RemoveV3MigrationSkillFilesAsync(
                projectRoot,
                targets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }
    }
}
