using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the KeyStateAfterPauseInterruption regression harness.
    // The marker line only executes while Space is actually held, so arming a pause-point
    // there before simulate-keyboard Press starts does not fire immediately — it fires
    // naturally from game code once Press drives the key down (see docs/regression-harness.md).
    public sealed class SpaceHoldPoller : MonoBehaviour
    {
        public bool IsSpaceHeld { get; private set; }

        private void Update()
        {
            bool isHeld = Keyboard.current != null && Keyboard.current[Key.Space].isPressed;
            if (isHeld)
            {
                IsSpaceHeld = isHeld;
            }
        }

        /// <summary>
        /// Clears the sticky IsSpaceHeld latch so a post-resume probe can detect a fresh press edge.
        /// Kept below Update so the harness pause-point line number (19) stays stable.
        /// </summary>
        public void ClearStickyHeld()
        {
            IsSpaceHeld = false;
        }
    }
}
