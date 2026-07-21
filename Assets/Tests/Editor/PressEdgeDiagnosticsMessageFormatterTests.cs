#nullable enable
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure unit tests for the press-edge-miss diagnostic message suffix.
    /// </summary>
    public sealed class PressEdgeDiagnosticsMessageFormatterTests
    {
        [Test]
        public void BuildSuffix_WhenKeyAlreadyPressedBeforeQueue_ReportsNoTransition()
        {
            // Verifies a pre-existing down state (no edge possible) is reported over other causes.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: "Dynamic",
                anyDynamicUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: true);

            Assert.That(suffix, Does.Contain("already down"));
        }

        [Test]
        public void BuildSuffix_WhenNotConsumedAndNoDynamicUpdateRan_ReportsNoDynamicUpdate()
        {
            // Verifies a stalled Dynamic update loop is distinguished from a dropped event.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: null,
                anyDynamicUpdateObserved: false,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("no Dynamic update ran"));
        }

        [Test]
        public void BuildSuffix_WhenNotConsumedButDynamicUpdatesRan_ReportsDroppedEvent()
        {
            // Verifies the event-never-consumed case is distinguished from a stalled update loop.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: null,
                anyDynamicUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("not consumed by any recorded"));
        }

        [Test]
        public void BuildSuffix_WhenConsumedByEditorUpdate_ReportsEditorInvisibility()
        {
            // Verifies the Editor-update-consumed case names the reason gameplay could not see it.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: "Editor",
                anyDynamicUpdateObserved: false,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("Editor update"));
        }

        [Test]
        public void BuildSuffix_WhenConsumedByOtherUpdateType_NamesThatUpdateType()
        {
            // Verifies an unexpected consuming update type (e.g. Fixed) is surfaced by name.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: "Fixed",
                anyDynamicUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("Fixed update"));
        }
    }
}
