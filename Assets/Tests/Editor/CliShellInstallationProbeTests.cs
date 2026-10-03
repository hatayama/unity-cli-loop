using System;
using System.Diagnostics;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies login-shell probe construction, path-setup usability and contract parsing of the CLI shell installation probe.
    /// </summary>
    public sealed class CliShellInstallationProbeTests
    {
        private const string InstallDirectory = "/opt/uloop-test/bin";

        /// <summary>
        /// Verifies that the POSIX probe runs the detection script in an interactive login shell with the install directory removed from PATH.
        /// </summary>
        [Test]
        public void BuildShellCliDetectionStartInfo_OnPosix_RunsLoginShellWithoutInstallDirectoryOnPath()
        {
            ProcessStartInfo startInfo = CliShellInstallationProbe.BuildShellCliDetectionStartInfo(
                "/bin/zsh",
                RuntimePlatform.OSXEditor,
                CreatePlan(),
                InstallDirectory + ":/usr/bin:/bin");

            Assert.That(startInfo.FileName, Is.EqualTo("/bin/zsh"));
            Assert.That(
                startInfo.Arguments,
                Is.EqualTo("-l -i -c \"" + CliShellInstallationProbe.BuildShellCliDetectionCommand("uloop") + "\""));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.EnvironmentVariables["PATH"], Is.EqualTo("/usr/bin:/bin"));
        }

        /// <summary>
        /// Verifies that the probe uses fish status syntax when the user's shell is fish.
        /// </summary>
        [Test]
        public void BuildShellCliDetectionStartInfo_WhenShellIsFish_UsesFishDetectionScript()
        {
            ProcessStartInfo startInfo = CliShellInstallationProbe.BuildShellCliDetectionStartInfo(
                "/usr/local/bin/fish",
                RuntimePlatform.OSXEditor,
                CreatePlan(),
                "/usr/bin");

            Assert.That(
                startInfo.Arguments,
                Is.EqualTo("-l -i -c \""
                    + CliShellInstallationProbe.BuildShellCliDetectionCommandForShell("uloop", "/usr/local/bin/fish")
                    + "\""));
            Assert.That(startInfo.Arguments, Does.Contain("set uloop_version_status $status"));
        }

        /// <summary>
        /// Verifies that the Windows probe leaves PATH untouched instead of applying the POSIX install-directory filter.
        /// </summary>
        [Test]
        public void BuildShellCliDetectionStartInfo_OnWindows_DoesNotRewritePath()
        {
            string markerEntry = "/opt/uloop-test-marker-" + Guid.NewGuid().ToString("N");

            ProcessStartInfo startInfo = CliShellInstallationProbe.BuildShellCliDetectionStartInfo(
                "/bin/zsh",
                RuntimePlatform.WindowsEditor,
                CreatePlan(),
                InstallDirectory + ";" + markerEntry);

            Assert.That(startInfo.EnvironmentVariables["PATH"], Is.Not.EqualTo(markerEntry));
        }

        /// <summary>
        /// Verifies that a non-fish shell gets the POSIX status syntax.
        /// </summary>
        [Test]
        public void BuildShellCliDetectionCommandForShell_WhenShellIsBash_UsesPosixStatusSyntax()
        {
            string command = CliShellInstallationProbe.BuildShellCliDetectionCommandForShell("uloop", "/bin/bash");

            Assert.That(command, Is.EqualTo(CliShellInstallationProbe.BuildShellCliDetectionCommand("uloop")));
            Assert.That(command, Does.Contain("uloop_version_status=$?"));
        }

        /// <summary>
        /// Verifies that the package-owned install counts as usable for PATH setup even when it is not a dispatcher.
        /// </summary>
        [Test]
        public void IsShellDetectionUsableForPathSetup_WhenPathIsPackageOwned_ReturnsTrue()
        {
            CliInstallationDetection detection = new CliInstallationDetection("2.0.0", InstallDirectory + "/uloop");

            bool result = CliShellInstallationProbe.IsShellDetectionUsableForPathSetup(
                detection,
                RuntimePlatform.OSXEditor,
                (path, platform) => true,
                "3.0.0");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a dispatcher at or above the minimum version counts as usable for PATH setup.
        /// </summary>
        [Test]
        public void IsShellDetectionUsableForPathSetup_WhenDispatcherMeetsMinimum_ReturnsTrue()
        {
            CliInstallationDetection detection = new CliInstallationDetection("3.1.0", "/usr/local/bin/uloop", true);

            bool result = CliShellInstallationProbe.IsShellDetectionUsableForPathSetup(
                detection,
                RuntimePlatform.OSXEditor,
                (path, platform) => false,
                "3.0.0");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a dispatcher below the minimum version is not usable for PATH setup.
        /// </summary>
        [Test]
        public void IsShellDetectionUsableForPathSetup_WhenDispatcherIsBelowMinimum_ReturnsFalse()
        {
            CliInstallationDetection detection = new CliInstallationDetection("2.9.0", "/usr/local/bin/uloop", true);

            bool result = CliShellInstallationProbe.IsShellDetectionUsableForPathSetup(
                detection,
                RuntimePlatform.OSXEditor,
                (path, platform) => false,
                "3.0.0");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a successful contract call without any version field falls back to the short version output.
        /// </summary>
        [Test]
        public void ParseShellCliInstallationOutput_WhenContractHasNoVersion_FallsBackToShortVersion()
        {
            string output = "__ULOOP_PATH_START__\n"
                + "/usr/local/bin/uloop\n"
                + "__ULOOP_PATH_END__\n"
                + "__ULOOP_CONTRACT_START__\n"
                + "{\"Other\":\"value\"}\n"
                + "__ULOOP_CONTRACT_END__\n"
                + "__ULOOP_CONTRACT_STATUS_START__\n"
                + "0\n"
                + "__ULOOP_CONTRACT_STATUS_END__\n"
                + "__ULOOP_VERSION_START__\n"
                + "2.4.0\n"
                + "__ULOOP_VERSION_END__\n"
                + "__ULOOP_VERSION_STATUS_START__\n"
                + "0\n"
                + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection = CliShellInstallationProbe.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("2.4.0"));
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/usr/local/bin/uloop"));
        }

        /// <summary>
        /// Verifies that contract output consisting only of blank lines yields no version but keeps the executable path.
        /// </summary>
        [Test]
        public void ParseCliContractOutput_WhenOutputHasOnlyBlankLines_ReturnsPathWithoutVersion()
        {
            CliInstallationDetection detection = CliShellInstallationProbe.ParseCliContractOutput(
                "\n   \n\t\n",
                "/usr/local/bin/uloop");

            Assert.That(detection.Version, Is.Null);
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/usr/local/bin/uloop"));
        }

        /// <summary>
        /// Verifies that malformed contract JSON yields no version instead of throwing.
        /// </summary>
        [Test]
        public void ParseCliContractOutput_WhenJsonIsMalformed_ReturnsPathWithoutVersion()
        {
            CliInstallationDetection detection = CliShellInstallationProbe.ParseCliContractOutput(
                "{\"DispatcherVersion\": ",
                "/usr/local/bin/uloop");

            Assert.That(detection.Version, Is.Null);
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/usr/local/bin/uloop"));
        }

        private static CliPathSetupPlan CreatePlan()
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                InstallDirectory,
                "$HOME/.local/bin",
                "<HOME>/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> '<HOME>/.zshrc'");
        }
    }
}
