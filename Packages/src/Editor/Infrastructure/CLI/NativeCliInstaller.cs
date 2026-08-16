using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Installs the package-owned global CLI through the same installer scripts used by CLI commands.
    /// </summary>
    public static class NativeCliInstaller
    {
        private const int INSTALL_PROCESS_TIMEOUT_MS = 300000;

        public static NativeCliInstallCommand GetInstallCommand(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            bool removeLegacyLaunchers)
        {
            return NativeCliCommandBuilder.BuildInstallCommandWithPackagePath(
                platform,
                dispatcherReleaseTag,
                dispatcherArchiveManifest,
                removeLegacyLaunchers,
                NodeEnvironmentResolver.GetUserShell(),
                UnityCliLoopConstants.PackageResolvedPath);
        }

        public static async Task<CliInstallResult> InstallAsync(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            IProgress<string> installProgress,
            CancellationToken ct)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(dispatcherReleaseTag), "dispatcherReleaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(dispatcherArchiveManifest), "dispatcherArchiveManifest must not be null or empty");
            UnityEngine.Debug.Assert(installProgress != null, "installProgress must not be null");
            ct.ThrowIfCancellationRequested();

            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = GetInstallCommand(
                platform,
                dispatcherReleaseTag,
                dispatcherArchiveManifest,
                true);
            CliInstallResult result = await Task.Run(
                () => NativeCliSetupCommandRunner.RunInstallCommand(
                    command,
                    ct,
                    INSTALL_PROCESS_TIMEOUT_MS,
                    line => installProgress.Report(line)),
                ct);

            if (result.Success)
            {
                result = FinishSuccessfulInstall(
                    result,
                    installDirectory,
                    platform,
                    ApplyInstallDirectoryToCurrentProcessPath);
            }

            return result;
        }

        public static async Task<CliInstallResult> UninstallAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildUninstallCommand(
                installDirectory,
                platform);
            CliInstallResult result = await Task.Run(
                () => NativeCliSetupCommandRunner.RunUninstallCommand(
                    command,
                    installDirectory,
                    ct,
                    INSTALL_PROCESS_TIMEOUT_MS),
                ct);
            if (!result.Success)
            {
                return result;
            }

            string installPath = NativeCliInstallPathResolver.GetGlobalCliInstallPath(installDirectory, platform);
            CliInstallResult removalResult = await NativeCliUninstallCompletionWaiter.WaitForUninstallCompletionAsync(
                installPath,
                ct,
                NativeCliUninstallCompletionWaiter.UNINSTALL_COMPLETION_TIMEOUT_MS,
                NativeCliSetupCommandRunner.INSTALL_PROCESS_WAIT_SLICE_MS,
                File.Exists,
                Task.Delay);
            if (!removalResult.Success)
            {
                return removalResult;
            }

            RemoveInstallDirectoryFromCurrentProcessPath(platform);
            return result;
        }

        internal static CliInstallResult FinishSuccessfulInstall(
            CliInstallResult installResult,
            string installDirectory,
            RuntimePlatform platform,
            Action<RuntimePlatform> applyInstallDirectoryToCurrentProcessPath)
        {
            UnityEngine.Debug.Assert(installResult.Success, "installResult must be successful");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(applyInstallDirectoryToCurrentProcessPath != null, "applyInstallDirectoryToCurrentProcessPath must not be null");

            applyInstallDirectoryToCurrentProcessPath(platform);
            return installResult;
        }

        private static void ApplyInstallDirectoryToCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = NativeCliInstallPathResolver.GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                currentPath,
                installDirectory,
                platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        private static void RemoveInstallDirectoryFromCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = NativeCliInstallPathResolver.GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = NativeCliInstallPathResolver.BuildPathWithoutInstallDirectory(
                currentPath,
                installDirectory,
                platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

    }

    /// <summary>
    /// Provides Native CLI Installer operations for its owning module.
    /// </summary>
    public sealed class NativeCliInstallerService : INativeCliInstaller
    {
        public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
        {
            return NativeCliInstallPathResolver.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool IsHomebrewManagedInstallPath(string cliExecutablePath)
        {
            return NativeCliInstallPathResolver.IsHomebrewManagedInstallPath(cliExecutablePath);
        }

        public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return NativeCliInstallPathResolver.HasPackageOwnedCurrentUserInstall(platform);
        }

        public Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            IProgress<string> installProgress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.InstallAsync(
                platform,
                dispatcherReleaseTag,
                dispatcherArchiveManifest,
                installProgress,
                ct);
        }

        public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.UninstallAsync(platform, ct);
        }

        public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CliPathSetupProfileResolver.ResolveCurrentUserPlan(platform));
        }

        public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
        {
            return CliPathSetupWriter.ApplyToFileSystem(plan);
        }

        public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            bool removeLegacyLaunchers)
        {
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                platform,
                dispatcherReleaseTag,
                dispatcherArchiveManifest,
                removeLegacyLaunchers);
            return NativeCliInstallCommandLoadResult.FromSuccess(command);
        }
    }
}
