using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies setup compatibility decisions for the global uloop command.
    /// </summary>
    public class CliSetupCompatibilityTests
    {
        // Why: fixed test value keeps compat logic assertions independent of the shipping pin version.
        private const string TEST_MINIMUM_DISPATCHER_VERSION = "3.0.0";

        [TestCase(null, false, false, false)]
        [TestCase("2.1.10", false, true, false)]
        [TestCase("3.0.0", false, true, false)]
        [TestCase("2.1.10", true, true, false)]
        [TestCase("3.0.0", true, false, true)]
        [TestCase("3.0.1", true, false, true)]
        public void Evaluate_ReturnsExpectedDispatcherSetupState(
            string cliVersion,
            bool isDispatcher,
            bool expectedNeedsUpdate,
            bool expectedCompatible)
        {
            // Verifies setup prompts are based on dispatcher identity and minimum dispatcher version.
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                isDispatcher,
                TEST_MINIMUM_DISPATCHER_VERSION);

            Assert.That(state.NeedsUpdate, Is.EqualTo(expectedNeedsUpdate));
            Assert.That(state.IsCompatible, Is.EqualTo(expectedCompatible));
            Assert.That(state.NeedsUpdate && state.IsCompatible, Is.False);
        }

        [Test]
        public void Evaluate_WhenDispatcherVersionIsInvalid_RequestsUpdate()
        {
            // Verifies malformed dispatcher versions fail closed instead of being treated as compatible.
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                "not-a-version",
                true,
                TEST_MINIMUM_DISPATCHER_VERSION);

            Assert.That(state.NeedsUpdate, Is.True);
            Assert.That(state.IsCompatible, Is.False);
        }
    }
}
