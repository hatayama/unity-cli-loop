using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI setup application service behavior.
    /// </summary>
    public class CliSetupApplicationServiceTests
    {
        [Test]
        public async Task InstallGlobalCliAsync_UsesPinnedDispatcherReleaseTag()
        {
            // Verifies installation uses the immutable dispatcher release tag stamped in the package pin.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            await service.InstallGlobalCliAsync(
                RuntimePlatform.OSXEditor,
                new Progress<string>(),
                CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(ExpectedDispatcherReleaseTag()));
        }

        [Test]
        public void GetMinimumRequiredCliVersion_UsesDispatcherVersion()
        {
            // Verifies setup reads the minimum dispatcher version from the package pin JSON.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller(),
                new CliPinReaderService());

            Assert.That(service.GetMinimumRequiredCliVersion(), Is.EqualTo(ExpectedMinimumDispatcherVersion()));
        }

        [Test]
        public void GetCliInstallTargetVersion_UsesDispatcherReleaseTagVersion()
        {
            // Verifies the install target version shown in the update button comes from the pinned release tag.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller(),
                new StubBootstrapPinReader("dispatcher-v3.4.0", "3.0.0-beta.31"));

            Assert.That(service.GetCliInstallTargetVersion(), Is.EqualTo("3.4.0"));
        }

        [Test]
        public void GetCliInstallTargetVersion_WhenBootstrapPinIsUnavailableFallsBackToMinimumVersion()
        {
            // Verifies an unreadable bootstrap pin still renders a label by falling back to the minimum version.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller(),
                new FailingBootstrapPinReader());

            Assert.That(
                service.GetCliInstallTargetVersion(),
                Is.EqualTo(service.GetMinimumRequiredCliVersion()));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesPinnedDispatcherReleaseTag()
        {
            // Verifies fallback manual commands use the immutable dispatcher tag from the bootstrap pin.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            NativeCliInstallCommandLoadResult commandResult = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(commandResult.Success, Is.True, commandResult.ErrorOutput);
            Assert.That(
                commandResult.Command.ManualCommand,
                Is.EqualTo($"install {ExpectedDispatcherReleaseTag()}"));
        }

        [Test]
        public async Task InstallGlobalCliAsync_WhenBootstrapPinIsUnavailableFailsWithoutInvokingInstaller()
        {
            // Verifies a missing bootstrap pin cannot fall back to a derived or latest dispatcher release.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new FailingBootstrapPinReader());

            CliInstallResult result = await service.InstallGlobalCliAsync(
                RuntimePlatform.OSXEditor,
                new Progress<string>(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("bootstrap pin"));
            Assert.That(nativeCliInstaller.InstalledVersion, Is.Null);
        }

        private static string ExpectedMinimumDispatcherVersion()
        {
            CliPinLoadResult result = new CliPinReaderService().LoadPackagePin();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Pin.MinimumDispatcherVersion;
        }

        private static string ExpectedDispatcherReleaseTag()
        {
            DispatcherBootstrapPinLoadResult result = new CliPinReaderService().LoadDispatcherBootstrapPin();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.DispatcherReleaseTag;
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            private readonly string[] _versions;
            private int _versionIndex;

            public FakeCliInstallationDetector(string[] versions)
            {
                Debug.Assert(versions != null, "versions must not be null");
                Debug.Assert(versions.Length > 0, "versions must not be empty");

                _versions = versions;
            }
            public bool IsCliInstalled() => GetCachedCliVersion() != null;
            public string GetCachedCliVersion() => _versions[_versionIndex];
            public bool GetCachedCliIsDispatcher() => false;
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
                => Task.FromResult(true);

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                if (_versionIndex < _versions.Length - 1)
                {
                    _versionIndex++;
                }

                return Task.CompletedTask;
            }

            public void InvalidateCache() { }
        }

        private sealed class FailingBootstrapPinReader : ICliPinReader
        {
            public CliPinLoadResult LoadPackagePin()
            {
                return CliPinLoadResult.FromFailure("bootstrap pin missing");
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return DispatcherBootstrapPinLoadResult.FromFailure("bootstrap pin missing");
            }

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return "3.0.1";
            }
        }

        private sealed class StubBootstrapPinReader : ICliPinReader
        {
            private readonly string _dispatcherReleaseTag;
            private readonly string _minimumDispatcherVersion;

            public StubBootstrapPinReader(string dispatcherReleaseTag, string minimumDispatcherVersion)
            {
                _dispatcherReleaseTag = dispatcherReleaseTag;
                _minimumDispatcherVersion = minimumDispatcherVersion;
            }

            public CliPinLoadResult LoadPackagePin()
            {
                return CliPinLoadResult.FromFailure("not used by these tests");
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return DispatcherBootstrapPinLoadResult.FromSuccess(_dispatcherReleaseTag, "manifest");
            }

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return _minimumDispatcherVersion;
            }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            public string InstalledVersion { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return false;
            }

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath)
            {
                return ManagedCliKind.None;
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return false;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                IProgress<string> installProgress,
                CancellationToken ct)
            {
                InstalledVersion = dispatcherReleaseTag;
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliPathSetupPlan(
                    CliPathSetupShellKind.Zsh,
                    "zsh",
                    true,
                    "/Users/ExampleUser/.local/bin",
                    "$HOME/.local/bin",
                    "/Users/ExampleUser/.zshrc",
                    "export PATH=\"$HOME/.local/bin:$PATH\"",
                    "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> '/Users/ExampleUser/.zshrc'"));
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
            {
                return new CliPathSetupApplyResult(true, CliPathSetupApplyStatus.Applied, "");
            }

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers)
            {
                return NativeCliInstallCommandLoadResult.FromSuccess(
                    new NativeCliInstallCommand("sh", "-c true", $"install {dispatcherReleaseTag}"));
            }
        }

        /// <summary>
        /// Verifies cached detector state is returned unchanged by the service queries.
        /// </summary>
        [Test]
        public void CachedDetectorQueries_WhenDetectorHasState_ReturnDetectorValues()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            detector.CheckCompleted = true;
            detector.Installed = true;
            detector.CachedVersion = "9.8.7";
            detector.CachedIsDispatcher = true;
            detector.CachedExecutablePath = "<PROJECT_ROOT>/bin/uloop";
            CliSetupApplicationService service = CreateService(
                detector,
                new RecordingNativeCliInstaller(),
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Assert.That(service.IsCliCheckCompleted(), Is.True);
            Assert.That(service.IsCliInstalled(), Is.True);
            Assert.That(service.GetCachedCliVersion(), Is.EqualTo("9.8.7"));
            Assert.That(service.GetCachedCliIsDispatcher(), Is.True);
            Assert.That(service.GetCachedCliExecutablePath(), Is.EqualTo("<PROJECT_ROOT>/bin/uloop"));
        }

        /// <summary>
        /// Verifies the refresh operations return the detector's own tasks and pass the caller's token through.
        /// </summary>
        [Test]
        public void RefreshOperations_WhenCalled_ReturnDetectorTasksWithCallerToken()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            CliSetupApplicationService service = CreateService(
                detector,
                new RecordingNativeCliInstaller(),
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            Task refreshTask = service.RefreshCliVersionAsync(cancellationTokenSource.Token);
            Task forceRefreshTask = service.ForceRefreshCliVersionAsync(cancellationTokenSource.Token);

            Assert.That(refreshTask, Is.SameAs(detector.RefreshTask));
            Assert.That(forceRefreshTask, Is.SameAs(detector.ForceRefreshTask));
            Assert.That(detector.RefreshTokens, Is.EqualTo(new[] { cancellationTokenSource.Token }));
            Assert.That(detector.ForceRefreshTokens, Is.EqualTo(new[] { cancellationTokenSource.Token }));
            cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// Verifies shell visibility is answered by the detector for the requested platform.
        /// </summary>
        [Test]
        public void IsCliVisibleFromShellAsync_WhenDetectorReportsInvisible_ReturnsFalseForRequestedPlatform()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            detector.VisibilityResults.Enqueue(false);
            CliSetupApplicationService service = CreateService(
                detector,
                new RecordingNativeCliInstaller(),
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Task<bool> visibleTask = service.IsCliVisibleFromShellAsync(RuntimePlatform.LinuxEditor, CancellationToken.None);

            Assert.That(GetCompletedResult(visibleTask), Is.False);
            Assert.That(detector.VisibilityPlatforms, Is.EqualTo(new[] { RuntimePlatform.LinuxEditor }));
        }

        /// <summary>
        /// Verifies install-ownership queries return the installer's answers for the given path and platform.
        /// </summary>
        [Test]
        public void InstallerQueries_WhenInstallerOwnsInstall_ReturnInstallerAnswersForArguments()
        {
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            installer.OwnedPath = "<PROJECT_ROOT>/owned/uloop";
            installer.ManagedKind = ManagedCliKind.Homebrew;
            installer.OwnedInstallPlatform = RuntimePlatform.WindowsEditor;
            CliSetupApplicationService service = CreateService(
                new RecordingCliInstallationDetector(),
                installer,
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Assert.That(
                service.IsPackageOwnedCurrentUserInstallPath("<PROJECT_ROOT>/owned/uloop", RuntimePlatform.WindowsEditor),
                Is.True);
            Assert.That(
                service.IsPackageOwnedCurrentUserInstallPath("<PROJECT_ROOT>/other/uloop", RuntimePlatform.WindowsEditor),
                Is.False);
            Assert.That(service.ResolveManagedCliKind("<PROJECT_ROOT>/owned/uloop"), Is.EqualTo(ManagedCliKind.Homebrew));
            Assert.That(service.HasPackageOwnedCurrentUserInstall(RuntimePlatform.WindowsEditor), Is.True);
            Assert.That(service.HasPackageOwnedCurrentUserInstall(RuntimePlatform.OSXEditor), Is.False);
            Assert.That(installer.ResolvedKindPaths, Is.EqualTo(new[] { "<PROJECT_ROOT>/owned/uloop" }));
        }

        /// <summary>
        /// Verifies a bootstrap pin whose tag lacks the dispatcher prefix falls back to the minimum required version.
        /// </summary>
        [Test]
        public void GetCliInstallTargetVersion_WhenReleaseTagIsNotDispatcherTag_ReturnsMinimumVersion()
        {
            ScriptedPinReader pinReader = ScriptedPinReader.WithBootstrapPin("release-4.5.6");
            pinReader.MinimumDispatcherVersion = "3.0.1";
            CliSetupApplicationService service = CreateService(
                new RecordingCliInstallationDetector(),
                new RecordingNativeCliInstaller(),
                pinReader);

            string targetVersion = service.GetCliInstallTargetVersion();

            Assert.That(targetVersion, Is.EqualTo("3.0.1"));
        }

        /// <summary>
        /// Verifies PATH setup reports AppliedButStillMissing when the profile was written but the shell still cannot see the CLI.
        /// </summary>
        [Test]
        public void EnsureCliVisibleFromShellAsync_WhenStillInvisibleAfterApply_ReturnsAppliedButStillMissing()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            detector.VisibilityResults.Enqueue(false);
            detector.VisibilityResults.Enqueue(false);
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            installer.ApplyResult = new CliPathSetupApplyResult(true, CliPathSetupApplyStatus.Applied, "");
            CliSetupApplicationService service = CreateService(
                detector,
                installer,
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Task<CliPathSetupFlowResult> flowTask =
                service.EnsureCliVisibleFromShellAsync(RuntimePlatform.OSXEditor, CancellationToken.None);
            CliPathSetupFlowResult result = GetCompletedResult(flowTask);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.AppliedButStillMissing));
            Assert.That(detector.InvalidateCacheCount, Is.EqualTo(1));
            Assert.That(detector.VisibilityPlatforms.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a profile that was already configured and is visible after recheck reports AlreadyConfiguredAndVisible.
        /// </summary>
        [Test]
        public void EnsureCliVisibleFromShellAsync_WhenProfileAlreadyConfiguredAndVisible_ReturnsAlreadyConfiguredAndVisible()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            detector.VisibilityResults.Enqueue(false);
            detector.VisibilityResults.Enqueue(true);
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            installer.ApplyResult = new CliPathSetupApplyResult(true, CliPathSetupApplyStatus.AlreadyConfigured, "");
            CliSetupApplicationService service = CreateService(
                detector,
                installer,
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Task<CliPathSetupFlowResult> flowTask =
                service.EnsureCliVisibleFromShellAsync(RuntimePlatform.OSXEditor, CancellationToken.None);
            CliPathSetupFlowResult result = GetCompletedResult(flowTask);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.AlreadyConfiguredAndVisible));
        }

        /// <summary>
        /// Verifies uninstall returns the installer result for the requested platform and invalidates the detector cache once.
        /// </summary>
        [Test]
        public void UninstallGlobalCliAsync_WhenInstallerFails_ReturnsInstallerResultAndInvalidatesCache()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            installer.UninstallResult = new CliInstallResult(false, "uninstall blocked");
            CliSetupApplicationService service = CreateService(
                detector,
                installer,
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));

            Task<CliInstallResult> uninstallTask =
                service.UninstallGlobalCliAsync(RuntimePlatform.LinuxEditor, CancellationToken.None);
            CliInstallResult result = GetCompletedResult(uninstallTask);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Is.EqualTo("uninstall blocked"));
            Assert.That(installer.UninstallPlatforms, Is.EqualTo(new[] { RuntimePlatform.LinuxEditor }));
            Assert.That(detector.InvalidateCacheCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies uninstall rejects an already-cancelled token before touching the installer.
        /// </summary>
        [Test]
        public void UninstallGlobalCliAsync_WhenTokenAlreadyCancelled_DoesNotInvokeInstaller()
        {
            RecordingCliInstallationDetector detector = new RecordingCliInstallationDetector();
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            CliSetupApplicationService service = CreateService(
                detector,
                installer,
                ScriptedPinReader.WithBootstrapPin("dispatcher-v1.0.0"));
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Task<CliInstallResult> uninstallTask =
                service.UninstallGlobalCliAsync(RuntimePlatform.LinuxEditor, cancellationTokenSource.Token);

            Assert.That(uninstallTask.IsCanceled, Is.True);
            Assert.That(installer.UninstallPlatforms, Is.Empty);
            Assert.That(detector.InvalidateCacheCount, Is.EqualTo(0));
            cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// Verifies an unreadable bootstrap pin makes the manual install command fail with the pin error and skips the installer.
        /// </summary>
        [Test]
        public void GetGlobalCliInstallCommand_WhenBootstrapPinFails_ReturnsPinErrorWithoutCallingInstaller()
        {
            RecordingNativeCliInstaller installer = new RecordingNativeCliInstaller();
            CliSetupApplicationService service = CreateService(
                new RecordingCliInstallationDetector(),
                installer,
                ScriptedPinReader.WithBootstrapFailure("bootstrap pin unreadable"));

            NativeCliInstallCommandLoadResult result = service.GetGlobalCliInstallCommand(RuntimePlatform.OSXEditor, true);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Is.EqualTo("bootstrap pin unreadable"));
            Assert.That(installer.InstallCommandRequestCount, Is.EqualTo(0));
        }

        private static CliSetupApplicationService CreateService(
            RecordingCliInstallationDetector detector,
            RecordingNativeCliInstaller installer,
            ScriptedPinReader pinReader)
        {
            return new CliSetupApplicationService(detector, installer, pinReader);
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True, "The service call must complete synchronously with completed fakes.");
            return task.GetAwaiter().GetResult();
        }

        private static CliPathSetupPlan CreateAutomaticPlan()
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "<HOME>/.local/bin",
                "$HOME/.local/bin",
                "<HOME>/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "echo setup");
        }

        /// <summary>
        /// Test support type that records detector calls and returns scripted values.
        /// </summary>
        private sealed class RecordingCliInstallationDetector : ICliInstallationDetector
        {
            public bool CheckCompleted { get; set; }
            public bool Installed { get; set; }
            public string CachedVersion { get; set; }
            public bool CachedIsDispatcher { get; set; }
            public string CachedExecutablePath { get; set; }
            public Task RefreshTask { get; } = Task.FromResult(1);
            public Task ForceRefreshTask { get; } = Task.FromResult(2);
            public List<CancellationToken> RefreshTokens { get; } = new List<CancellationToken>();
            public List<CancellationToken> ForceRefreshTokens { get; } = new List<CancellationToken>();
            public Queue<bool> VisibilityResults { get; } = new Queue<bool>();
            public List<RuntimePlatform> VisibilityPlatforms { get; } = new List<RuntimePlatform>();
            public int InvalidateCacheCount { get; private set; }

            public bool IsCliInstalled() => Installed;
            public string GetCachedCliVersion() => CachedVersion;
            public bool GetCachedCliIsDispatcher() => CachedIsDispatcher;
            public string GetCachedCliExecutablePath() => CachedExecutablePath;
            public bool IsCheckCompleted() => CheckCompleted;

            public Task RefreshCliVersionAsync(CancellationToken ct)
            {
                RefreshTokens.Add(ct);
                return RefreshTask;
            }

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                ForceRefreshTokens.Add(ct);
                return ForceRefreshTask;
            }

            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
            {
                VisibilityPlatforms.Add(platform);
                return Task.FromResult(VisibilityResults.Dequeue());
            }

            public void InvalidateCache()
            {
                InvalidateCacheCount++;
            }
        }

        /// <summary>
        /// Test support type that records installer calls and returns scripted results.
        /// </summary>
        private sealed class RecordingNativeCliInstaller : INativeCliInstaller
        {
            public string OwnedPath { get; set; }
            public ManagedCliKind ManagedKind { get; set; }
            public RuntimePlatform OwnedInstallPlatform { get; set; }
            public CliPathSetupApplyResult ApplyResult { get; set; }
            public CliInstallResult UninstallResult { get; set; }
            public List<string> ResolvedKindPaths { get; } = new List<string>();
            public List<RuntimePlatform> UninstallPlatforms { get; } = new List<RuntimePlatform>();
            public int InstallCommandRequestCount { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return platform == OwnedInstallPlatform && cliExecutablePath == OwnedPath;
            }

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath)
            {
                ResolvedKindPaths.Add(cliExecutablePath);
                return ManagedKind;
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return platform == OwnedInstallPlatform;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                IProgress<string> installProgress,
                CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                UninstallPlatforms.Add(platform);
                return Task.FromResult(UninstallResult);
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(CreateAutomaticPlan());
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
            {
                return ApplyResult;
            }

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers)
            {
                InstallCommandRequestCount++;
                return NativeCliInstallCommandLoadResult.FromSuccess(
                    new NativeCliInstallCommand("sh", "-c true", "install"));
            }
        }

        /// <summary>
        /// Test support type that returns a scripted bootstrap pin and minimum version.
        /// </summary>
        private sealed class ScriptedPinReader : ICliPinReader
        {
            private readonly DispatcherBootstrapPinLoadResult _bootstrapPin;

            private ScriptedPinReader(DispatcherBootstrapPinLoadResult bootstrapPin)
            {
                _bootstrapPin = bootstrapPin;
            }

            public string MinimumDispatcherVersion { get; set; } = "1.0.0";

            public static ScriptedPinReader WithBootstrapPin(string dispatcherReleaseTag)
            {
                return new ScriptedPinReader(DispatcherBootstrapPinLoadResult.FromSuccess(dispatcherReleaseTag, "manifest"));
            }

            public static ScriptedPinReader WithBootstrapFailure(string errorMessage)
            {
                return new ScriptedPinReader(DispatcherBootstrapPinLoadResult.FromFailure(errorMessage));
            }

            public CliPinLoadResult LoadPackagePin()
            {
                return CliPinLoadResult.FromFailure("not used by these tests");
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return _bootstrapPin;
            }

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return MinimumDispatcherVersion;
            }
        }
    }
}
