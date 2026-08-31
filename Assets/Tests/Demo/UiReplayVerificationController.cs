#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    // Verification controller for UI-based record/replay.
    // Logs mouse position, button states, and UI interaction results
    // to compare recording vs replay for EventSystem-driven scenes.
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class UiReplayVerificationController : ReplayVerificationControllerBase
    {
        [SerializeField] private Text? _mousePositionText;
        [SerializeField] private Text? _counterText;

        private Vector2 _lastLoggedPosition;
        private bool _lastLeftButton;
        private bool _lastRightButton;
        private string _lastCounterValue = "";
        private DropZone[] _dropZones = null!;
        private string[] _lastDropTexts = null!;

        protected override void Start()
        {
            base.Start();

            _dropZones = FindObjectsByType<DropZone>(FindObjectsSortMode.InstanceID);
            _lastDropTexts = new string[_dropZones.Length];
        }

        protected override bool TryActivateFromInput()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return false;
            }
            Vector2 pos = mouse.position.ReadValue();
            return pos.x >= 0 && pos.x <= Screen.width && pos.y >= 0 && pos.y <= Screen.height;
        }

        protected override void RecordEvents(int relativeFrame)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            LogMousePosition(mouse, relativeFrame);
            LogMouseButtons(mouse, relativeFrame);
            LogCounterText(relativeFrame);
            LogDropZoneTexts(relativeFrame);
            UpdateUI(mouse);
        }

        protected override void ResetState()
        {
            base.ResetState();
            _lastLoggedPosition = Vector2.zero;
            _lastLeftButton = false;
            _lastRightButton = false;
            _lastCounterValue = "";
            if (_lastDropTexts != null)
            {
                for (int i = 0; i < _lastDropTexts.Length; i++)
                {
                    _lastDropTexts[i] = "";
                }
            }
        }

        private void LogMousePosition(Mouse mouse, int frame)
        {
            Vector2 position = RoundVector2(mouse.position.ReadValue());
            if (position == _lastLoggedPosition)
            {
                return;
            }

            _lastLoggedPosition = position;
            EventLog.Add($"Frame {frame}: MousePos {FormatVector2(position)}");
        }

        private void LogMouseButtons(Mouse mouse, int frame)
        {
            bool left = mouse.leftButton.isPressed;
            bool right = mouse.rightButton.isPressed;

            if (left != _lastLeftButton)
            {
                _lastLeftButton = left;
                EventLog.Add($"Frame {frame}: LeftButton {(left ? "Down" : "Up")}");
            }

            if (right != _lastRightButton)
            {
                _lastRightButton = right;
                EventLog.Add($"Frame {frame}: RightButton {(right ? "Down" : "Up")}");
            }
        }

        private void LogCounterText(int frame)
        {
            if (_counterText == null)
            {
                return;
            }

            string current = _counterText.text;
            if (current == _lastCounterValue)
            {
                return;
            }

            _lastCounterValue = current;
            EventLog.Add($"Frame {frame}: Counter \"{current}\"");
        }

        private void LogDropZoneTexts(int frame)
        {
            for (int i = 0; i < _dropZones.Length; i++)
            {
                string current = _dropZones[i].StatusMessage;
                if (current == _lastDropTexts[i])
                {
                    continue;
                }

                _lastDropTexts[i] = current;
                EventLog.Add($"Frame {frame}: DropZone \"{current}\"");
            }
        }

        private void UpdateUI(Mouse mouse)
        {
            if (_mousePositionText != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                _mousePositionText.text = $"Mouse: {FormatVector2(pos)}";
            }
        }
    }
}
#endif
