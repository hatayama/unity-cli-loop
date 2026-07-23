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
    /// Decision made on each EditorApplication.update tick while polling for the
    /// compile-error-driven migration auto-scan trigger.
    /// </summary>
    public enum MigrationAutoScanPollAction
    {
        ContinueWaiting,
        RunDetection,
        FallBackToFullScan,
        Terminate
    }

    /// <summary>
    /// Coordinates setup wizard startup auto-show and migration auto-scan decisions.
    /// </summary>
    internal sealed class SetupWizardStartupFlow
    {
        private static readonly char[] VersionMajorSeparators = { '.', '-' };

        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly IThirdPartyToolMigrationAutoScanSeedRepository _autoScanSeedRepository;
        private readonly CliSetupApplicationService _cliSetupApplicationService;
        private readonly SkillSetupUseCase _skillSetupUseCase;
        private readonly ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;
        private readonly System.Action _showWindowOnVersionChange;
        private readonly System.Action _showThirdPartyMigrationAutoScan;
        private bool _migrationAutoScanPollingActive;
        private double _migrationAutoScanPollStartTime;

        internal SetupWizardStartupFlow(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            IThirdPartyToolMigrationAutoScanSeedRepository autoScanSeedRepository,
            CliSetupApplicationService cliSetupApplicationService,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase,
            System.Action showWindowOnVersionChange,
            System.Action showThirdPartyMigrationAutoScan)
        {
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            Debug.Assert(autoScanSeedRepository != null, "autoScanSeedRepository must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(
                thirdPartyToolMigrationUseCase != null,
                "thirdPartyToolMigrationUseCase must not be null");
            Debug.Assert(showWindowOnVersionChange != null, "showWindowOnVersionChange must not be null");
            Debug.Assert(showThirdPartyMigrationAutoScan != null, "showThirdPartyMigrationAutoScan must not be null");

            _editorSettingsPort = editorSettingsPort
                ?? throw new System.ArgumentNullException(nameof(editorSettingsPort));
            _sessionFlagsRepository = sessionFlagsRepository
                ?? throw new System.ArgumentNullException(nameof(sessionFlagsRepository));
            _autoScanSeedRepository = autoScanSeedRepository
                ?? throw new System.ArgumentNullException(nameof(autoScanSeedRepository));
            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new System.ArgumentNullException(nameof(cliSetupApplicationService));
            _skillSetupUseCase = skillSetupUseCase ?? throw new System.ArgumentNullException(nameof(skillSetupUseCase));
            _thirdPartyToolMigrationUseCase = thirdPartyToolMigrationUseCase
                ?? throw new System.ArgumentNullException(nameof(thirdPartyToolMigrationUseCase));
            _showWindowOnVersionChange = showWindowOnVersionChange
                ?? throw new System.ArgumentNullException(nameof(showWindowOnVersionChange));
            _showThirdPartyMigrationAutoScan = showThirdPartyMigrationAutoScan
                ?? throw new System.ArgumentNullException(nameof(showThirdPartyMigrationAutoScan));
        }

        internal static bool ShouldAutoShowForVersion(
            string currentVersion,
            string lastSeenVersion,
            string currentMinimumDispatcherVersion,
            string lastSeenMinimumDispatcherVersion,
            bool suppressAutoShow,
            bool needsCliUpdate,
            bool hasSkillUpdate)
        {
            bool versionChanged = !string.Equals(currentVersion, lastSeenVersion, System.StringComparison.Ordinal);
            bool minimumDispatcherVersionChanged = !string.Equals(
                currentMinimumDispatcherVersion,
                lastSeenMinimumDispatcherVersion,
                System.StringComparison.Ordinal);
            if (!versionChanged && !minimumDispatcherVersionChanged) return false;
            if (suppressAutoShow) return false;

            return string.IsNullOrEmpty(lastSeenVersion)
                || needsCliUpdate
                || (versionChanged && hasSkillUpdate);
        }

        internal static bool ShouldAutoScanThirdPartyToolMigration(string currentVersion, string lastSeenVersion)
        {
            if (!TryGetMajorVersion(currentVersion, out int currentMajorVersion))
            {
                return false;
            }

            if (string.IsNullOrEmpty(lastSeenVersion))
            {
                return currentMajorVersion == 3;
            }

            if (!TryGetMajorVersion(lastSeenVersion, out int lastSeenMajorVersion))
            {
                return false;
            }

            return lastSeenMajorVersion < 3 && currentMajorVersion == 3;
        }

        internal static void MaybeRecordLastSeenSetupWizardState(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            bool shouldRecordState,
            string version,
            string minimumDispatcherVersion)
        {
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");

            if (!shouldRecordState) return;

            Debug.Assert(!string.IsNullOrEmpty(version), "version must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(minimumDispatcherVersion),
                "minimumDispatcherVersion must not be null or empty");

            editorSettingsPort.UpdateSettings((UnityCliLoopEditorSettingsData settings) => settings with
            {
                lastSeenSetupWizardVersion = version,
                lastSeenSetupWizardMinimumDispatcherVersion = minimumDispatcherVersion
            });
        }

        internal static void MaybeRecordSuppressedSetupWizardState(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            bool suppressAutoShow,
            string version,
            string minimumDispatcherVersion)
        {
            if (!suppressAutoShow) return;

            MaybeRecordLastSeenSetupWizardState(editorSettingsPort, true, version, minimumDispatcherVersion);
        }

        internal static bool HasSkillUpdateForSetupWizard(IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets.Any(
                target => target.HasSkillsDirectory
                    && (target.InstallState == SkillInstallState.Outdated
                        || target.HasDifferentLayoutSkills));
        }

        internal void TryShowOnVersionChange()
        {
            EvaluateVersionChange(CancellationToken.None).Forget();
        }

        /// <summary>
        /// Pure decision function for the migration auto-scan poll loop. Unity does not reload the
        /// domain on a failed compile, and EditorApplication.delayCall is not reliably invoked while
        /// EditorUtility.scriptCompilationFailed/Console state is still settling right after startup,
        /// so the trigger must poll via EditorApplication.update instead of relying on a single
        /// one-shot delayCall.
        /// </summary>
        internal static MigrationAutoScanPollAction DecideMigrationAutoScanPollAction(
            bool isCompiling,
            bool scriptCompilationFailed,
            double elapsedSeconds,
            double timeoutSeconds)
        {
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be positive");

            if (isCompiling)
            {
                return MigrationAutoScanPollAction.ContinueWaiting;
            }

            if (!scriptCompilationFailed)
            {
                return MigrationAutoScanPollAction.Terminate;
            }

            if (elapsedSeconds >= timeoutSeconds)
            {
                return MigrationAutoScanPollAction.FallBackToFullScan;
            }

            return MigrationAutoScanPollAction.RunDetection;
        }

        private static bool TryGetMajorVersion(string version, out int majorVersion)
        {
            majorVersion = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            int separatorIndex = version.IndexOfAny(VersionMajorSeparators);
            string majorText = separatorIndex < 0 ? version : version.Substring(0, separatorIndex);
            return int.TryParse(majorText, out majorVersion);
        }

        private async Task EvaluateVersionChange(CancellationToken ct)
        {
            string currentVersion = UnityCliLoopConstants.PackageInfo.version;
            string currentMinimumDispatcherVersion = _cliSetupApplicationService.GetMinimumRequiredCliVersion();
            UnityCliLoopEditorSettingsData settings = _editorSettingsPort.GetSettings();
            bool suppressAutoShow = settings.suppressSetupWizardAutoShow;
            string lastSeenVersion = settings.lastSeenSetupWizardVersion ?? string.Empty;
            string lastSeenMinimumDispatcherVersion =
                settings.lastSeenSetupWizardMinimumDispatcherVersion ?? string.Empty;
            if (ct.IsCancellationRequested)
            {
                return;
            }

            bool shouldAutoScanThirdPartyToolMigration = ShouldAutoScanThirdPartyToolMigration(
                currentVersion,
                lastSeenVersion);
            MaybeScheduleThirdPartyToolMigrationAutoScan(shouldAutoScanThirdPartyToolMigration);

            bool versionChanged = !string.Equals(
                currentVersion,
                lastSeenVersion,
                System.StringComparison.Ordinal);
            bool minimumDispatcherVersionChanged = !string.Equals(
                currentMinimumDispatcherVersion,
                lastSeenMinimumDispatcherVersion,
                System.StringComparison.Ordinal);
            if (suppressAutoShow)
            {
                MaybeRecordSuppressedSetupWizardState(
                    _editorSettingsPort,
                    suppressAutoShow,
                    currentVersion,
                    currentMinimumDispatcherVersion);
                return;
            }

            if (!versionChanged && !minimumDispatcherVersionChanged)
            {
                return;
            }

            bool needsCliUpdate = false;
            bool hasSkillUpdate = false;
            if (!string.IsNullOrEmpty(lastSeenVersion))
            {
                needsCliUpdate = await NeedsCliUpdateForSetupWizardAsync(ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (versionChanged && !needsCliUpdate)
                {
                    hasSkillUpdate = await HasSkillUpdateForSetupWizardAsync(ct);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }

            bool shouldAutoShow = ShouldAutoShowForVersion(
                currentVersion,
                lastSeenVersion,
                currentMinimumDispatcherVersion,
                lastSeenMinimumDispatcherVersion,
                suppressAutoShow,
                needsCliUpdate,
                hasSkillUpdate);

            if (!shouldAutoShow)
            {
                MaybeRecordLastSeenSetupWizardState(
                    _editorSettingsPort,
                    true,
                    currentVersion,
                    currentMinimumDispatcherVersion);
                return;
            }

            EditorApplication.delayCall += () => _showWindowOnVersionChange();
        }

        private async Task<bool> NeedsCliUpdateForSetupWizardAsync(CancellationToken ct)
        {
            await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            if (string.IsNullOrEmpty(cliVersion))
            {
                return false;
            }

            CliSetupCompatibilityState state = EvaluateCliSetupCompatibility(
                cliVersion,
                cliIsDispatcher);
            return state.NeedsUpdate;
        }

        private CliSetupCompatibilityState EvaluateCliSetupCompatibility(
            string cliVersion,
            bool cliIsDispatcher)
        {
            string minimumRequiredCliVersion = _cliSetupApplicationService.GetMinimumRequiredCliVersion();
            return CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                minimumRequiredCliVersion);
        }

        private async Task<bool> HasSkillUpdateForSetupWizardAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets = await Task.Run(
                () => _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(
                    projectRoot,
                    !SetupWizardWindow.ForceFlatSkillInstall),
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            return HasSkillUpdateForSetupWizard(targets);
        }

        /// <summary>
        /// The version-gate check alone can no longer trigger the auto-scan window: it must also be
        /// confirmed against an actual compile-error hit for V2 legacy API tokens (mirroring Unity's own
        /// API Updater), so a V2-to-V3 upgrade with no legacy custom-tool code present never shows an
        /// unnecessary window.
        /// </summary>
        private void MaybeScheduleThirdPartyToolMigrationAutoScan(bool shouldAutoScanVersionGate)
        {
            if (!shouldAutoScanVersionGate)
            {
                return;
            }

            if (_migrationAutoScanPollingActive)
            {
                return;
            }

            _migrationAutoScanPollingActive = true;
            _migrationAutoScanPollStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += PollThirdPartyToolMigrationAutoScan;
        }

        private void PollThirdPartyToolMigrationAutoScan()
        {
            double elapsedSeconds = EditorApplication.timeSinceStartup - _migrationAutoScanPollStartTime;
            MigrationAutoScanPollAction action = DecideMigrationAutoScanPollAction(
                EditorApplication.isCompiling,
                EditorUtility.scriptCompilationFailed,
                elapsedSeconds,
                SetupWizardStartupFlowConstants.MigrationAutoScanPollTimeoutSeconds);

            switch (action)
            {
                case MigrationAutoScanPollAction.ContinueWaiting:
                    return;

                case MigrationAutoScanPollAction.Terminate:
                    StopThirdPartyToolMigrationAutoScanPolling();
                    return;

                case MigrationAutoScanPollAction.RunDetection:
                {
                    // Defaults to stopping so a thrown exception still unsubscribes this callback;
                    // otherwise a throwing detection would retry every editor update indefinitely.
                    // Only the still-searching (found == false) outcome keeps polling.
                    bool shouldStopPolling = true;
                    try
                    {
                        shouldStopPolling = TryRunThirdPartyToolMigrationAutoScanDetection();
                    }
                    finally
                    {
                        if (shouldStopPolling)
                        {
                            StopThirdPartyToolMigrationAutoScanPolling();
                        }
                    }

                    return;
                }

                case MigrationAutoScanPollAction.FallBackToFullScan:
                    StopThirdPartyToolMigrationAutoScanPolling();
                    ScheduleThirdPartyToolMigrationFallbackFullScan();
                    return;
            }
        }

        private void StopThirdPartyToolMigrationAutoScanPolling()
        {
            EditorApplication.update -= PollThirdPartyToolMigrationAutoScan;
            _migrationAutoScanPollingActive = false;
        }

        private bool TryRunThirdPartyToolMigrationAutoScanDetection()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            (bool found, List<string> targetFilePaths) =
                _thirdPartyToolMigrationUseCase.TryDetectAutoScanTargetsFromCompileErrors(projectRoot);
            if (!found)
            {
                return false;
            }

            _autoScanSeedRepository.StoreSeedFilePaths(targetFilePaths.ToArray());
            _sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(true);
            _showThirdPartyMigrationAutoScan();
            return true;
        }

        /// <summary>
        /// Reached only when scriptCompilationFailed stays true for the whole poll timeout without
        /// ever yielding parsed compile-error entries (an abnormal state); falls back to the
        /// pre-existing full-project scan rather than giving up detection entirely.
        /// </summary>
        private void ScheduleThirdPartyToolMigrationFallbackFullScan()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            RunThirdPartyToolMigrationFallbackFullScanAsync(projectRoot).Forget();
        }

        private async Task RunThirdPartyToolMigrationFallbackFullScanAsync(string projectRoot)
        {
            bool hasTargets = await _thirdPartyToolMigrationUseCase.HasMigrationTargetsAsync(
                projectRoot,
                CancellationToken.None);
            if (!hasTargets)
            {
                return;
            }

            _sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(true);
            _showThirdPartyMigrationAutoScan();
        }
    }
}
