#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Characterization tests for InputReplayEventProcessor loop-restart held-state clearing.
    /// </summary>
    public class InputReplayEventProcessorCharacterizationTests
    {
        /// <summary>
        /// Pins ReleaseAllHeldInputs clearing virtual mouse position so loop restart matches pre-split ResetUiReplayState.
        /// </summary>
        [Test]
        public void ReleaseAllHeldInputs_WhenMousePositionWasSet_ShouldClearVirtualMousePosition()
        {
            InputReplayEventProcessor processor = new();
            Vector2 unusedDelta = Vector2.zero;
            Vector2 unusedScroll = Vector2.zero;
            processor.ProcessEvent(
                new RecordedInputEvent
                {
                    Type = InputEventTypes.MOUSE_POSITION,
                    Data = InputRecorder.FormatVector2(new Vector2(120f, 340f))
                },
                ref unusedDelta,
                ref unusedScroll);

            Assert.That(processor.MousePosition, Is.EqualTo(new Vector2(120f, 340f)));

            processor.ReleaseAllHeldInputs();

            Assert.That(processor.MousePosition, Is.Null);
        }
    }
}
#endif
