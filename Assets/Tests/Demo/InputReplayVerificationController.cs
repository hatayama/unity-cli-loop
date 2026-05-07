#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    // Deterministic controller for verifying record/replay accuracy.
    // Uses fixed per-frame movement (no deltaTime) to ensure identical
    // results between recording and replay at the same frame rate.
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class InputReplayVerificationController : ReplayVerificationControllerBase
    {
        private const float MOVE_SPEED = 0.1f;
        private const float ROTATE_SENSITIVITY = 0.5f;
        private const float SCALE_STEP = 0.1f;

        [SerializeField] private Text? _frameText;
        [SerializeField] private Text? _positionText;
        [SerializeField] private Text? _rotationText;
        [SerializeField] private Text? _scaleText;
        [SerializeField] private Text? _inputText;
        [SerializeField] private MeshRenderer? _cubeRenderer;

        private Vector3 _initialPosition;
        private Vector3 _initialEulerAngles;
        private Vector3 _lastLoggedPosition;
        private bool _colorToggleRed;
        private bool _colorToggleBlue;

        protected override void Start()
        {
            base.Start();
            Debug.Assert(_cubeRenderer != null, "_cubeRenderer must be assigned in scene");

            _initialPosition = transform.position;
            _initialEulerAngles = transform.eulerAngles;
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
            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (keyboard == null || mouse == null)
            {
                return;
            }

            ProcessMovement(keyboard, relativeFrame);
            ProcessRotation(mouse, relativeFrame);
            ProcessClicks(mouse, relativeFrame);
            ProcessScroll(mouse, relativeFrame);
            UpdateUI(keyboard, mouse, relativeFrame);
        }

        protected override void ResetState()
        {
            base.ResetState();
            transform.position = _initialPosition;
            transform.eulerAngles = _initialEulerAngles;
            transform.localScale = Vector3.one;
            _colorToggleRed = false;
            _colorToggleBlue = false;
            UpdateCubeColor();
            _lastLoggedPosition = _initialPosition;
        }

        private void ProcessMovement(Keyboard keyboard, int frame)
        {
            Vector3 movement = Vector3.zero;

            if (keyboard[Key.W].isPressed) movement.z += MOVE_SPEED;
            if (keyboard[Key.S].isPressed) movement.z -= MOVE_SPEED;
            if (keyboard[Key.A].isPressed) movement.x -= MOVE_SPEED;
            if (keyboard[Key.D].isPressed) movement.x += MOVE_SPEED;

            if (movement == Vector3.zero)
            {
                return;
            }

            transform.Translate(movement, Space.World);

            Vector3 rounded = RoundVector3(transform.position);
            if (rounded != _lastLoggedPosition)
            {
                _lastLoggedPosition = rounded;
                EventLog.Add($"Frame {frame}: Position {FormatVector3(rounded)}");
            }
        }

        private void ProcessRotation(Mouse mouse, int frame)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if (delta == Vector2.zero)
            {
                return;
            }

            float rotationY = delta.x * ROTATE_SENSITIVITY;
            Vector3 euler = transform.eulerAngles;
            euler.y += rotationY;
            transform.eulerAngles = euler;

            EventLog.Add($"Frame {frame}: Rotation Y={euler.y.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        private void ProcessClicks(Mouse mouse, int frame)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _colorToggleRed = !_colorToggleRed;
                UpdateCubeColor();
                EventLog.Add($"Frame {frame}: LeftClick color={GetColorName()}");
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _colorToggleBlue = !_colorToggleBlue;
                UpdateCubeColor();
                EventLog.Add($"Frame {frame}: RightClick color={GetColorName()}");
            }
        }

        private void ProcessScroll(Mouse mouse, int frame)
        {
            float scrollY = mouse.scroll.y.ReadValue();
            if (scrollY == 0f)
            {
                return;
            }

            float direction = scrollY > 0f ? SCALE_STEP : -SCALE_STEP;
            Vector3 scale = transform.localScale;
            float newScale = Mathf.Max(0.1f, scale.x + direction);
            transform.localScale = Vector3.one * newScale;

            EventLog.Add($"Frame {frame}: Scroll scale={newScale.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        private void UpdateCubeColor()
        {
            if (_cubeRenderer == null)
            {
                return;
            }

            Color color = Color.white;
            if (_colorToggleRed) color = Color.red;
            if (_colorToggleBlue) color = Color.blue;
            if (_colorToggleRed && _colorToggleBlue) color = Color.magenta;

            _cubeRenderer.material.color = color;
        }

        private string GetColorName()
        {
            if (_colorToggleRed && _colorToggleBlue) return "Magenta";
            if (_colorToggleRed) return "Red";
            if (_colorToggleBlue) return "Blue";
            return "White";
        }

        private void UpdateUI(Keyboard keyboard, Mouse mouse, int frame)
        {
            if (_frameText != null) _frameText.text = $"Frame: {frame}";
            if (_positionText != null) _positionText.text = $"Pos: {FormatVector3(transform.position)}";
            if (_rotationText != null) _rotationText.text = $"Rot Y: {transform.eulerAngles.y:F2}";
            if (_scaleText != null) _scaleText.text = $"Scale: {transform.localScale.x:F2}";
            if (_inputText != null) _inputText.text = BuildInputStateText(keyboard, mouse);
        }

        private static string BuildInputStateText(Keyboard keyboard, Mouse mouse)
        {
            List<string> held = new();
            if (keyboard[Key.W].isPressed) held.Add("W");
            if (keyboard[Key.A].isPressed) held.Add("A");
            if (keyboard[Key.S].isPressed) held.Add("S");
            if (keyboard[Key.D].isPressed) held.Add("D");
            if (mouse.leftButton.isPressed) held.Add("LMB");
            if (mouse.rightButton.isPressed) held.Add("RMB");

            return held.Count > 0 ? $"Input: [{string.Join(", ", held)}]" : "Input: [none]";
        }
    }
}
#endif
