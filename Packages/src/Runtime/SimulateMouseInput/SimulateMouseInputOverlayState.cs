#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public static class SimulateMouseInputOverlayState
    {
        private const float BUTTON_MIN_DISPLAY_DURATION = 0.05f;

        private static bool _leftButtonHeld;
        private static bool _rightButtonHeld;
        private static bool _middleButtonHeld;
        private static float _leftButtonActiveUntil;
        private static float _rightButtonActiveUntil;
        private static float _middleButtonActiveUntil;

        public static bool IsLeftButtonHeld =>
            _leftButtonHeld || Time.realtimeSinceStartup < _leftButtonActiveUntil;
        public static bool IsRightButtonHeld =>
            _rightButtonHeld || Time.realtimeSinceStartup < _rightButtonActiveUntil;
        public static bool IsMiddleButtonHeld =>
            _middleButtonHeld || Time.realtimeSinceStartup < _middleButtonActiveUntil;

        private const float SCROLL_DISPLAY_DURATION = 0.05f;
        private static int _scrollDirection;
        private static float _scrollActiveUntil;

        public static int ScrollDirection =>
            _scrollDirection != 0 && Time.realtimeSinceStartup < _scrollActiveUntil
                ? _scrollDirection
                : 0;

        private const float MOVE_DISPLAY_DURATION = 0.3f;
        private const int MOVE_SAMPLE_FRAMES = 5;
        private static Vector2 _moveDelta;
        private static Vector2 _moveAccumulator;
        private static int _moveFrameCount;
        private static float _moveActiveUntil;

        public static Vector2 MoveDelta =>
            _moveDelta != Vector2.zero && Time.realtimeSinceStartup < _moveActiveUntil
                ? _moveDelta
                : Vector2.zero;

        public static float LastActivityTime { get; private set; }

        public static bool HasAnyActivity =>
            IsLeftButtonHeld || IsRightButtonHeld || IsMiddleButtonHeld
            || ScrollDirection != 0 || MoveDelta != Vector2.zero;

        public static void SetButtonHeld(MouseButton button, bool held)
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

        public static void SetScrollDirection(int direction)
        {
            Debug.Assert(direction >= -1 && direction <= 1, $"direction must be -1, 0, or 1, got: {direction}");
            _scrollDirection = direction;
            _scrollActiveUntil = Time.realtimeSinceStartup + SCROLL_DISPLAY_DURATION;
            LastActivityTime = Time.realtimeSinceStartup;
        }

        public static void ClearScroll()
        {
            _scrollActiveUntil = 0f;
        }

        public static void SetMoveDelta(Vector2 delta)
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

        public static void Clear()
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
}
