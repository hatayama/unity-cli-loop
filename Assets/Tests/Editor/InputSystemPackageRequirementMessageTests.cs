#nullable enable
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies missing Input System package warning text shared by first-party input tools.
    /// </summary>
    [TestFixture]
    public sealed class InputSystemPackageRequirementMessageTests
    {
        [TestCase(
            "simulate-keyboard",
            "simulate-keyboard requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.")]
        [TestCase(
            "simulate-mouse-input",
            "simulate-mouse-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.")]
        [TestCase(
            "record-input",
            "record-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.")]
        [TestCase(
            "replay-input",
            "replay-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.")]
        public void Format_WithToolName_ReturnsExistingWarningMessage(string toolName, string expected)
        {
            // Verifies the shared formatter preserves the existing wire-visible warning text.
            string result = InputSystemPackageRequirementMessage.Format(toolName);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
