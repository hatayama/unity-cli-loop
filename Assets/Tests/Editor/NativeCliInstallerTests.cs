using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Native CLI Installer behavior.
    /// </summary>
    public class NativeCliInstallerTests
    {
        [Test]
        public void GetInstallCommand_OnMacKeepsCliOnlyCurlInstallerAvailable()
        {
            // Verifies that editor and CLI installs use the same channel installer script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
            Assert.That(command.Arguments, Does.Contain("-l -i -c"));
            Assert.That(command.Arguments, Does.Contain("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.sh"));
            Assert.That(command.Arguments, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION"));
            Assert.That(command.Arguments, Does.Contain("v3.0.0-beta.3"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacDelegatesPosixSnippetThroughSh()
        {
            // Verifies that fish or other login shells only load environment before POSIX script execution.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                false,
                "/opt/homebrew/bin/fish");

            Assert.That(command.FileName, Is.EqualTo("/opt/homebrew/bin/fish"));
            Assert.That(command.Arguments, Does.Contain("-l -i -c"));
            Assert.That(command.ManualCommand, Does.StartWith($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c "));
            Assert.That(command.ManualCommand, Does.Contain("tmp_script=$(mktemp)"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
        }

        [Test]
        public void GetInstallCommand_OnMacPropagatesInstallerDownloadFailure()
        {
            // Verifies that editor installs do not report success when curl fails before script execution.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Contain(" -o "));
            Assert.That(command.ManualCommand, Does.Contain(" && "));
            Assert.That(command.ManualCommand, Does.Not.Contain("|"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsKeepsCliOnlyPowerShellInstallerAvailable()
        {
            // Verifies that editor and CLI installs use the same channel PowerShell installer script.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.ps1"));
            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_VERSION='v3.0.0-beta.3'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacCliOnlyInstallerDoesNotAdvertiseWindowsLegacyCleanup()
        {
            // Verifies that macOS manual commands do not expose old cleanup flags.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsDoesNotNeedLegacyCleanupFlag()
        {
            // Verifies that Windows installs rely on the installer script's npm uninstall attempt.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.3",
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
        }

        [Test]
        public void GetInstallCommand_WhenLocalPackageOnMacUsesPackageLocalInstaller()
        {
            // Verifies that local package development tests exercise the checked-out installer script.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-local-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string packageResolvedPath = Path.Combine(
                tempRoot,
                CliConstants.UNITY_PACKAGES_DIR_NAME,
                CliConstants.PACKAGE_SOURCE_DIR_NAME);
            string scriptsDirectory = Path.Combine(tempRoot, CliConstants.SCRIPTS_DIR_NAME);
            string scriptPath = Path.Combine(scriptsDirectory, CliConstants.POSIX_INSTALL_SCRIPT_NAME);

            Directory.CreateDirectory(packageResolvedPath);
            Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(scriptPath, string.Empty);

            try
            {
                NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                    RuntimePlatform.OSXEditor,
                    "3.0.0-beta.3",
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
                Assert.That(command.Arguments, Does.Contain("-l -i -c"));
                Assert.That(command.ManualCommand, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
                Assert.That(command.ManualCommand, Does.Contain(scriptPath));
                Assert.That(command.ManualCommand, Does.Contain("ULOOP_VERSION"));
                Assert.That(command.ManualCommand, Does.Contain("v3.0.0-beta.3"));
                Assert.That(command.ManualCommand, Does.Not.Contain("curl -fsSL"));
                Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void GetInstallCommand_WhenLocalPackageOnWindowsUsesPackageLocalInstaller()
        {
            // Verifies that local package development tests use the checked-out PowerShell installer.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-local-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string packageResolvedPath = Path.Combine(
                tempRoot,
                CliConstants.UNITY_PACKAGES_DIR_NAME,
                CliConstants.PACKAGE_SOURCE_DIR_NAME);
            string scriptsDirectory = Path.Combine(tempRoot, CliConstants.SCRIPTS_DIR_NAME);
            string scriptPath = Path.Combine(scriptsDirectory, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);

            Directory.CreateDirectory(packageResolvedPath);
            Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(scriptPath, string.Empty);

            try
            {
                NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                    RuntimePlatform.WindowsEditor,
                    "3.0.0-beta.3",
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("powershell"));
                Assert.That(command.ManualCommand, Does.Contain($"& '{scriptPath}'"));
                Assert.That(command.ManualCommand, Does.Contain("$env:ULOOP_VERSION='v3.0.0-beta.3'"));
                Assert.That(command.ManualCommand, Does.Not.Contain("irm"));
                Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void BuildInstallerScriptUrl_WhenBetaVersionUsesV3BetaInstallerScript()
        {
            // Verifies that beta editor installs call the same beta installer script as CLI commands.
            string url = NativeCliInstaller.BuildInstallerScriptUrl(
                "v3.0.0-beta.3",
                CliConstants.POSIX_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.sh"));
        }

        [Test]
        public void BuildInstallerScriptUrl_WhenStableVersionUsesMainInstallerScript()
        {
            // Verifies that stable editor installs call the stable installer script.
            string url = NativeCliInstaller.BuildInstallerScriptUrl(
                "v3.0.0",
                CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo("https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerExecutableIsMissingReturnsFailure()
        {
            // Verifies that release installer startup failure stays inside the install result contract.
            NativeCliInstallCommand command = new(
                "missing-uloop-release-installer",
                "--version",
                "missing-uloop-release-installer --version");

            CliInstallResult result = NativeCliInstaller.RunInstallCommand(command, CancellationToken.None, 1000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("Failed to start release CLI dispatcher installer"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerDoesNotExitReturnsFailure()
        {
            // Verifies that release installer stalls cannot leave the editor setup task alive forever.
            NativeCliInstallCommand command = BuildLongRunningInstallCommand();

            CliInstallResult result = NativeCliInstaller.RunInstallCommand(command, CancellationToken.None, 50);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("timed out"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsPrependsMissingNativeInstallDir()
        {
            // Verifies that Unity's current Windows PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
        }

        private static NativeCliInstallCommand BuildLongRunningInstallCommand()
        {
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                return new NativeCliInstallCommand(
                    "powershell",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Start-Sleep -Seconds 5\"",
                    "Start-Sleep -Seconds 5");
            }

            return new NativeCliInstallCommand(
                "/bin/sh",
                "-c \"sleep 5\"",
                "sleep 5");
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsMovesExistingNativeInstallDirToFront()
        {
            // Verifies that a later Windows native install dir does not leave an earlier npm shim first.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm;C:\\USERS\\MASAMICHI\\PROGRAMS\\ULOOP\\BIN",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnMacPrependsMissingNativeInstallDir()
        {
            // Verifies that POSIX PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "/usr/local/bin",
                "/Users/masamichi/.local/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/Users/masamichi/.local/bin:/usr/local/bin"));
        }

        [Test]
        public void PersistInstallDirectoryToUserPath_OnWindowsUpdatesUserPath()
        {
            // Verifies that Windows editor installs survive Unity restarts by updating User PATH.
            string capturedName = null;
            string capturedValue = null;
            System.EnvironmentVariableTarget capturedTarget = default;

            CliInstallResult result = NativeCliInstaller.PersistInstallDirectoryToUserPath(
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                (name, target) => "C:\\npm",
                (name, value, target) =>
                {
                    capturedName = name;
                    capturedValue = value;
                    capturedTarget = target;
                });

            Assert.That(result.Success, Is.True);
            Assert.That(capturedName, Is.EqualTo("Path"));
            Assert.That(capturedValue, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
            Assert.That(capturedTarget, Is.EqualTo(System.EnvironmentVariableTarget.User));
        }

        [Test]
        public void PersistInstallDirectoryToUserPath_OnMacDoesNothing()
        {
            // Verifies that POSIX editor installs do not attempt unsupported .NET User PATH writes.
            bool wroteUserPath = false;

            CliInstallResult result = NativeCliInstaller.PersistInstallDirectoryToUserPath(
                "/Users/masamichi/.local/bin",
                RuntimePlatform.OSXEditor,
                (name, target) => "/usr/local/bin",
                (name, value, target) => { wroteUserPath = true; });

            Assert.That(result.Success, Is.True);
            Assert.That(wroteUserPath, Is.False);
        }

        [Test]
        public void PersistInstallDirectoryToUserPath_OnWindowsSurfacesPermissionFailure()
        {
            // Verifies that permission failures are reported instead of crashing the editor installer.
            CliInstallResult result = NativeCliInstaller.PersistInstallDirectoryToUserPath(
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                (name, target) => "C:\\npm",
                (name, value, target) => throw new System.UnauthorizedAccessException("denied"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("failed to persist the uLoop CLI install directory"));
            Assert.That(result.ErrorOutput, Does.Contain("denied"));
        }

        [Test]
        public void BuildPathWithoutInstallDirectory_OnWindowsRemovesNativeInstallDir()
        {
            // Verifies that uninstall removes every matching native CLI PATH entry.
            string result = NativeCliInstaller.BuildPathWithoutInstallDirectory(
                "C:\\npm;C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\Other;C:\\USERS\\MASAMICHI\\PROGRAMS\\ULOOP\\BIN",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\npm;C:\\Other"));
        }

        [Test]
        public void RemoveInstallDirectoryFromUserPath_OnWindowsUpdatesUserPath()
        {
            // Verifies that uninstall persists removal from Windows User PATH.
            string capturedName = null;
            string capturedValue = null;
            System.EnvironmentVariableTarget capturedTarget = default;

            CliInstallResult result = NativeCliInstaller.RemoveInstallDirectoryFromUserPath(
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                (name, target) => "C:\\npm;C:\\Users\\masamichi\\Programs\\uloop\\bin",
                (name, value, target) =>
                {
                    capturedName = name;
                    capturedValue = value;
                    capturedTarget = target;
                });

            Assert.That(result.Success, Is.True);
            Assert.That(capturedName, Is.EqualTo("Path"));
            Assert.That(capturedValue, Is.EqualTo("C:\\npm"));
            Assert.That(capturedTarget, Is.EqualTo(System.EnvironmentVariableTarget.User));
        }

        [Test]
        public void RemoveInstallDirectoryFromUserPath_OnMacDoesNothing()
        {
            // Verifies that POSIX uninstalls do not attempt unsupported .NET User PATH writes.
            bool wroteUserPath = false;

            CliInstallResult result = NativeCliInstaller.RemoveInstallDirectoryFromUserPath(
                "/Users/masamichi/.local/bin",
                RuntimePlatform.OSXEditor,
                (name, target) => "/Users/masamichi/.local/bin",
                (name, value, target) => { wroteUserPath = true; });

            Assert.That(result.Success, Is.True);
            Assert.That(wroteUserPath, Is.False);
        }

        [Test]
        public void UninstallGlobalCli_OnWindowsDeletesCommandAndEmptyNativeInstallTree()
        {
            // Verifies that uninstall removes the native CLI binary and empty package-owned directories.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string nativeRoot = Path.Combine(tempRoot, "Programs", "uloop");
            string installDir = Path.Combine(nativeRoot, "bin");
            string installPath = NativeCliInstaller.GetGlobalCliInstallPath(
                installDir,
                RuntimePlatform.WindowsEditor);
            string stagedInstallPath = Path.Combine(installDir, ".uloop.exe.install-test");

            Directory.CreateDirectory(installDir);
            File.WriteAllText(installPath, "native-binary");
            File.WriteAllText(stagedInstallPath, "staged-binary");

            try
            {
                CliInstallResult result = NativeCliInstaller.UninstallGlobalCli(
                    installDir,
                    RuntimePlatform.WindowsEditor);

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(File.Exists(installPath), Is.False);
                Assert.That(File.Exists(stagedInstallPath), Is.False);
                Assert.That(Directory.Exists(nativeRoot), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void FinishSuccessfulInstall_WhenPathPersistenceFailsReturnsPathFailure()
        {
            // Verifies that PATH persistence failure is reported after the current process PATH is updated.
            bool appliedCurrentPath = false;

            CliInstallResult result = NativeCliInstaller.FinishSuccessfulInstall(
                new CliInstallResult(true, ""),
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                platform => { appliedCurrentPath = true; },
                (installDirectory, platform) => new CliInstallResult(false, "path failed"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("path failed"));
            Assert.That(appliedCurrentPath, Is.True);
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnMacMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the POSIX installer default install directory.
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.OSXEditor,
                "/Users/masamichi",
                null);

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine("/Users/masamichi", ".local", "bin")));
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnWindowsMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the PowerShell installer default install directory.
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine(
                "C:\\Users\\masamichi\\AppData\\Local",
                "Programs",
                "uloop",
                "bin")));
        }

        [Test]
        public void IsDefaultInstallDirectoryForCurrentUser_WhenWindowsDefaultDirectoryReturnsTrue()
        {
            // Verifies that uninstall can clean PATH entries for the package-owned default directory.
            bool result = NativeCliInstaller.IsDefaultInstallDirectoryForCurrentUser(
                System.IO.Path.Combine(
                    "C:\\Users\\masamichi\\AppData\\Local",
                    "Programs",
                    "uloop",
                    "bin"),
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsDefaultInstallDirectoryForCurrentUser_WhenWindowsSharedDirectoryReturnsFalse()
        {
            // Verifies that uninstall preserves user-owned shared PATH directories such as C:\Tools.
            bool result = NativeCliInstaller.IsDefaultInstallDirectoryForCurrentUser(
                "C:\\Tools",
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutableMatchesInstallDirectoryReturnsTrue()
        {
            // Verifies that uninstall is available only for the package-owned command path.
            bool result = NativeCliInstaller.IsPackageOwnedInstallPath(
                "C:/Users/masamichi/AppData/Local/Programs/uloop/bin/uloop.exe",
                "C:\\Users\\masamichi\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutableIsSharedCommandReturnsFalse()
        {
            // Verifies that same-version shared commands do not route the settings button to uninstall.
            bool result = NativeCliInstaller.IsPackageOwnedInstallPath(
                "C:\\Tools\\uloop.exe",
                "C:\\Users\\masamichi\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldRemoveInstallDirectoryFromPath_WhenWindowsDefaultDirectoryReturnsTrue()
        {
            // Verifies that Windows uninstalls can remove the package-owned default PATH directory.
            bool result = NativeCliInstaller.ShouldRemoveInstallDirectoryFromPath(
                System.IO.Path.Combine(
                    "C:\\Users\\masamichi\\AppData\\Local",
                    "Programs",
                    "uloop",
                    "bin"),
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldRemoveInstallDirectoryFromPath_WhenMacDefaultDirectoryReturnsFalse()
        {
            // Verifies that POSIX uninstalls preserve shared directories such as ~/.local/bin.
            bool result = NativeCliInstaller.ShouldRemoveInstallDirectoryFromPath(
                System.IO.Path.Combine("/Users/masamichi", ".local", "bin"),
                RuntimePlatform.OSXEditor,
                "/Users/masamichi",
                null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldRemoveInstallDirectoryFromPath_WhenWindowsSharedDirectoryReturnsFalse()
        {
            // Verifies that Windows uninstalls preserve user-owned shared PATH directories.
            bool result = NativeCliInstaller.ShouldRemoveInstallDirectoryFromPath(
                "C:\\Tools",
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.False);
        }
    }
}
