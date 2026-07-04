using System.Diagnostics;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI installation detection behavior.
    /// </summary>
    public class CliInstallationDetectorTests
    {
        [Test]
        public void SelectPreferredDetection_WhenShellCommandShadowsPackageOwnedCliUsesShellPath()
        {
            // Verifies that the settings UI reports the same CLI command the user's terminal runs.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellCommandMissingUsesPackageOwnedCli()
        {
            // Verifies that package-owned installs still count when the shell cannot resolve uloop.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                null,
                null);

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("3.0.0-beta.3"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.local/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellCommandExistsButVersionFailsUsesShellPath()
        {
            // Verifies that a broken PATH command is surfaced instead of hidden by the package-owned binary.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                null,
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.Null);
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenPackageOwnedCliMissingUsesShellPath()
        {
            // Verifies that legacy CLI installs still surface as update candidates.
            CliInstallationDetection packageOwnedDetection = new(
                null,
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellVersionExistsWithoutPathUsesShellVersion()
        {
            // Verifies that installed state does not depend on command path availability.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                null);

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.Null);
        }

        [Test]
        public void BuildShellCliDetectionCommand_UsesShortVersionFlag()
        {
            // Verifies that shell detection asks the command itself for its terminal-visible version.
            string command = CliInstallationDetector.BuildShellCliDetectionCommand("uloop");

            Assert.That(command, Does.Contain("command -v uloop"));
            Assert.That(command, Does.Contain("uloop --version --json"));
            Assert.That(command, Does.Contain("uloop_contract_status=$?"));
            Assert.That(command, Does.Contain("uloop -v"));
            Assert.That(command, Does.Contain("uloop_version_status=$?"));
            Assert.That(command, Does.Contain("__ULOOP_CONTRACT_STATUS_START__"));
            Assert.That(command, Does.Contain("__ULOOP_VERSION_STATUS_START__"));
        }

        [Test]
        public void BuildShellCliDetectionCommandForShell_WhenRuntimeShellIsFish_UsesFishStatusSyntax()
        {
            // Verifies that command syntax follows the actual shell process, not PATH setup support.
            string command = CliInstallationDetector.BuildShellCliDetectionCommandForShell(
                "uloop",
                "/opt/homebrew/bin/fish");

            Assert.That(command, Does.Contain("set uloop_version_status $status"));
            Assert.That(command, Does.Contain("set uloop_contract_status $status"));
            Assert.That(command, Does.Not.Contain("uloop_version_status=$?"));
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenPathAndVersionExist_ReturnsDetection()
        {
            // Verifies that shell detection keeps terminal-visible path data as auxiliary UI context.
            string output = "banner\n"
                            + "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.npm-global/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_CONTRACT_START__\n"
                            + "unknown option: --json\n"
                            + "__ULOOP_CONTRACT_END__\n"
                            + "__ULOOP_CONTRACT_STATUS_START__\n"
                            + "1\n"
                            + "__ULOOP_CONTRACT_STATUS_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("2.1.1"));
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenLegacyCliJsonExists_ReturnsVersionWithoutProtocol()
        {
            // Verifies pre-dispatcher CLIs cannot satisfy the dispatcher setup contract.
            string output = "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.npm-global/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_CONTRACT_START__\n"
                            + "{\"CliVersion\":\"3.0.0-beta.31\",\"ProtocolVersion\":1}\n"
                            + "__ULOOP_CONTRACT_END__\n"
                            + "__ULOOP_CONTRACT_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_CONTRACT_STATUS_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("3.0.0-beta.31"));
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenDispatcherJsonExists_ReturnsDispatcherDetection()
        {
            // Verifies setup detection recognizes the dispatcher release exposed by global uloop.
            string output = "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.local/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_CONTRACT_START__\n"
                            + "{\"DispatcherVersion\":\"3.0.0\"}\n"
                            + "__ULOOP_CONTRACT_END__\n"
                            + "__ULOOP_CONTRACT_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_CONTRACT_STATUS_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "3.0.0\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("3.0.0"));
            Assert.That(detection.IsDispatcher, Is.True);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.local/bin/uloop"));
        }

        [Test]
        public void IsShellDetectionUsableForPathSetup_WhenLegacyCliVersionIsHigh_ReturnsFalse()
        {
            // Verifies old pre-dispatcher global CLIs cannot satisfy dispatcher path setup.
            CliInstallationDetection detection = new(
                "3.0.0-beta.40",
                "/Users/ExampleUser/.npm-global/bin/uloop");

            bool result = CliInstallationDetector.IsShellDetectionUsableForPathSetup(
                detection,
                UnityEngine.RuntimePlatform.OSXEditor,
                (path, platform) => false,
                "3.0.1-beta.6");

            Assert.That(result, Is.False);
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenProtocolVersionIsOutsideIntRange_ReturnsVersionWithoutProtocol()
        {
            // Verifies oversized protocol metadata cannot break setup compatibility detection.
            string output = "__ULOOP_PATH_START__\n"
                            + "/tmp/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_CONTRACT_START__\n"
                            + "{\"CliVersion\":\"3.0.0-beta.31\",\"ProtocolVersion\":2147483648}\n"
                            + "__ULOOP_CONTRACT_END__\n"
                            + "__ULOOP_CONTRACT_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_CONTRACT_STATUS_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("3.0.0-beta.31"));
            Assert.That(detection.ExecutablePath, Is.EqualTo("/tmp/uloop"));
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenOnlyVersionExists_ReturnsInstalledDetection()
        {
            // Verifies that installation state depends on version output, not path availability.
            string output = "__ULOOP_PATH_START__\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("2.1.1"));
            Assert.That(detection.ExecutablePath, Is.Null);
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenVersionCommandFails_ReturnsPathWithoutVersion()
        {
            // Verifies that failed shell probes do not treat stdout usage text as a CLI version.
            string output = "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.npm-global/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "usage: broken uloop\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "1\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.Null);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void KillProcessIfRunning_WhenProcessAlreadyExited_DoesNotThrow()
        {
            // Verifies that process cleanup tolerates the race where the child exits before Kill.
            ProcessStartInfo startInfo = BuildImmediateExitProcessStartInfo();

            using Process process = Process.Start(startInfo);
            process.WaitForExit();

            Assert.DoesNotThrow(() => CliInstallationDetector.KillProcessIfRunning(process));
        }

        private static ProcessStartInfo BuildImmediateExitProcessStartInfo()
        {
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }

            return new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"exit 0\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }
    }
}
