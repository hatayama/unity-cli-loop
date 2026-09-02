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
                anyGameplayUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: true);

            Assert.That(suffix, Does.Contain("already down"));
        }

        [Test]
        public void BuildSuffix_WhenNotConsumedAndNoGameplayUpdateRan_ReportsNoGameplayUpdate()
        {
            // Verifies a stalled gameplay update loop is distinguished from a dropped event.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: null,
                anyGameplayUpdateObserved: false,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("no gameplay input update"));
        }

        [Test]
        public void BuildSuffix_WhenNotConsumedButGameplayUpdatesRan_ReportsDroppedEvent()
        {
            // Verifies the event-never-consumed case is distinguished from a stalled update loop.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: null,
                anyGameplayUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("not consumed by any recorded"));
        }

        [Test]
        public void BuildSuffix_WhenConsumedByEditorUpdate_ReportsEditorInvisibility()
        {
            // Verifies the Editor-update-consumed case names the reason gameplay could not see it.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: "Editor",
                anyGameplayUpdateObserved: false,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("Editor update"));
        }

        [Test]
        public void BuildSuffix_WhenConsumedByOtherUpdateType_NamesThatUpdateType()
        {
            // Verifies an unexpected consuming update type (e.g. Fixed) is surfaced by name.
            string suffix = PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                consumedByUpdateType: "Fixed",
                anyGameplayUpdateObserved: true,
                keyAlreadyPressedBeforeQueue: false);

            Assert.That(suffix, Does.Contain("Fixed update"));
        }
    }
}
