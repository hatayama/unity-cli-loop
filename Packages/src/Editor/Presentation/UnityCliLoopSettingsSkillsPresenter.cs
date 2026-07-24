using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Snapshot of skills UI state consumed by the CLI setup section refresh.
    /// </summary>
    internal readonly struct UnityCliLoopSettingsSkillsSnapshot
    {
        internal UnityCliLoopSettingsSkillsSnapshot(
            bool installSkillsFlat,
            SkillInstallState selectedTargetInstallState,
            SkillsTarget skillsTarget,
            bool isInstallingSkills,
            IReadOnlyList<SkillSetupTargetInfo> installableSkillTargets)
        {
            InstallSkillsFlat = installSkillsFlat;
            SelectedTargetInstallState = selectedTargetInstallState;
            SkillsTarget = skillsTarget;
            IsInstallingSkills = isInstallingSkills;
            InstallableSkillTargets = installableSkillTargets;
        }

        internal bool InstallSkillsFlat { get; }
        internal SkillInstallState SelectedTargetInstallState { get; }
        internal SkillsTarget SkillsTarget { get; }
        internal bool IsInstallingSkills { get; }
        internal IReadOnlyList<SkillSetupTargetInfo> InstallableSkillTargets { get; }
    }

    /// <summary>
    /// Presents skills install-state refresh and install workflows for Settings.
    /// </summary>
    internal sealed class UnityCliLoopSettingsSkillsPresenter
    {
        private const bool ForceFlatSkillInstall = true;

        private readonly SkillSetupUseCase _skillSetupUseCase;
        private readonly CliSetupApplicationService _cliSetupApplicationService;
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;

        private Action<bool> _refreshCliSetupSection;
        private Func<bool> _isRefreshingVersion;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installSkillsFlat = ForceFlatSkillInstall;
        private bool _isInstallingSkills;
        private SkillInstallState _selectedTargetInstallState = SkillInstallState.Missing;
        private List<SkillSetupTargetInfo> _installableTargets = new();
        private CancellationTokenSource _skillInstallStateRefreshCts;

        internal UnityCliLoopSettingsSkillsPresenter(
            SkillSetupUseCase skillSetupUseCase,
            CliSetupApplicationService cliSetupApplicationService,
            IUnityCliLoopEditorSettingsPort editorSettingsPort)
        {
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");

            _skillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
        }

        internal void BindCoordination(
            Action<bool> refreshCliSetupSection,
            Func<bool> isRefreshingVersion)
        {
            Debug.Assert(refreshCliSetupSection != null, "refreshCliSetupSection must not be null");
            Debug.Assert(isRefreshingVersion != null, "isRefreshingVersion must not be null");

            _refreshCliSetupSection = refreshCliSetupSection
                ?? throw new ArgumentNullException(nameof(refreshCliSetupSection));
            _isRefreshingVersion = isRefreshingVersion
                ?? throw new ArgumentNullException(nameof(isRefreshingVersion));
        }

        internal UnityCliLoopSettingsSkillsSnapshot GetSnapshot()
        {
            return new UnityCliLoopSettingsSkillsSnapshot(
                _installSkillsFlat,
                _selectedTargetInstallState,
                _skillsTarget,
                _isInstallingSkills,
                _installableTargets);
        }

        internal void MarkSelectedTargetInstallStateChecking()
        {
            _selectedTargetInstallState = SkillInstallState.Checking;
        }

        internal void HandleSkillsTargetChanged(SkillsTarget value)
        {
            _skillsTarget = value;
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
        }

        internal void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
        }

        internal void HandleGroupSkillsChanged(bool groupSkillsUnderUnityCliLoop)
        {
            ApplyFlatSkillInstallPreference();
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

        internal void ApplyFlatSkillInstallPreference()
        {
            // Claude Code does not resolve nested skill folders, so editor-driven installs stay flat for every target.
            _installSkillsFlat = ForceFlatSkillInstall;
            _editorSettingsPort.SetInstallSkillsFlat(_installSkillsFlat);
        }

        internal void CancelSkillInstallStateRefresh()
        {
            if (_skillInstallStateRefreshCts == null)
            {
                return;
            }

            _skillInstallStateRefreshCts.Cancel();
            _skillInstallStateRefreshCts.Dispose();
            _skillInstallStateRefreshCts = null;
        }

        internal void RefreshSelectedTargetInstallStateFast()
        {
            if (!_cliSetupApplicationService.IsCliInstalled())
            {
                _selectedTargetInstallState = SkillInstallState.Missing;
                _installableTargets = new List<SkillSetupTargetInfo>();
                RefreshCliSetupSection();
                return;
            }

            (SkillSetupTargetInfo selectedTargetInfo, List<SkillSetupTargetInfo> allTargets) =
                GetSelectedTargetInfo(UnityCliLoopPathResolver.GetProjectRoot(), includeFreshnessCheck: false);
            _selectedTargetInstallState = string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? SkillInstallState.Missing
                : selectedTargetInfo.InstallState;
            _installableTargets = SkillsSetupPanelView.FilterInstallableSkillTargets(allTargets);
            RefreshCliSetupSection();
        }

        internal void RefreshSelectedTargetInstallStateInBackground(bool allowDuringCliRefresh = false)
        {
            CancelSkillInstallStateRefresh();
            bool isCliInstalled = _cliSetupApplicationService.IsCliInstalled();
            if (!UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartSkillInstallStateRefresh(
                    isCliInstalled,
                    _isRefreshingVersion(),
                    _isInstallingSkills,
                    allowDuringCliRefresh))
            {
                SkillInstallState resolvedInstallState =
                    UnityCliLoopSettingsWindowRefreshPolicy.ResolveSkillInstallStateWhenRefreshCannotStart(
                        isCliInstalled,
                        _selectedTargetInstallState);
                if (_selectedTargetInstallState != resolvedInstallState)
                {
                    _selectedTargetInstallState = resolvedInstallState;
                    RefreshCliSetupSection();
                }
                return;
            }

            CancellationTokenSource cts = new();
            _skillInstallStateRefreshCts = cts;
            RefreshSelectedTargetInstallStateAsync(cts.Token).Forget();
        }

        internal async Task HandleInstallSkills()
        {
            if (!_cliSetupApplicationService.IsCliInstalled())
            {
                EditorUtility.DisplayDialog(
                    "CLI Not Found",
                    "uloop CLI is not installed. Please install the CLI first.",
                    "OK");
                return;
            }

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            (SkillSetupTargetInfo selectedTargetInfo, List<SkillSetupTargetInfo> _) =
                GetSelectedTargetInfo(projectRoot, includeFreshnessCheck: true);
            bool shouldShowSkillsInstalledDialog =
                SkillInstallDialogPolicy.ShouldShowForSelectedTarget(selectedTargetInfo);
            CancelSkillInstallStateRefresh();
            _isInstallingSkills = true;
            RefreshCliSetupSection();

            try
            {
                SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                    _skillsTarget,
                    !_installSkillsFlat);
                SkillSetupTargetInfo target = new(
                    selection.DisplayName,
                    selection.DirectoryName,
                    selection.InstallFlag,
                    hasSkillsDirectory: true,
                    hasExistingSkills: false,
                    hasDifferentLayoutSkills: false,
                    SkillInstallState.Missing);
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    new List<SkillSetupTargetInfo> { target },
                    !_installSkillsFlat,
                    CancellationToken.None);
                if (shouldShowSkillsInstalledDialog)
                {
                    EditorDialogHelper.ShowSkillsInstalledDialog();
                }
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSelectedTargetInstallStateFast();
                RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
                RefreshCliSetupSection();
            }
        }

        internal async Task HandleInstallAllSkills(CancellationToken ct)
        {
            if (!_cliSetupApplicationService.IsCliInstalled())
            {
                EditorUtility.DisplayDialog(
                    "CLI Not Found",
                    "uloop CLI is not installed. Please install the CLI first.",
                    "OK");
                return;
            }

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets =
                _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat);
            List<SkillSetupTargetInfo> installable =
                SkillsSetupPanelView.FilterInstallableSkillTargets(targets);
            if (installable.Count == 0)
            {
                return;
            }

            bool shouldShowSkillsInstalledDialog =
                SkillInstallDialogPolicy.ShouldShowForInstallableTargets(installable);
            CancelSkillInstallStateRefresh();
            _isInstallingSkills = true;
            RefreshCliSetupSection();

            try
            {
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    installable,
                    !_installSkillsFlat,
                    ct);
                if (shouldShowSkillsInstalledDialog)
                {
                    EditorDialogHelper.ShowSkillsInstalledDialog();
                }
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSelectedTargetInstallStateFast();
                RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
                RefreshCliSetupSection();
            }
        }

        internal async Task ApplyToolToggleSideEffects(string toolName, bool enabled)
        {
            if (!enabled)
            {
                _skillSetupUseCase.RemoveSkillFiles(toolName);
            }
            else
            {
                await _skillSetupUseCase.InstallSkillFilesForToolAsync(
                    toolName,
                    !_installSkillsFlat,
                    CancellationToken.None);

                if (!_skillSetupUseCase.IsSkillInstalled(toolName))
                {
                    Debug.LogWarning(
                        $"[UnityCliLoop] Skill for '{toolName}' was not installed after enabling. " +
                        "The skill source may have an incorrect directory structure " +
                        "(expected: <ToolDir>/Skill/SKILL.md). Run 'uloop skills list' for details."
                    );
                }
            }
        }

        private async Task RefreshSelectedTargetInstallStateAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            (SkillSetupTargetInfo selectedTargetInfo, List<SkillSetupTargetInfo> allTargets) =
                await Task.Run(() => GetSelectedTargetInfo(projectRoot, includeFreshnessCheck: true));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _selectedTargetInstallState = string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? SkillInstallState.Missing
                : selectedTargetInfo.InstallState;
            _installableTargets = SkillsSetupPanelView.FilterInstallableSkillTargets(allTargets);
            RefreshCliSetupSection();
        }

        private (SkillSetupTargetInfo selectedInfo, List<SkillSetupTargetInfo> allTargets) GetSelectedTargetInfo(
            string projectRoot,
            bool includeFreshnessCheck)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                _skillsTarget,
                !_installSkillsFlat);
            List<SkillSetupTargetInfo> targets = includeFreshnessCheck
                ? _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat)
                : _skillSetupUseCase.DetectSkillTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
            SkillSetupTargetInfo targetInfo = targets
                .FirstOrDefault(target => target.DirName == selection.DirectoryName);

            return (targetInfo, targets);
        }

        private void RefreshCliSetupSection(bool includeSkillDirectoryChecks = true)
        {
            Debug.Assert(_refreshCliSetupSection != null, "BindCoordination must be called before refresh");
            _refreshCliSetupSection(includeSkillDirectoryChecks);
        }
    }
}
