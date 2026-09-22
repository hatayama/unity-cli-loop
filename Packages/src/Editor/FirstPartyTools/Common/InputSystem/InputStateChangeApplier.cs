#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes simulated input state into a device while suppressing the Input System's messageless
    /// assertion raised when an input callback removes a state monitor, and counts every suppression.
    /// </summary>
    internal sealed class InputStateChangeApplierService
    {
        // Monotonic so callers can diff the value across one command instead of threading a result
        // back through every apply call site.
        public int SuppressedAssertionCount { get; private set; }

        private readonly InputSystemMonitorRemovalAssertionOrigin _assertionOrigin =
            new InputSystemMonitorRemovalAssertionOrigin();

        public void Apply(InputDevice device, InputEventPtr eventPtr, InputUpdateType updateType)
        {
            Debug.Assert(device != null, "device must not be null");

            ILogHandler original = Debug.unityLogger.logHandler;
            // Nested apply keeps the outer filter; wrapping twice would restore the wrong handler.
            if (original is InputSystemMonitorRemovalAssertionLogFilter)
            {
                InputState.Change(device, eventPtr, updateType);
                return;
            }

            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(
                original,
                _assertionOrigin.IsCurrentAssertionFromMonitorRemoval);
            Debug.unityLogger.logHandler = filter;
            try
            {
                InputState.Change(device, eventPtr, updateType);
            }
            finally
            {
                // The log handler is global state, so it must be restored even when a callback throws.
                Debug.unityLogger.logHandler = original;
                SuppressedAssertionCount += filter.SuppressedCount;
            }
        }
    }

    /// <summary>
    /// Shared entry point that every uloop input simulation path uses to apply device state.
    /// </summary>
    internal static class InputStateChangeApplier
    {
        private static readonly InputStateChangeApplierService ServiceValue = new InputStateChangeApplierService();

        public static int SuppressedAssertionCount => ServiceValue.SuppressedAssertionCount;

        public static void Apply(InputDevice device, InputEventPtr eventPtr, InputUpdateType updateType)
        {
            ServiceValue.Apply(device, eventPtr, updateType);
        }
    }
}
#endif
