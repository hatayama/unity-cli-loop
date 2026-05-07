#nullable enable
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Simulate Keyboard Overlay State operations for its owning module.
    /// </summary>
    public sealed class SimulateKeyboardOverlayStateService
    {
        private readonly List<string> _heldKeys = new List<string>();
        private string? _pressKey;
        private float _pressReleasedTime = -1f;

        public bool IsActive => _heldKeys.Count > 0 || _pressKey != null;
        public IReadOnlyList<string> HeldKeys => _heldKeys;
        public string? PressKey => _pressKey;
        public bool IsPressHeld => _pressKey != null && _pressReleasedTime < 0f;
        public float PressReleasedTime => _pressReleasedTime;

        public void AddHeldKey(string keyName)
        {
            if (!_heldKeys.Contains(keyName))
            {
                _heldKeys.Add(keyName);
            }
        }

        public void RemoveHeldKey(string keyName)
        {
            _heldKeys.Remove(keyName);
        }

        public void ShowPress(string keyName)
        {
            _pressKey = keyName;
            _pressReleasedTime = -1f;
        }

        public void ReleasePress()
        {
            UnityEngine.Debug.Assert(_pressKey != null, "Press key must exist before it can be released.");
            if (_pressKey == null)
            {
                return;
            }

            _pressReleasedTime = UnityEngine.Time.realtimeSinceStartup;
        }

        public void ClearPress()
        {
            _pressKey = null;
            _pressReleasedTime = -1f;
        }

        public void Clear()
        {
            _heldKeys.Clear();
            _pressKey = null;
            _pressReleasedTime = -1f;
        }
    }

    /// <summary>
    /// Stores Simulate Keyboard Overlay state shared by the owning workflow.
    /// </summary>
    public static class SimulateKeyboardOverlayState
    {
        private static readonly SimulateKeyboardOverlayStateService ServiceValue =
            new SimulateKeyboardOverlayStateService();

        public static bool IsActive => ServiceValue.IsActive;
        public static IReadOnlyList<string> HeldKeys => ServiceValue.HeldKeys;
        public static string? PressKey => ServiceValue.PressKey;
        public static bool IsPressHeld => ServiceValue.IsPressHeld;
        public static float PressReleasedTime => ServiceValue.PressReleasedTime;

        public static void AddHeldKey(string keyName)
        {
            ServiceValue.AddHeldKey(keyName);
        }

        public static void RemoveHeldKey(string keyName)
        {
            ServiceValue.RemoveHeldKey(keyName);
        }

        public static void ShowPress(string keyName)
        {
            ServiceValue.ShowPress(keyName);
        }

        public static void ReleasePress()
        {
            ServiceValue.ReleasePress();
        }

        public static void ClearPress()
        {
            ServiceValue.ClearPress();
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
