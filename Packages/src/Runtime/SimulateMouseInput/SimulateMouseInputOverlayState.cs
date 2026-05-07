#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Simulate Mouse Input Overlay State operations for its owning module.
    /// </summary>
    public sealed class SimulateMouseInputOverlayStateService
    {
        private const float BUTTON_MIN_DISPLAY_DURATION = 0.05f;

        private bool _leftButtonHeld;
        private bool _rightButtonHeld;
        private bool _middleButtonHeld;
        private float _leftButtonActiveUntil;
        private float _rightButtonActiveUntil;
        private float _middleButtonActiveUntil;

        public bool IsLeftButtonHeld =>
            _leftButtonHeld || Time.realtimeSinceStartup < _leftButtonActiveUntil;
        public bool IsRightButtonHeld =>
            _rightButtonHeld || Time.realtimeSinceStartup < _rightButtonActiveUntil;
        public bool IsMiddleButtonHeld =>
            _middleButtonHeld || Time.realtimeSinceStartup < _middleButtonActiveUntil;

        private const float SCROLL_DISPLAY_DURATION = 0.05f;
        private int _scrollDirection;
        private float _scrollActiveUntil;

        public int ScrollDirection =>
            _scrollDirection != 0 && Time.realtimeSinceStartup < _scrollActiveUntil
                ? _scrollDirection
                : 0;

        private const float MOVE_DISPLAY_DURATION = 0.3f;
        private const int MOVE_SAMPLE_FRAMES = 5;
        private Vector2 _moveDelta;
        private Vector2 _moveAccumulator;
        private int _moveFrameCount;
        private float _moveActiveUntil;

        public Vector2 MoveDelta =>
            _moveDelta != Vector2.zero && Time.realtimeSinceStartup < _moveActiveUntil
                ? _moveDelta
                : Vector2.zero;

        public float LastActivityTime { get; private set; }

        public bool HasAnyActivity =>
            IsLeftButtonHeld || IsRightButtonHeld || IsMiddleButtonHeld
            || ScrollDirection != 0 || MoveDelta != Vector2.zero;

        public void SetButtonHeld(MouseButton button, bool held)
        {
            // When releasing, set a minimum display time so short clicks are always visible
            float activeUntil = held ? 0f : Time.realtimeSinceStartup + BUTTON_MIN_DISPLAY_DURATION;

            switch (button)
            {
                case MouseButton.Left:
                    _leftButtonHeld = held;
                    if (!held) _leftButtonActiveUntil = activeUntil;
                    break;
                case MouseButton.Right:
                    _rightButtonHeld = held;
                    if (!held) _rightButtonActiveUntil = activeUntil;
                    break;
                case MouseButton.Middle:
                    _middleButtonHeld = held;
                    if (!held) _middleButtonActiveUntil = activeUntil;
                    break;
                default:
                    Debug.Assert(false, $"Unexpected MouseButton value: {button}");
                    break;
            }

            LastActivityTime = Time.realtimeSinceStartup;
        }

        public void SetScrollDirection(int direction)
        {
            Debug.Assert(direction >= -1 && direction <= 1, $"direction must be -1, 0, or 1, got: {direction}");
            _scrollDirection = direction;
            _scrollActiveUntil = Time.realtimeSinceStartup + SCROLL_DISPLAY_DURATION;
            LastActivityTime = Time.realtimeSinceStartup;
        }

        public void ClearScroll()
        {
            _scrollActiveUntil = 0f;
        }

        public void SetMoveDelta(Vector2 delta)
        {
            _moveAccumulator += delta;
            _moveFrameCount++;

            // Update direction every frame from accumulated delta so single-call
            // MoveDelta actions are visible, while the accumulator reset every N
            // frames smooths out per-frame integer quantization noise.
            if (_moveAccumulator != Vector2.zero)
            {
                _moveDelta = _moveAccumulator.normalized;
            }

            if (_moveFrameCount >= MOVE_SAMPLE_FRAMES)
            {
                _moveAccumulator = Vector2.zero;
                _moveFrameCount = 0;
            }

            _moveActiveUntil = Time.realtimeSinceStartup + MOVE_DISPLAY_DURATION;
            LastActivityTime = Time.realtimeSinceStartup;
        }

        public void Clear()
        {
            _leftButtonHeld = false;
            _rightButtonHeld = false;
            _middleButtonHeld = false;
            _leftButtonActiveUntil = 0f;
            _rightButtonActiveUntil = 0f;
            _middleButtonActiveUntil = 0f;
            _scrollDirection = 0;
            _scrollActiveUntil = 0f;
            _moveDelta = Vector2.zero;
            _moveAccumulator = Vector2.zero;
            _moveFrameCount = 0;
            _moveActiveUntil = 0f;
            LastActivityTime = 0f;
        }
    }

    /// <summary>
    /// Stores Simulate Mouse Input Overlay state shared by the owning workflow.
    /// </summary>
    public static class SimulateMouseInputOverlayState
    {
        private static readonly SimulateMouseInputOverlayStateService ServiceValue =
            new SimulateMouseInputOverlayStateService();

        public static bool IsLeftButtonHeld => ServiceValue.IsLeftButtonHeld;
        public static bool IsRightButtonHeld => ServiceValue.IsRightButtonHeld;
        public static bool IsMiddleButtonHeld => ServiceValue.IsMiddleButtonHeld;
        public static int ScrollDirection => ServiceValue.ScrollDirection;
        public static Vector2 MoveDelta => ServiceValue.MoveDelta;
        public static float LastActivityTime => ServiceValue.LastActivityTime;
        public static bool HasAnyActivity => ServiceValue.HasAnyActivity;

        public static void SetButtonHeld(MouseButton button, bool held)
        {
            ServiceValue.SetButtonHeld(button, held);
        }

        public static void SetScrollDirection(int direction)
        {
            ServiceValue.SetScrollDirection(direction);
        }

        public static void ClearScroll()
        {
            ServiceValue.ClearScroll();
        }

        public static void SetMoveDelta(Vector2 delta)
        {
            ServiceValue.SetMoveDelta(delta);
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
