#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop
{
    // Reads actual mouse input and drives SimulateMouseInputOverlayState
    // so the overlay can be tested standalone without the CLI tool pipeline.
    public class DemoMouseInputOverlayTester : MonoBehaviour
    {
        private void Update()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Left, true);
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Left, false);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Right, true);
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Right, false);
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Middle, true);
            }
            if (mouse.middleButton.wasReleasedThisFrame)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(MouseButton.Middle, false);
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude > 0.01f)
            {
                SimulateMouseInputOverlayState.SetMoveDelta(delta);
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (scrollY > 0f)
            {
                SimulateMouseInputOverlayState.SetScrollDirection(1);
            }
            else if (scrollY < 0f)
            {
                SimulateMouseInputOverlayState.SetScrollDirection(-1);
            }
        }

        private void OnDisable()
        {
            SimulateMouseInputOverlayState.Clear();
        }
    }
}
#endif
