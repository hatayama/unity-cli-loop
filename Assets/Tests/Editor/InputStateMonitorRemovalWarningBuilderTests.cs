#if ULOOP_HAS_INPUT_SYSTEM
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests how the monitor-removal warning is appended to an existing response warning.
    /// </summary>
    public class InputStateMonitorRemovalWarningBuilderTests
    {
        /// <summary>
        /// Verifies an unchanged suppression count keeps a non-empty existing warning as is.
        /// </summary>
        [Test]
        public void Append_WhenCountUnchanged_ReturnsExistingWarning()
        {
            string result = InputStateMonitorRemovalWarningBuilder.Append("existing", 3, 3);

            Assert.That(result, Is.EqualTo("existing"));
        }

        /// <summary>
        /// Verifies an unchanged suppression count keeps an empty existing warning empty.
        /// </summary>
        [Test]
        public void Append_WhenCountUnchangedAndNoExistingWarning_ReturnsEmpty()
        {
            string result = InputStateMonitorRemovalWarningBuilder.Append("", 3, 3);

            Assert.That(result, Is.EqualTo(""));
        }

        /// <summary>
        /// Verifies a suppression with no existing warning yields only the monitor-removal warning.
        /// </summary>
        [Test]
        public void Append_WhenCountIncreasedAndNoExistingWarning_ReturnsMonitorRemovalWarning()
        {
            string result = InputStateMonitorRemovalWarningBuilder.Append("", 3, 4);

            Assert.That(result, Is.EqualTo(InputStateMonitorRemovalWarningBuilder.MonitorRemovalWarning));
        }

        /// <summary>
        /// Verifies a suppression appends the monitor-removal warning after an existing warning with a space.
        /// </summary>
        [Test]
        public void Append_WhenCountIncreasedWithExistingWarning_AppendsAfterSpace()
        {
            string result = InputStateMonitorRemovalWarningBuilder.Append("existing", 3, 4);

            Assert.That(result, Is.EqualTo("existing " + InputStateMonitorRemovalWarningBuilder.MonitorRemovalWarning));
        }
    }
}
#endif
