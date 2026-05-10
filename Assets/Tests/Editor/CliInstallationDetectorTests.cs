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
        public void SelectPreferredDetection_WhenPackageOwnedDispatcherExistsUsesItBeforeShellPath()
        {
            // Verifies that a stale shell shim cannot hide the installed native Dispatcher.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/masamichi/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/masamichi/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("3.0.0-beta.3"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/masamichi/.local/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenPackageOwnedDispatcherMissingUsesShellPath()
        {
            // Verifies that legacy CLI installs still surface as update candidates.
            CliInstallationDetection packageOwnedDetection = new(
                null,
                "/Users/masamichi/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/masamichi/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/masamichi/.npm-global/bin/uloop"));
        }
    }
}
