using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure adapter for project skill files managed by Unity CLI Loop.
    /// <summary>
    /// Provides Tool Skill Setup operations for its owning module.
    /// </summary>
    public sealed class ToolSkillSetupService : ISkillSetupPort
    {
        private readonly IToolSettingsPort _toolSettingsPort;
        private readonly SkillInstallRootResolver _installRootResolver = new();

        public ToolSkillSetupService(IToolSettingsPort toolSettingsPort)
        {
            Debug.Assert(toolSettingsPort != null, "toolSettingsPort must not be null");

            _toolSettingsPort = toolSettingsPort ?? throw new System.ArgumentNullException(nameof(toolSettingsPort));
        }

        public SkillInstallRootSelection GetSkillInstallRootSelection(string projectRoot)
        {
            return _installRootResolver.GetSelection(projectRoot);
        }

        public void SaveSkillInstallRoot(string projectRoot, SkillInstallRoot root)
        {
            _installRootResolver.SaveInstallRoot(projectRoot, root);
        }

        public void RemoveSkillFiles(string toolName)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            if (!TryGetInstallRootForToolSync(projectRoot, toolName, out string installRoot))
            {
                return;
            }

            ToolSkillSynchronizer.RemoveSkillFilesAtInstallRoot(installRoot, toolName);
        }

        public bool IsSkillInstalled(string toolName)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            return installRoot != null && ToolSkillSynchronizer.IsSkillInstalledAtInstallRoot(installRoot, toolName);
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            string installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            if (installRoot == null)
            {
                return new List<SkillSetupTargetInfo>();
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                ToolSkillSynchronizer.DetectTargetsForLayoutAtProjectRoot(
                    projectRoot,
                    installRoot,
                    groupSkillsUnderUnityCliLoop,
                    _toolSettingsPort.GetDisabledTools());
            return targets.Select(ToDomainInfo).ToList();
        }

        public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
            string projectRoot,
            bool groupSkillsUnderUnityCliLoop)
        {
            string installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            if (installRoot == null)
            {
                return new List<SkillSetupTargetInfo>();
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                ToolSkillSynchronizer.DetectTargetsForLayoutFastAtProjectRoot(
                    projectRoot,
                    installRoot,
                    groupSkillsUnderUnityCliLoop);
            return targets.Select(ToDomainInfo).ToList();
        }

        public async Task InstallSkillFilesAsync(
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string installRoot = GetInstallRootOrThrow(projectRoot);
            List<ToolSkillSynchronizer.SkillTargetInfo> synchronizerTargets = targets
                .Select(ToSynchronizerInfo)
                .ToList();
            await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                projectRoot,
                installRoot,
                synchronizerTargets,
                groupSkillsUnderUnityCliLoop,
                _toolSettingsPort.GetDisabledTools(),
                ct);
        }

        public async Task InstallSkillFilesForToolAsync(
            string toolName,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            ct.ThrowIfCancellationRequested();

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            if (!TryGetInstallRootForToolSync(projectRoot, toolName, out string installRoot))
            {
                return;
            }

            await ToolSkillSynchronizer.InstallSkillFilesForToolAtProjectRoot(
                projectRoot,
                installRoot,
                toolName,
                groupSkillsUnderUnityCliLoop,
                _toolSettingsPort.GetDisabledTools(),
                ct);
        }

        public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
            string projectRoot,
            SkillSetupTargetInfo target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            if (installRoot == null)
            {
                return SkillInstallState.Missing;
            }

            return ToolSkillSynchronizer.GetV3MigrationSkillInstallStateAtInstallRoot(
                installRoot,
                ToSynchronizerInfo(target),
                groupSkillsUnderUnityCliLoop);
        }

        public async Task InstallV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            string installRoot = GetInstallRootOrThrow(projectRoot);
            List<ToolSkillSynchronizer.SkillTargetInfo> synchronizerTargets = targets
                .Select(ToSynchronizerInfo)
                .ToList();
            await ToolSkillSynchronizer.InstallV3MigrationSkillFilesAtInstallRoot(
                installRoot,
                synchronizerTargets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        public async Task RemoveV3MigrationSkillFilesAsync(
            string projectRoot,
            List<SkillSetupTargetInfo> targets,
            bool groupSkillsUnderUnityCliLoop,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(targets != null, "targets must not be null");
            ct.ThrowIfCancellationRequested();

            string installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            if (installRoot == null)
            {
                return;
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> synchronizerTargets = targets
                .Select(ToSynchronizerInfo)
                .ToList();
            await ToolSkillSynchronizer.RemoveV3MigrationSkillFilesAtInstallRoot(
                installRoot,
                synchronizerTargets,
                groupSkillsUnderUnityCliLoop,
                ct);
        }

        // An explicit install must not silently land somewhere the user did not choose.
        private string GetInstallRootOrThrow(string projectRoot)
        {
            SkillInstallRootSelection selection = _installRootResolver.GetSelection(projectRoot);
            if (!selection.IsSelectedRootAvailable)
            {
                throw new System.InvalidOperationException(
                    $"No Git repository contains the Unity project '{projectRoot}', so skills cannot be " +
                    "installed at the Git repository root. Choose the Unity project root as the install location.");
            }

            return selection.SelectedRootPath;
        }

        // Toggling a tool must keep working when skills cannot be synced, so an unavailable
        // install root only skips the sync instead of failing the toggle.
        private bool TryGetInstallRootForToolSync(string projectRoot, string toolName, out string installRoot)
        {
            installRoot = _installRootResolver.GetSelection(projectRoot).SelectedRootPath;
            if (installRoot != null)
            {
                return true;
            }

            UnityEngine.Debug.LogWarning(
                $"[UnityCliLoop] Skipped syncing the skill for '{toolName}': no Git repository contains the " +
                "Unity project, but the Git repository root is chosen as the skill install location.");
            return false;
        }

        private static SkillSetupTargetInfo ToDomainInfo(ToolSkillSynchronizer.SkillTargetInfo target)
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
