using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests
{
    public class NativeCliInstallerTests
    {
        [Test]
        public void GetInstallCommand_OnMacKeepsCliOnlyCurlInstallerAvailable()
        {
            // Verifies that CLI-only macOS users still have the direct release script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.0",
                false);

            Assert.That(command.FileName, Is.EqualTo("/bin/sh"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.sh"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsKeepsCliOnlyPowerShellInstallerAvailable()
        {
            // Verifies that CLI-only Windows users still have the direct release script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.0",
                false);

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.ps1"));
            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacCliOnlyInstallerCanOptIntoLegacyNpmRemoval()
        {
            // Verifies that CLI-only macOS installs can opt into removing the legacy npm launcher.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.0",
                true);

            Assert.That(command.Arguments, Does.Contain("ULOOP_REMOVE_LEGACY='1'"));
            Assert.That(command.ManualCommand, Does.Contain("ULOOP_REMOVE_LEGACY='1'"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsCliOnlyInstallerCanOptIntoLegacyNpmRemoval()
        {
            // Verifies that CLI-only Windows installs can opt into removing the legacy npm launcher.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.0",
                true);

            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_REMOVE_LEGACY='1'"));
            Assert.That(command.ManualCommand, Does.Contain("$env:ULOOP_REMOVE_LEGACY='1'"));
        }

        [Test]
        public void GetGlobalCliBundlePath_OnMacArm64UsesPackagedDispatcher()
        {
            // Verifies that the editor installer reads the bundled macOS dispatcher from the package.
            string result = NativeCliInstaller.GetGlobalCliBundlePath(
                "/package",
                RuntimePlatform.OSXEditor,
                Architecture.Arm64);

            Assert.That(result, Is.EqualTo(Path.Combine(
                "/package",
                "GoCli~",
                "dist",
                "darwin-arm64",
                "uloop-dispatcher")));
        }

        [Test]
        public void GetGlobalCliBundlePath_OnWindowsUsesPackagedDispatcher()
        {
            // Verifies that the editor installer reads the bundled Windows dispatcher from the package.
            string result = NativeCliInstaller.GetGlobalCliBundlePath(
                "C:\\package",
                RuntimePlatform.WindowsEditor,
                Architecture.X64);

            Assert.That(result, Is.EqualTo(Path.Combine(
                "C:\\package",
                "GoCli~",
                "dist",
                "windows-amd64",
                "uloop-dispatcher.exe")));
        }

        [Test]
        public void InstallGlobalCliFromBundle_OnWindowsCopiesDispatcherAsUloopExe()
        {
            // Verifies that editor install copies the bundled dispatcher as the user-facing uloop command.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(tempRoot, "source");
            string sourcePath = Path.Combine(sourceDir, "uloop-dispatcher.exe");
            string installDir = Path.Combine(tempRoot, "install");

            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(sourcePath, "fake-binary");

            try
            {
                CliInstallResult result = NativeCliInstaller.InstallGlobalCliFromBundle(
                    sourcePath,
                    installDir,
                    RuntimePlatform.WindowsEditor);

                string installedPath = NativeCliInstaller.GetGlobalCliInstallPath(
                    installDir,
                    RuntimePlatform.WindowsEditor);
                Assert.That(result.Success, Is.True);
                Assert.That(result.ErrorOutput, Is.Empty);
                Assert.That(Path.GetFileName(installedPath), Is.EqualTo("uloop.exe"));
                Assert.That(File.ReadAllText(installedPath), Is.EqualTo("fake-binary"));
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
        public void InstallGlobalCliFromBundle_WhenInstallPathExistsReplacesPreviousCommand()
        {
            // Verifies that editor install swaps the staged dispatcher into the final command path.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(tempRoot, "source");
            string sourcePath = Path.Combine(sourceDir, "uloop-dispatcher.exe");
            string installDir = Path.Combine(tempRoot, "install");
            string installPath = NativeCliInstaller.GetGlobalCliInstallPath(
                installDir,
                RuntimePlatform.WindowsEditor);

            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(installDir);
            File.WriteAllText(sourcePath, "new-binary");
            File.WriteAllText(installPath, "old-binary");

            try
            {
                CliInstallResult result = NativeCliInstaller.InstallGlobalCliFromBundle(
                    sourcePath,
                    installDir,
                    RuntimePlatform.WindowsEditor);

                Assert.That(result.Success, Is.True);
                Assert.That(File.ReadAllText(installPath), Is.EqualTo("new-binary"));
                Assert.That(Directory.GetFiles(installDir, ".uloop.exe.install-*"), Is.Empty);
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
        public void InstallGlobalCliFromBundle_WhenBundleIsMissingReturnsFailure()
        {
            // Verifies that editor install reports a missing packaged dispatcher without creating the install dir.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(tempRoot, "missing", "uloop-dispatcher.exe");
            string installDir = Path.Combine(tempRoot, "install");

            try
            {
                CliInstallResult result = NativeCliInstaller.InstallGlobalCliFromBundle(
                    sourcePath,
                    installDir,
                    RuntimePlatform.WindowsEditor);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorOutput, Does.Contain("Global CLI dispatcher binary was not found"));
                Assert.That(Directory.Exists(installDir), Is.False);
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
        public void InstallGlobalCliFromBundle_WhenInstallDirectoryIsFileReturnsFailure()
        {
            // Verifies that expected filesystem setup failures stay inside the installer result contract.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(tempRoot, "source");
            string sourcePath = Path.Combine(sourceDir, "uloop-dispatcher.exe");
            string installDir = Path.Combine(tempRoot, "install-as-file");

            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(sourcePath, "fake-binary");
            File.WriteAllText(installDir, "not-a-directory");

            try
            {
                CliInstallResult result = NativeCliInstaller.InstallGlobalCliFromBundle(
                    sourcePath,
                    installDir,
                    RuntimePlatform.WindowsEditor);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorOutput, Does.Contain("Failed to install bundled CLI dispatcher"));
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
        public void InstallGlobalCliFromBundle_WhenInstallDirectoryContainsNullCharacterReturnsFailure()
        {
            // Verifies that invalid user-provided install paths stay inside the installer result contract.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(tempRoot, "source");
            string sourcePath = Path.Combine(sourceDir, "uloop-dispatcher.exe");
            string installDir = tempRoot + Path.DirectorySeparatorChar + "bad\0path";

            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(sourcePath, "fake-binary");

            try
            {
                CliInstallResult result = NativeCliInstaller.InstallGlobalCliFromBundle(
                    sourcePath,
                    installDir,
                    RuntimePlatform.WindowsEditor);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorOutput, Does.Contain("Failed to install bundled CLI dispatcher"));
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
        public void BuildPathWithInstallDirectory_OnWindowsPrependsMissingNativeInstallDir()
        {
            // Verifies that Unity's current Windows PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
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
        public void CleanupLegacyCommandShimsInDirectory_WhenOrphanedNpmShimsExistDeletesThem()
        {
            // Verifies that orphaned npm launchers no longer remain earlier in PATH.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string nativeDirectory = Path.Combine(tempRoot, "native");
            string nativeUloopPath = Path.Combine(nativeDirectory, "uloop.exe");

            Directory.CreateDirectory(legacyBinDirectory);
            Directory.CreateDirectory(nativeDirectory);
            File.WriteAllText(nativeUloopPath, "native-binary");
            File.WriteAllText(Path.Combine(legacyBinDirectory, "uloop.ps1"), "node_modules/uloop-cli/dist/cli.bundle.cjs");
            File.WriteAllText(Path.Combine(legacyBinDirectory, "uloop.cmd"), "node_modules\\uloop-cli\\dist\\cli.bundle.cjs");
            File.WriteAllText(Path.Combine(legacyBinDirectory, "uloop"), "node_modules/uloop-cli/dist/cli.bundle.cjs");

            try
            {
                CliInstallResult result = NativeCliInstaller.CleanupLegacyCommandShimsInDirectory(
                    legacyBinDirectory,
                    nativeUloopPath);

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(File.Exists(Path.Combine(legacyBinDirectory, "uloop.ps1")), Is.False);
                Assert.That(File.Exists(Path.Combine(legacyBinDirectory, "uloop.cmd")), Is.False);
                Assert.That(File.Exists(Path.Combine(legacyBinDirectory, "uloop")), Is.False);
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
        public void CleanupLegacyCommandShimsInDirectory_WhenForwardingShimsExistDeletesThem()
        {
            // Verifies that forwarders written by older native installs are treated as package-owned cleanup targets.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string nativeDirectory = Path.Combine(tempRoot, "native");
            string nativeUloopPath = Path.Combine(nativeDirectory, "uloop.exe");
            string powerShellShimPath = Path.Combine(legacyBinDirectory, "uloop.ps1");

            Directory.CreateDirectory(legacyBinDirectory);
            Directory.CreateDirectory(nativeDirectory);
            File.WriteAllText(nativeUloopPath, "native-binary");
            File.WriteAllText(powerShellShimPath, $"& '{nativeUloopPath}' @args");

            try
            {
                CliInstallResult result = NativeCliInstaller.CleanupLegacyCommandShimsInDirectory(
                    legacyBinDirectory,
                    nativeUloopPath);

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(File.Exists(powerShellShimPath), Is.False);
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
        public void CleanupLegacyCommandShimsInDirectory_WhenForwardingShimTargetsPreviousDefaultInstallDeletesIt()
        {
            // Verifies that stale forwarders are cleaned even when they reference an older native install path.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string nativeUloopPath = Path.Combine(tempRoot, "custom", "uloop.exe");
            string oldNativeUloopPath = Path.Combine(
                "C:\\Users\\masamichi\\AppData\\Local",
                CliConstants.WINDOWS_PROGRAMS_DIR_NAME,
                CliConstants.NATIVE_INSTALL_DIR_NAME,
                CliConstants.NATIVE_INSTALL_BIN_DIR_NAME,
                CliConstants.GLOBAL_WINDOWS_COMMAND_NAME);
            string commandShimPath = Path.Combine(legacyBinDirectory, "uloop.cmd");

            Directory.CreateDirectory(legacyBinDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(nativeUloopPath));
            File.WriteAllText(commandShimPath, $"@\"{oldNativeUloopPath}\" %*");

            try
            {
                CliInstallResult result = NativeCliInstaller.CleanupLegacyCommandShimsInDirectory(
                    legacyBinDirectory,
                    nativeUloopPath);

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(File.Exists(commandShimPath), Is.False);
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
        public void CleanupLegacyCommandShimsInDirectory_WhenCommandIsNotPackageOwnedPreservesFile()
        {
            // Verifies that unrelated user commands are not overwritten by legacy cleanup.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string nativeUloopPath = Path.Combine(tempRoot, "native", "uloop.exe");
            string commandPath = Path.Combine(legacyBinDirectory, "uloop.ps1");
            string customCommand = "Write-Host 'custom'";

            Directory.CreateDirectory(legacyBinDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(nativeUloopPath));
            File.WriteAllText(nativeUloopPath, "native-binary");
            File.WriteAllText(commandPath, customCommand);

            try
            {
                CliInstallResult result = NativeCliInstaller.CleanupLegacyCommandShimsInDirectory(
                    legacyBinDirectory,
                    nativeUloopPath);

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(File.ReadAllText(commandPath), Is.EqualTo(customCommand));
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
        public void RemoveLegacyNpmBinDirectoryFromPathIfUnused_WhenOnlyNodeModulesRemainRemovesPath()
        {
            // Verifies that the npm global bin directory is removed from PATH only after command shims are gone.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string userPath = $"C:\\Tools;{legacyBinDirectory};C:\\Other";
            string processPath = $"{legacyBinDirectory};C:\\ProcessTools";
            string capturedUserPath = null;
            string capturedProcessPath = null;

            Directory.CreateDirectory(Path.Combine(legacyBinDirectory, "node_modules"));

            try
            {
                CliInstallResult result = NativeCliInstaller.RemoveLegacyNpmBinDirectoryFromPathIfUnused(
                    legacyBinDirectory,
                    RuntimePlatform.WindowsEditor,
                    (name, target) => userPath,
                    (name, value, target) => { capturedUserPath = value; },
                    name => processPath,
                    (name, value) => { capturedProcessPath = value; });

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(capturedUserPath, Is.EqualTo("C:\\Tools;C:\\Other"));
                Assert.That(capturedProcessPath, Is.EqualTo("C:\\ProcessTools"));
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
        public void RemoveLegacyNpmBinDirectoryFromPathIfUnused_WhenOtherCommandsRemainKeepsPath()
        {
            // Verifies that npm, npx, and other global command shims keep the npm bin directory in PATH.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string capturedUserPath = null;
            string capturedProcessPath = null;

            Directory.CreateDirectory(Path.Combine(legacyBinDirectory, "node_modules"));
            File.WriteAllText(Path.Combine(legacyBinDirectory, "npm.cmd"), "npm");

            try
            {
                CliInstallResult result = NativeCliInstaller.RemoveLegacyNpmBinDirectoryFromPathIfUnused(
                    legacyBinDirectory,
                    RuntimePlatform.WindowsEditor,
                    (name, target) => legacyBinDirectory,
                    (name, value, target) => { capturedUserPath = value; },
                    name => legacyBinDirectory,
                    (name, value) => { capturedProcessPath = value; });

                Assert.That(result.Success, Is.True, result.ErrorOutput);
                Assert.That(capturedUserPath, Is.Null);
                Assert.That(capturedProcessPath, Is.Null);
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
        public void HasLegacyNpmInstallation_WhenNpmPackageExistsReturnsTrue()
        {
            // Verifies that the installer can prompt before removing an installed npm package.
            bool result = NativeCliInstaller.HasLegacyNpmInstallation(
                RuntimePlatform.WindowsEditor,
                (command, platform) => new CliInstallResult(true, ""),
                applicationData: null);

            Assert.That(result, Is.True);
        }

        [Test]
        public void HasLegacyNpmInstallation_WhenOrphanedWindowsShimExistsReturnsTrue()
        {
            // Verifies that stale npm launchers are still detected when npm itself cannot report the package.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");

            Directory.CreateDirectory(legacyBinDirectory);
            File.WriteAllText(
                Path.Combine(legacyBinDirectory, "uloop.ps1"),
                "node_modules/uloop-cli/dist/cli.bundle.cjs");

            try
            {
                bool result = NativeCliInstaller.HasLegacyNpmInstallation(
                    RuntimePlatform.WindowsEditor,
                    (command, platform) => new CliInstallResult(false, ""),
                    tempRoot);

                Assert.That(result, Is.True);
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
        public void HasLegacyNpmInstallation_WhenForwardingWindowsShimExistsReturnsTrue()
        {
            // Verifies that setup can prompt again for forwarders created by earlier native installer versions.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string nativeUloopPath = Path.Combine(tempRoot, "native", "uloop.exe");

            Directory.CreateDirectory(legacyBinDirectory);
            File.WriteAllText(
                Path.Combine(legacyBinDirectory, "uloop.ps1"),
                $"& '{nativeUloopPath}' @args");

            try
            {
                bool result = NativeCliInstaller.HasLegacyNpmInstallation(
                    RuntimePlatform.WindowsEditor,
                    (command, platform) => new CliInstallResult(false, ""),
                    tempRoot,
                    nativeUloopPath);

                Assert.That(result, Is.True);
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
        public void HasLegacyNpmInstallation_WhenForwardingShimTargetsBundledDispatcherReturnsTrue()
        {
            // Verifies that package-cache dispatcher forwarders are detected as stale package-owned shims.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string legacyBinDirectory = Path.Combine(tempRoot, "npm");
            string bundledDispatcherPath = Path.Combine(
                tempRoot,
                "Library",
                "PackageCache",
                CliConstants.GO_CLI_PACKAGE_DIR_NAME,
                CliConstants.DIST_DIR_NAME,
                "windows-amd64",
                CliConstants.GLOBAL_DISPATCHER_WINDOWS_BUNDLE_NAME);

            Directory.CreateDirectory(legacyBinDirectory);
            File.WriteAllText(
                Path.Combine(legacyBinDirectory, "uloop.ps1"),
                $"& '{bundledDispatcherPath}' @args");

            try
            {
                bool result = NativeCliInstaller.HasLegacyNpmInstallation(
                    RuntimePlatform.WindowsEditor,
                    (command, platform) => new CliInstallResult(false, ""),
                    tempRoot);

                Assert.That(result, Is.True);
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
        public void HasLegacyNpmInstallation_WhenNoPackageOrShimReturnsFalse()
        {
            // Verifies that clean systems can install without a legacy removal prompt.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-installer-tests",
                System.Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(tempRoot, "npm"));

            try
            {
                bool result = NativeCliInstaller.HasLegacyNpmInstallation(
                    RuntimePlatform.WindowsEditor,
                    (command, platform) => new CliInstallResult(false, ""),
                    tempRoot);

                Assert.That(result, Is.False);
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
        public void RemoveLegacyNpmPackageIfPresent_WhenPackageMissingSkipsUninstall()
        {
            // Verifies that package installs do not require npm when the legacy launcher is absent.
            int runCount = 0;

            CliInstallResult result = NativeCliInstaller.RemoveLegacyNpmPackageIfPresent(
                RuntimePlatform.OSXEditor,
                (command, platform) =>
                {
                    runCount++;
                    Assert.That(command.ManualCommand, Does.Contain("npm list -g uloop-cli"));
                    return new CliInstallResult(false, "");
                });

            Assert.That(result.Success, Is.True);
            Assert.That(runCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveLegacyNpmPackageIfPresent_WhenPackageExistsRunsUninstall()
        {
            // Verifies that package installs preserve the previous UI cleanup of the legacy npm launcher.
            int runCount = 0;

            CliInstallResult result = NativeCliInstaller.RemoveLegacyNpmPackageIfPresent(
                RuntimePlatform.WindowsEditor,
                (command, platform) =>
                {
                    runCount++;
                    string expectedCommand = runCount == 1
                        ? "npm list -g uloop-cli"
                        : "npm uninstall -g uloop-cli";
                    Assert.That(command.ManualCommand, Does.Contain(expectedCommand));
                    return new CliInstallResult(true, "");
                });

            Assert.That(result.Success, Is.True);
            Assert.That(runCount, Is.EqualTo(2));
        }

        [Test]
        public void RemoveLegacyNpmPackageIfPresent_WhenUninstallFailsReturnsManualCommand()
        {
            // Verifies that package installs fail visibly when the legacy launcher cannot be removed.
            int runCount = 0;

            CliInstallResult result = NativeCliInstaller.RemoveLegacyNpmPackageIfPresent(
                RuntimePlatform.WindowsEditor,
                (command, platform) =>
                {
                    runCount++;
                    return runCount == 1
                        ? new CliInstallResult(true, "")
                        : new CliInstallResult(false, "denied");
                });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("Failed to remove legacy npm installation"));
            Assert.That(result.ErrorOutput, Does.Contain("npm uninstall -g uloop-cli"));
            Assert.That(result.ErrorOutput, Does.Contain("denied"));
        }

        [Test]
        public void FinishSuccessfulBundleInstall_WhenLegacyRemovalFailsStillUpdatesPaths()
        {
            // Verifies that install still updates PATH and removes stale shims before reporting npm cleanup failure.
            bool appliedCurrentPath = false;
            bool cleanedLegacyShims = false;
            bool persistedUserPath = false;

            CliInstallResult result = NativeCliInstaller.FinishSuccessfulBundleInstall(
                new CliInstallResult(true, ""),
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                platform => new CliInstallResult(false, "legacy failed"),
                platform => { appliedCurrentPath = true; },
                (installDirectory, platform) =>
                {
                    cleanedLegacyShims = true;
                    return new CliInstallResult(true, "");
                },
                (installDirectory, platform) =>
                {
                    persistedUserPath = true;
                    return new CliInstallResult(true, "");
                });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("legacy failed"));
            Assert.That(appliedCurrentPath, Is.True);
            Assert.That(cleanedLegacyShims, Is.True);
            Assert.That(persistedUserPath, Is.True);
        }

        [Test]
        public void FinishSuccessfulBundleInstall_WhenPathPersistenceFailsReturnsPathFailure()
        {
            // Verifies that PATH persistence failure is reported after the current process PATH is updated.
            bool appliedCurrentPath = false;

            CliInstallResult result = NativeCliInstaller.FinishSuccessfulBundleInstall(
                new CliInstallResult(true, ""),
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                platform => new CliInstallResult(false, "legacy failed"),
                platform => { appliedCurrentPath = true; },
                (installDirectory, platform) => new CliInstallResult(true, ""),
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
