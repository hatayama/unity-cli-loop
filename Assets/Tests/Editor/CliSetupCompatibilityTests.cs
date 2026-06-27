using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies setup compatibility decisions for the global uloop command.
    /// </summary>
    public class CliSetupCompatibilityTests
    {
        [TestCase(null, false, false, false, false)]
        [TestCase("2.1.10", false, true, false, false)]
        [TestCase("3.0.0", false, true, false, false)]
        [TestCase("2.1.10", true, true, false, false)]
        [TestCase("3.0.0", true, false, false, true)]
        [TestCase("3.0.1", true, false, false, true)]
        public void Evaluate_ReturnsExpectedDispatcherSetupState(
            string cliVersion,
            bool isDispatcher,
            bool expectedNeedsUpdate,
            bool expectedNeedsDowngrade,
            bool expectedCompatible)
        {
            // Verifies setup prompts are based on dispatcher identity and minimum dispatcher version.
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                isDispatcher,
                CliConstants.MINIMUM_REQUIRED_DISPATCHER_VERSION);

            Assert.That(state.NeedsUpdate, Is.EqualTo(expectedNeedsUpdate));
            Assert.That(state.NeedsDowngrade, Is.EqualTo(expectedNeedsDowngrade));
            Assert.That(state.IsCompatible, Is.EqualTo(expectedCompatible));
        }

        [Test]
        public void Evaluate_WhenDispatcherVersionIsInvalid_RequestsUpdate()
        {
            // Verifies malformed dispatcher versions fail closed instead of being treated as compatible.
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                "not-a-version",
                true,
                CliConstants.MINIMUM_REQUIRED_DISPATCHER_VERSION);

            Assert.That(state.NeedsUpdate, Is.True);
            Assert.That(state.IsCompatible, Is.False);
        }
    }
}
