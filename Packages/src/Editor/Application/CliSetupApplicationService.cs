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
        bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform);
        Task<CliInstallResult> InstallGlobalCliAsync(RuntimePlatform platform, string cliReleaseTag, CancellationToken ct);
        Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct);
        Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct);
        CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan);
        NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers);
    }

    /// <summary>
    /// Coordinates CLI setup workflows for editor UI without knowing how the CLI is detected or installed.
    /// </summary>
    public sealed class CliSetupApplicationService
    {
        private readonly ICliInstallationDetector _cliInstallationDetector;
        private readonly INativeCliInstaller _nativeCliInstaller;

        public CliSetupApplicationService(
            ICliInstallationDetector cliInstallationDetector,
            INativeCliInstaller nativeCliInstaller)
        {
            Debug.Assert(cliInstallationDetector != null, "cliInstallationDetector must not be null");
            Debug.Assert(nativeCliInstaller != null, "nativeCliInstaller must not be null");

            _cliInstallationDetector = cliInstallationDetector;
            _nativeCliInstaller = nativeCliInstaller;
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

        public void InvalidateCliCache()
        {
            _cliInstallationDetector.InvalidateCache();
        }

        public string GetMinimumRequiredCliVersion()
        {
            // Why: v3 setup installs the global dispatcher; project-local CLI versions are pinned separately.
            return CliConstants.MINIMUM_REQUIRED_DISPATCHER_VERSION;
        }

        public string GetMinimumRequiredCliReleaseTag()
        {
            // Why: v3 setup installs the global dispatcher; project-local CLI versions are pinned separately.
            return CliConstants.MINIMUM_REQUIRED_DISPATCHER_RELEASE_TAG;
        }

        public bool IsPackageOwnedCurrentUserInstallPath(
            string cliExecutablePath,
            RuntimePlatform platform)
        {
            return _nativeCliInstaller.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return _nativeCliInstaller.HasPackageOwnedCurrentUserInstall(platform);
        }

        public bool IsCliVersionLessThan(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionLessThan(leftVersion, rightVersion);
        }

        public bool IsCliVersionGreaterThan(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionGreaterThan(leftVersion, rightVersion);
        }

        public bool IsCliVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionGreaterThanOrEqual(leftVersion, rightVersion);
        }

        public bool IsCliVersionEqual(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionEqual(leftVersion, rightVersion);
        }

        public async Task<CliInstallResult> InstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            CliInstallResult result = await _nativeCliInstaller.InstallGlobalCliAsync(
                platform,
                GetMinimumRequiredCliReleaseTag(),
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

        public NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            bool removeLegacyLaunchers)
        {
            return _nativeCliInstaller.GetGlobalCliInstallCommand(
                platform,
                GetMinimumRequiredCliReleaseTag(),
                removeLegacyLaunchers);
        }
    }

    /// <summary>
    /// Compatibility facade for editor setup UI workflows.
    /// </summary>
    public static class CliSetupApplicationFacade
    {
        private static CliSetupApplicationService ServiceValue;

        internal static void RegisterService(CliSetupApplicationService service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        private static CliSetupApplicationService GetService()
        {
            if (ServiceValue == null)
            {
                throw new InvalidOperationException("Unity CLI Loop CLI setup service is not registered.");
            }

            return ServiceValue;
        }

        public static bool IsCliCheckCompleted()
        {
            return GetService().IsCliCheckCompleted();
        }

        public static bool IsCliInstalled()
        {
            return GetService().IsCliInstalled();
        }

        public static string GetCachedCliVersion()
        {
            return GetService().GetCachedCliVersion();
        }

        public static bool GetCachedCliIsDispatcher()
        {
            return GetService().GetCachedCliIsDispatcher();
        }

        public static string GetCachedCliExecutablePath()
        {
            return GetService().GetCachedCliExecutablePath();
        }

        public static Task RefreshCliVersionAsync(CancellationToken ct)
        {
            return GetService().RefreshCliVersionAsync(ct);
        }

        public static Task ForceRefreshCliVersionAsync(CancellationToken ct)
        {
            return GetService().ForceRefreshCliVersionAsync(ct);
        }

        public static Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
        {
            return GetService().IsCliVisibleFromShellAsync(platform, ct);
        }

        public static void InvalidateCliCache()
        {
            GetService().InvalidateCliCache();
        }

        public static string GetMinimumRequiredCliVersion()
        {
            return GetService().GetMinimumRequiredCliVersion();
        }

        public static string GetMinimumRequiredCliReleaseTag()
        {
            return GetService().GetMinimumRequiredCliReleaseTag();
        }

        public static bool IsPackageOwnedCurrentUserInstallPath(
            string cliExecutablePath,
            RuntimePlatform platform)
        {
            return GetService().IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public static bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return GetService().HasPackageOwnedCurrentUserInstall(platform);
        }

        public static bool IsCliVersionLessThan(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionLessThan(leftVersion, rightVersion);
        }

        public static bool IsCliVersionGreaterThan(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionGreaterThan(leftVersion, rightVersion);
        }

        public static bool IsCliVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionGreaterThanOrEqual(leftVersion, rightVersion);
        }

        public static bool IsCliVersionEqual(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionEqual(leftVersion, rightVersion);
        }

        public static Task<CliInstallResult> InstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            return GetService().InstallGlobalCliAsync(platform, ct);
        }

        public static Task<CliPathSetupFlowResult> EnsureCliVisibleFromShellAsync(
            RuntimePlatform platform,
            CancellationToken ct)
        {
            return GetService().EnsureCliVisibleFromShellAsync(platform, ct);
        }

        public static Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            return GetService().UninstallGlobalCliAsync(platform, ct);
        }

        public static NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            bool removeLegacyLaunchers)
        {
            return GetService().GetGlobalCliInstallCommand(platform, removeLegacyLaunchers);
        }
    }
}
