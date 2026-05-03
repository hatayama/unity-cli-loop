#nullable enable
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    public static class SimulateKeyboardOverlayState
    {
        private static readonly List<string> _heldKeys = new();
        private static string? _pressKey;
        private static float _pressReleasedTime = -1f;

        public static bool IsActive => _heldKeys.Count > 0 || _pressKey != null;
        public static IReadOnlyList<string> HeldKeys => _heldKeys;
        public static string? PressKey => _pressKey;
        public static bool IsPressHeld => _pressKey != null && _pressReleasedTime < 0f;
        public static float PressReleasedTime => _pressReleasedTime;

        public static void AddHeldKey(string keyName)
        {
            if (!_heldKeys.Contains(keyName))
            {
                _heldKeys.Add(keyName);
            }
        }

        public static void RemoveHeldKey(string keyName)
        {
            _heldKeys.Remove(keyName);
        }

        public static void ShowPress(string keyName)
        {
            _pressKey = keyName;
            _pressReleasedTime = -1f;
        }

        public static void ReleasePress()
        {
            UnityEngine.Debug.Assert(_pressKey != null, "Press key must exist before it can be released.");
            if (_pressKey == null)
            {
                return;
            }

            _pressReleasedTime = UnityEngine.Time.realtimeSinceStartup;
        }

        public static void ClearPress()
        {
            _pressKey = null;
            _pressReleasedTime = -1f;
        }

        public static void Clear()
        {
            _heldKeys.Clear();
            _pressKey = null;
            _pressReleasedTime = -1f;
        }
    }
}
