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
        string GetCachedCliExecutablePath();
        bool IsCheckCompleted();
        Task RefreshCliVersionAsync(CancellationToken ct);
        Task ForceRefreshCliVersionAsync(CancellationToken ct);
        void InvalidateCache();
    }

    /// <summary>
    /// Defines the project-local CLI installation operations required by CLI setup.
    /// </summary>
    public interface IProjectLocalCliInstaller
    {
        string DetectBundledRequiredDispatcherVersion();
        CliInstallResult EnsureProjectLocalCliCurrent(string projectRoot, string packageVersion);
    }

    /// <summary>
    /// Defines the native CLI installation operations required by CLI setup.
    /// </summary>
    public interface INativeCliInstaller
    {
        bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform);
        Task<CliInstallResult> InstallGlobalCliAsync(RuntimePlatform platform, string packageVersion, CancellationToken ct);
        Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct);
        NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string packageVersion,
            bool removeLegacyLaunchers);
    }

    /// <summary>
    /// Coordinates CLI setup workflows for editor UI without knowing how the CLI is detected or installed.
    /// </summary>
    public sealed class CliSetupApplicationService
    {
        private readonly ICliInstallationDetector _cliInstallationDetector;
        private readonly IProjectLocalCliInstaller _projectLocalCliInstaller;
        private readonly INativeCliInstaller _nativeCliInstaller;

        public CliSetupApplicationService(
            ICliInstallationDetector cliInstallationDetector,
            IProjectLocalCliInstaller projectLocalCliInstaller,
            INativeCliInstaller nativeCliInstaller)
        {
            Debug.Assert(cliInstallationDetector != null, "cliInstallationDetector must not be null");
            Debug.Assert(projectLocalCliInstaller != null, "projectLocalCliInstaller must not be null");
            Debug.Assert(nativeCliInstaller != null, "nativeCliInstaller must not be null");

            _cliInstallationDetector = cliInstallationDetector;
            _projectLocalCliInstaller = projectLocalCliInstaller;
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

        public void InvalidateCliCache()
        {
            _cliInstallationDetector.InvalidateCache();
        }

        public string GetRequiredDispatcherVersion(string packageVersion)
        {
            Debug.Assert(!string.IsNullOrEmpty(packageVersion), "packageVersion must not be null or empty");

            string requiredDispatcherVersion = _projectLocalCliInstaller.DetectBundledRequiredDispatcherVersion();
            return string.IsNullOrEmpty(requiredDispatcherVersion)
                ? packageVersion
                : requiredDispatcherVersion;
        }

        public CliInstallResult EnsureProjectLocalCliCurrent(string projectRoot, string packageVersion)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(packageVersion), "packageVersion must not be null or empty");

            return _projectLocalCliInstaller.EnsureProjectLocalCliCurrent(projectRoot, packageVersion);
        }

        public bool IsPackageOwnedCurrentUserInstallPath(
            string cliExecutablePath,
            RuntimePlatform platform)
        {
            return _nativeCliInstaller.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool IsCliVersionLessThan(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionLessThan(leftVersion, rightVersion);
        }

        public bool IsCliVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return CliVersionComparer.IsVersionGreaterThanOrEqual(leftVersion, rightVersion);
        }

        public async Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string packageVersion,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(packageVersion), "packageVersion must not be null or empty");
            ct.ThrowIfCancellationRequested();

            CliInstallResult result = await _nativeCliInstaller.InstallGlobalCliAsync(platform, packageVersion, ct);
            _cliInstallationDetector.InvalidateCache();
            return result;
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
            string packageVersion,
            bool removeLegacyLaunchers)
        {
            Debug.Assert(!string.IsNullOrEmpty(packageVersion), "packageVersion must not be null or empty");

            return _nativeCliInstaller.GetGlobalCliInstallCommand(platform, packageVersion, removeLegacyLaunchers);
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

        public static void InvalidateCliCache()
        {
            GetService().InvalidateCliCache();
        }

        public static string GetRequiredDispatcherVersion(string packageVersion)
        {
            return GetService().GetRequiredDispatcherVersion(packageVersion);
        }

        public static CliInstallResult EnsureProjectLocalCliCurrent(string projectRoot, string packageVersion)
        {
            return GetService().EnsureProjectLocalCliCurrent(projectRoot, packageVersion);
        }

        public static bool IsPackageOwnedCurrentUserInstallPath(
            string cliExecutablePath,
            RuntimePlatform platform)
        {
            return GetService().IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public static bool IsCliVersionLessThan(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionLessThan(leftVersion, rightVersion);
        }

        public static bool IsCliVersionGreaterThanOrEqual(string leftVersion, string rightVersion)
        {
            return GetService().IsCliVersionGreaterThanOrEqual(leftVersion, rightVersion);
        }

        public static Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string packageVersion,
            CancellationToken ct)
        {
            return GetService().InstallGlobalCliAsync(platform, packageVersion, ct);
        }

        public static Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            return GetService().UninstallGlobalCliAsync(platform, ct);
        }

        public static NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string packageVersion,
            bool removeLegacyLaunchers)
        {
            return GetService().GetGlobalCliInstallCommand(platform, packageVersion, removeLegacyLaunchers);
        }
    }
}
