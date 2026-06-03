using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests user-facing input-simulation duration formatting.
    /// </summary>
    public sealed class InputSimulationDurationFormatterTests
    {
        [Test]
        public void FormatSeconds_WhenDurationHasSubTenthPrecision_PreservesMeaningfulDigits()
        {
            // Verifies short requested durations are not rounded up to a misleading tenth of a second.
            string formatted = InputSimulationDurationFormatter.FormatSeconds(0.05f);

            Assert.That(formatted, Is.EqualTo("0.05"));
        }

        [Test]
        public void FormatSeconds_WhenDurationUsesWholeSeconds_DropsTrailingDecimal()
        {
            // Verifies whole-second durations stay compact in command result messages.
            string formatted = InputSimulationDurationFormatter.FormatSeconds(2f);

            Assert.That(formatted, Is.EqualTo("2"));
        }
    }
}
