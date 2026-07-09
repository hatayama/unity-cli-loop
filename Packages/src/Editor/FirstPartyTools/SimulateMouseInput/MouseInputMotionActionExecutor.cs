#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes delta, scroll, and smooth-delta mouse input actions.
    /// </summary>
    internal static class MouseInputMotionActionExecutor
    {
        internal static async Task<SimulateMouseInputResponse> ExecuteMoveDelta(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            Vector2 delta = new(request.DeltaX, request.DeltaY);
            SimulateMouseInputOverlayState.SetMoveDelta(delta);

            InputSimulationWaitOutcome applyOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetDeltaState(mouse, delta), ct).ConfigureAwait(false);
            if (applyOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                MouseInputMainThreadCleanup.ScheduleTimedOutMouseOverlayCleanup();
                return MouseInputSimulationResponseFactory.TimedOutActionResult(
                    UnityCliLoopMouseInputAction.MoveDelta);
            }

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                SimulateMouseInputOverlayState.Clear();
                return MouseInputSimulationResponseFactory.InterruptedActionResult(
                    UnityCliLoopMouseInputAction.MoveDelta);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                MouseInputMainThreadCleanup.ScheduleTimedOutMouseOverlayCleanup();
                return MouseInputSimulationResponseFactory.TimedOutActionResult(
                    UnityCliLoopMouseInputAction.MoveDelta);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Mouse delta injected: ({request.DeltaX:F1}, {request.DeltaY:F1})",
                Action = UnityCliLoopMouseInputAction.MoveDelta.ToString()
            };
        }

        internal static async Task<SimulateMouseInputResponse> ExecuteScroll(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            Vector2 scroll = new(request.ScrollX, request.ScrollY);

            int scrollDir = request.ScrollY > 0f ? 1 : request.ScrollY < 0f ? -1 : 0;
            SimulateMouseInputOverlayState.SetScrollDirection(scrollDir);

            InputSimulationWaitOutcome applyOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetScrollState(mouse, scroll), ct).ConfigureAwait(false);
            if (applyOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                MouseInputMainThreadCleanup.ScheduleTimedOutMouseOverlayCleanup();
                return MouseInputSimulationResponseFactory.TimedOutActionResult(
                    UnityCliLoopMouseInputAction.Scroll);
            }

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                SimulateMouseInputOverlayState.Clear();
                return MouseInputSimulationResponseFactory.InterruptedActionResult(
                    UnityCliLoopMouseInputAction.Scroll);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                MouseInputMainThreadCleanup.ScheduleTimedOutMouseOverlayCleanup();
                return MouseInputSimulationResponseFactory.TimedOutActionResult(
                    UnityCliLoopMouseInputAction.Scroll);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Scroll injected: ({request.ScrollX:F1}, {request.ScrollY:F1})",
                Action = UnityCliLoopMouseInputAction.Scroll.ToString()
            };
        }

        // Distributes totalDelta across frames over duration for human-like smooth movement.
        // Uses ApplyOnNextConfiguredUpdate per frame so the delta is visible to game code
        // in the same Input System update cycle. Resets delta to zero only after the final frame.
        internal static async Task<SimulateMouseInputResponse> ExecuteSmoothDelta(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            if (request.Duration <= 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be positive for SmoothDelta, got: {request.Duration}",
                    Action = UnityCliLoopMouseInputAction.SmoothDelta.ToString()
                };
            }

            Vector2 totalDelta = new(request.DeltaX, request.DeltaY);
            float duration = request.Duration;
            float startTime = Time.realtimeSinceStartup;
            float previousT = 0f;

            while (true)
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);

                float elapsed = Time.realtimeSinceStartup - startTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float frameFraction = t - previousT;
                Vector2 frameDelta = totalDelta * frameFraction;
                SimulateMouseInputOverlayState.SetMoveDelta(frameDelta);

                InputSimulationWaitOutcome applyOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.InjectDelta(mouse, frameDelta), ct).ConfigureAwait(false);
                if (applyOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    MouseInputMainThreadCleanup.ScheduleTimedOutDeltaCleanup(mouse);
                    return MouseInputSimulationResponseFactory.TimedOutActionResult(
                        UnityCliLoopMouseInputAction.SmoothDelta);
                }

                previousT = t;
                InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForRuntimeFrames(1, ct)
                    .ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Paused)
                {
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                    MouseInputMainThreadCleanup.ResetDeltaIfPossible(mouse);
                    SimulateMouseInputOverlayState.Clear();
                    return MouseInputSimulationResponseFactory.InterruptedActionResult(
                        UnityCliLoopMouseInputAction.SmoothDelta);
                }

                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    MouseInputMainThreadCleanup.ScheduleTimedOutDeltaCleanup(mouse);
                    return MouseInputSimulationResponseFactory.TimedOutActionResult(
                        UnityCliLoopMouseInputAction.SmoothDelta);
                }

                if (t >= 1f)
                {
                    break;
                }
            }

            // Reset delta to zero after the smooth operation completes
            InputSimulationWaitOutcome resetOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.InjectDelta(mouse, Vector2.zero), ct).ConfigureAwait(false);
            if (resetOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                MouseInputMainThreadCleanup.ScheduleTimedOutDeltaCleanup(mouse);
                return MouseInputSimulationResponseFactory.TimedOutActionResult(
                    UnityCliLoopMouseInputAction.SmoothDelta);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Smooth delta ({request.DeltaX:F1}, {request.DeltaY:F1}) over {duration:F2}s",
                Action = UnityCliLoopMouseInputAction.SmoothDelta.ToString()
            };
        }
    }
}
#endif
