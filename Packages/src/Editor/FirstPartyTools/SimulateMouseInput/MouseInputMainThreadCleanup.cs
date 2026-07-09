#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Restores mouse device and overlay state on the Unity main thread after simulation ends.
    /// </summary>
    internal static class MouseInputMainThreadCleanup
    {
        internal static async Task<InputSimulationWaitOutcome> ReleaseButtonIfPossible(
            Mouse mouse,
            RuntimeMouseButton button,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            if (!CanInjectMouseState(mouse))
            {
                return InputSimulationWaitOutcome.Completed;
            }

            if (EditorApplication.isPaused)
            {
                ReleaseButtonImmediately(mouse, button);
                return InputSimulationWaitOutcome.Completed;
            }

            InputSimulationWaitOutcome releaseOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetButtonState(mouse, button, false),
                ct).ConfigureAwait(false);
            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleReleaseButtonImmediately(mouse, button);
            }

            return releaseOutcome;
        }

        private static void ScheduleReleaseButtonImmediately(Mouse mouse, RuntimeMouseButton button)
        {
            ReleaseButtonImmediatelyOnMainThreadAsync(mouse, button, CancellationToken.None).Forget();
        }

        private static async Task ReleaseButtonImmediatelyOnMainThreadAsync(
            Mouse mouse,
            RuntimeMouseButton button,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            ReleaseButtonImmediately(mouse, button);
        }

        private static void ReleaseButtonImmediately(Mouse mouse, RuntimeMouseButton button)
        {
            Debug.Assert(CanInjectMouseState(mouse), "mouse button can only be released while PlayMode has a mouse");
            if (!CanInjectMouseState(mouse))
            {
                return;
            }

            MouseInputState.SetButtonState(mouse, button, false);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
        }

        internal static void ResetDeltaIfPossible(Mouse mouse)
        {
            if (!CanInjectMouseState(mouse))
            {
                return;
            }

            MouseInputState.InjectDelta(mouse, Vector2.zero);
            if (EditorApplication.isPaused)
            {
                InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
            }
        }

        internal static void ScheduleTimedOutButtonCleanup(
            Mouse mouse,
            RuntimeMouseButton button,
            bool pressWasApplied)
        {
            CleanupTimedOutButtonAsync(mouse, button, pressWasApplied, CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutButtonAsync(
            Mouse mouse,
            RuntimeMouseButton button,
            bool pressWasApplied,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            if (pressWasApplied)
            {
                await ReleaseButtonIfPossible(mouse, button, ct).ConfigureAwait(false);
            }

            MouseInputState.SetButtonUp(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, false);
        }

        internal static void ScheduleTimedOutMouseOverlayCleanup()
        {
            CleanupTimedOutMouseOverlayAsync(CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutMouseOverlayAsync(CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            SimulateMouseInputOverlayState.Clear();
        }

        internal static void ScheduleTimedOutDeltaCleanup(Mouse mouse)
        {
            CleanupTimedOutDeltaAsync(mouse, CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutDeltaAsync(Mouse mouse, CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            ResetDeltaIfPossible(mouse);
            SimulateMouseInputOverlayState.Clear();
        }

        private static bool CanInjectMouseState(Mouse mouse)
        {
            return EditorApplication.isPlaying && mouse != null;
        }
    }
}
#endif
