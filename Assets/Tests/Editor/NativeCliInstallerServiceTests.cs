using System;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the delegation and cancellation guards of the native CLI installer service.
    /// </summary>
    public sealed class NativeCliInstallerServiceTests
    {
        private const string ArchiveManifest =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\n"
            + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1";

        /// <summary>
        /// Verifies that a blank executable path is never reported as the package-owned install.
        /// </summary>
        [Test]
        public void IsPackageOwnedCurrentUserInstallPath_WhenPathIsBlank_ReturnsFalse()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();

            bool result = service.IsPackageOwnedCurrentUserInstallPath("  ", RuntimePlatform.OSXEditor);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a winget link path is reported as winget-managed.
        /// </summary>
        [Test]
        public void ResolveManagedCliKind_WhenPathIsWingetLink_ReturnsWinget()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();

            ManagedCliKind result = service.ResolveManagedCliKind(
                @"C:\Users\<USER_NAME>\AppData\Local\Microsoft\WinGet\Links\uloop.exe");

            Assert.That(result, Is.EqualTo(ManagedCliKind.Winget));
        }

        /// <summary>
        /// Verifies that an already-canceled install request throws synchronously before any installer work starts.
        /// </summary>
        [Test]
        public void InstallGlobalCliAsync_WhenTokenIsCanceled_ThrowsSynchronously()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();

            Assert.Throws<OperationCanceledException>(
                () => service.InstallGlobalCliAsync(
                    RuntimePlatform.OSXEditor,
                    "dispatcher-v3.0.1",
                    ArchiveManifest,
                    new Progress<string>(),
                    new CancellationToken(true)));
        }

        /// <summary>
        /// Verifies that an already-canceled uninstall request throws synchronously before any uninstall work starts.
        /// </summary>
        [Test]
        public void UninstallGlobalCliAsync_WhenTokenIsCanceled_ThrowsSynchronously()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();

            Assert.Throws<OperationCanceledException>(
                () => service.UninstallGlobalCliAsync(RuntimePlatform.OSXEditor, new CancellationToken(true)));
        }

        /// <summary>
        /// Verifies that an already-canceled PATH setup plan request throws synchronously before resolving the user's shell.
        /// </summary>
        [Test]
        public void GetGlobalCliPathSetupPlanAsync_WhenTokenIsCanceled_ThrowsSynchronously()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();

            Assert.Throws<OperationCanceledException>(
                () => service.GetGlobalCliPathSetupPlanAsync(RuntimePlatform.OSXEditor, new CancellationToken(true)));
        }

        /// <summary>
        /// Verifies that applying an unsupported plan reports the writer's unsupported result without touching the file system.
        /// </summary>
        [Test]
        public void ApplyGlobalCliPathSetup_WhenPlanIsUnsupported_ReturnsUnsupportedResult()
        {
            NativeCliInstallerService service = new NativeCliInstallerService();
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Unsupported,
                "tcsh",
                false,
                "<HOME>/.local/bin",
                "$HOME/.local/bin",
                "",
                "",
                "");

            CliPathSetupApplyResult result = service.ApplyGlobalCliPathSetup(plan);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Unsupported));
        }
    }
}
