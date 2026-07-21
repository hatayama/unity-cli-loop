using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the KeyStateAfterPauseInterruption regression harness.
    // Its only job is to give simulate-keyboard/pause-point scenarios a stable, unconditionally
    // executing line to arm a pause-point marker on (see docs/regression-harness.md).
    public sealed class SpaceHoldPoller : MonoBehaviour
    {
        public bool IsSpaceHeld { get; private set; }

        private void Update()
        {
            IsSpaceHeld = Keyboard.current != null && Keyboard.current[Key.Space].isPressed;
        }
    }
}
