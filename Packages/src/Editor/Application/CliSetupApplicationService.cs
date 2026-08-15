using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    public readonly struct NativeCliInstallCommand
    {
        public NativeCliInstallCommand(string fileName, string arguments, string manualCommand)
        {
            Debug.Assert(!string.IsNullOrEmpty(fileName), "fileName must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(arguments), "arguments must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(manualCommand), "manualCommand must not be null or empty");

            FileName = fileName;
            Arguments = arguments;
            ManualCommand = manualCommand;
        }

        public string FileName { get; }
        public string Arguments { get; }
        public string ManualCommand { get; }
    }

    /// <summary>
    /// Represents either a safe native installer command or the reason it could not be built.
    /// </summary>
    public readonly struct NativeCliInstallCommandLoadResult
    {
        private NativeCliInstallCommandLoadResult(bool success, NativeCliInstallCommand command, string errorOutput)
        {
            Success = success;
            Command = command;
            ErrorOutput = errorOutput;
        }

        public bool Success { get; }
        public NativeCliInstallCommand Command { get; }
        public string ErrorOutput { get; }

        public static NativeCliInstallCommandLoadResult FromSuccess(NativeCliInstallCommand command)
        {
            return new NativeCliInstallCommandLoadResult(true, command, string.Empty);
        }

        public static NativeCliInstallCommandLoadResult FromFailure(string errorOutput)
        {
            return new NativeCliInstallCommandLoadResult(false, default, errorOutput);
        }
    }

    /// <summary>
    /// Defines how CLI installation state is detected by the owning workflow.
    /// </summary>
    public interface ICliInstallationDetector
    {
        bool IsCliInstalled();
        string GetCachedCliVersion();
        bool GetCachedCliIsDispatcher();
        string GetCachedCliExecutablePath();
        bool IsCheckCompleted();
        Task RefreshCliVersionAsync(CancellationToken ct);
        Task ForceRefreshCliVersionAsync(CancellationToken ct);
        Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct);
        void InvalidateCache();
    }

    /// <summary>
    /// Defines the native CLI installation operations required by CLI setup.
    /// </summary>
    public interface INativeCliInstaller
    {
        bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform);
        bool IsHomebrewManagedInstallPath(string cliExecutablePath);
        bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform);
        Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            IProgress<string> installProgress,
            CancellationToken ct);
        Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct);
        Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct);
        CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan);
        NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            bool removeLegacyLaunchers);
    }

    /// <summary>
    /// Coordinates CLI setup workflows for editor UI without knowing how the CLI is detected or installed.
    /// </summary>
    public sealed class CliSetupApplicationService
    {
        private readonly ICliInstallationDetector _cliInstallationDetector;
        private readonly INativeCliInstaller _nativeCliInstaller;
        private readonly ICliPinReader _cliPinReader;

        public CliSetupApplicationService(
            ICliInstallationDetector cliInstallationDetector,
            INativeCliInstaller nativeCliInstaller,
            ICliPinReader cliPinReader)
        {
            Debug.Assert(cliInstallationDetector != null, "cliInstallationDetector must not be null");
            Debug.Assert(nativeCliInstaller != null, "nativeCliInstaller must not be null");
            Debug.Assert(cliPinReader != null, "cliPinReader must not be null");

            _cliInstallationDetector = cliInstallationDetector ?? throw new ArgumentNullException(nameof(cliInstallationDetector));
            _nativeCliInstaller = nativeCliInstaller ?? throw new ArgumentNullException(nameof(nativeCliInstaller));
            _cliPinReader = cliPinReader ?? throw new ArgumentNullException(nameof(cliPinReader));
        }

        public bool IsCliCheckCompleted()
        {
            return _cliInstallationDetector.IsCheckCompleted();
        }

        public bool IsCliInstalled()
        {
            return _cliInstallationDetector.IsCliInstalled();
        }

        public string GetCachedCliVersion()
        {
            return _cliInstallationDetector.GetCachedCliVersion();
        }

        public bool GetCachedCliIsDispatcher()
        {
            return _cliInstallationDetector.GetCachedCliIsDispatcher();
        }

        public string GetCachedCliExecutablePath()
        {
            return _cliInstallationDetector.GetCachedCliExecutablePath();
        }

        public Task RefreshCliVersionAsync(CancellationToken ct)
        {
            return _cliInstallationDetector.RefreshCliVersionAsync(ct);
        }

        public Task ForceRefreshCliVersionAsync(CancellationToken ct)
        {
            return _cliInstallationDetector.ForceRefreshCliVersionAsync(ct);
        }

        public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
        {
            return _cliInstallationDetector.IsCliVisibleFromShellAsync(platform, ct);
        }

        public string GetMinimumRequiredCliVersion()
        {
            // Why: v3 setup installs the global dispatcher and reads the minimum from the package pin JSON
            // so that the single source of truth stays consistent with the dispatcher installation flow.
            return _cliPinReader.LoadMinimumDispatcherVersionOrThrow();
        }

        public bool IsPackageOwnedCurrentUserInstallPath(
            string cliExecutablePath,
            RuntimePlatform platform)
        {
            return _nativeCliInstaller.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool IsHomebrewManagedInstallPath(string cliExecutablePath)
        {
            return _nativeCliInstaller.IsHomebrewManagedInstallPath(cliExecutablePath);
        }

        public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return _nativeCliInstaller.HasPackageOwnedCurrentUserInstall(platform);
        }

        public async Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            IProgress<string> installProgress,
            CancellationToken ct)
        {
            Debug.Assert(installProgress != null, "installProgress must not be null");
            ct.ThrowIfCancellationRequested();

            DispatcherBootstrapPinLoadResult bootstrapPin = _cliPinReader.LoadDispatcherBootstrapPin();
            if (!bootstrapPin.Success)
            {
                return new CliInstallResult(false, bootstrapPin.ErrorMessage);
            }

            CliInstallResult result = await _nativeCliInstaller.InstallGlobalCliAsync(
                platform,
                bootstrapPin.DispatcherReleaseTag,
                bootstrapPin.ArchiveManifest,
                installProgress,
                ct);
            _cliInstallationDetector.InvalidateCache();
            return result;
        }

        public async Task<CliPathSetupFlowResult> EnsureCliVisibleFromShellAsync(
            RuntimePlatform platform,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool isVisible = await _cliInstallationDetector.IsCliVisibleFromShellAsync(platform, ct);
            if (isVisible)
            {
                return new CliPathSetupFlowResult(
                    CliPathSetupFlowStatus.AlreadyVisible,
                    default,
                    "");
            }

            CliPathSetupPlan plan = await _nativeCliInstaller.GetGlobalCliPathSetupPlanAsync(platform, ct);
            if (!plan.CanApplyAutomatically)
            {
                return new CliPathSetupFlowResult(
                    CliPathSetupFlowStatus.ManualSetupRequired,
                    plan,
                    "");
            }

            CliPathSetupApplyResult applyResult = _nativeCliInstaller.ApplyGlobalCliPathSetup(plan);
            if (!applyResult.Success)
            {
                return new CliPathSetupFlowResult(
                    CliPathSetupFlowStatus.Failed,
                    plan,
                    applyResult.ErrorOutput);
            }

            _cliInstallationDetector.InvalidateCache();
            bool isVisibleAfterApply = await _cliInstallationDetector.IsCliVisibleFromShellAsync(platform, ct);
            if (!isVisibleAfterApply)
            {
                return new CliPathSetupFlowResult(
                    CliPathSetupFlowStatus.AppliedButStillMissing,
                    plan,
                    "");
            }

            CliPathSetupFlowStatus status = applyResult.Status == CliPathSetupApplyStatus.AlreadyConfigured
                ? CliPathSetupFlowStatus.AlreadyConfiguredAndVisible
                : CliPathSetupFlowStatus.AppliedAndVisible;
            return new CliPathSetupFlowResult(status, plan, "");
        }

        public async Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            CliInstallResult result = await _nativeCliInstaller.UninstallGlobalCliAsync(platform, ct);
            _cliInstallationDetector.InvalidateCache();
            return result;
        }

        public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            bool removeLegacyLaunchers)
        {
            DispatcherBootstrapPinLoadResult bootstrapPin = _cliPinReader.LoadDispatcherBootstrapPin();
            if (!bootstrapPin.Success)
            {
                return NativeCliInstallCommandLoadResult.FromFailure(bootstrapPin.ErrorMessage);
            }

            return _nativeCliInstaller.GetGlobalCliInstallCommand(
                platform,
                bootstrapPin.DispatcherReleaseTag,
                bootstrapPin.ArchiveManifest,
                removeLegacyLaunchers);
        }
    }

}
