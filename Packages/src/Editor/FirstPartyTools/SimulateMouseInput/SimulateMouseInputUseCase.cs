#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Input System mouse simulation for the bundled simulate-mouse-input tool.
    /// </summary>
    public class SimulateMouseInputUseCase
    {
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<SimulateMouseInputResponse> ExecuteAsync(
            SimulateMouseInputSchema parameters,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new SimulateMouseInputResponse
            {
                Success = false,
                Message = "simulate-mouse-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = parameters.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            if (!EditorApplication.isPlaying)
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = parameters.Action.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before simulating mouse input.",
                    Action = parameters.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseInputAction), parameters.Action))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Invalid Action value: {(int)parameters.Action}. Use Click, LongPress, MoveDelta, Scroll, or SmoothDelta.",
                    Action = parameters.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseButton), parameters.Button))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Invalid Button value: {(int)parameters.Button}. Use Left, Right, or Middle.",
                    Action = parameters.Action.ToString()
                };
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = "No mouse device found in Input System. Ensure the Input System package is properly configured.",
                    Action = parameters.Action.ToString()
                };
            }

            UloopPausePointRegistry.ClearLatestHitSnapshot();

            VibeLogger.LogInfo(
                "simulate_mouse_input_start",
                "Mouse input simulation started",
                new { Action = parameters.Action.ToString(), Button = parameters.Button.ToString() },
                correlationId: correlationId
            );

            using InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();

            EnsureOverlayExists();

            SimulateMouseInputResponse response;

            switch (parameters.Action)
            {
                case UnityCliLoopMouseInputAction.Click:
                    response = await ExecuteClick(mouse, parameters, ct);
                    break;

                case UnityCliLoopMouseInputAction.LongPress:
                    response = await ExecuteLongPress(mouse, parameters, ct);
                    break;

                case UnityCliLoopMouseInputAction.MoveDelta:
                    response = await ExecuteMoveDelta(mouse, parameters, ct);
                    break;

                case UnityCliLoopMouseInputAction.Scroll:
                    response = await ExecuteScroll(mouse, parameters, ct);
                    break;

                case UnityCliLoopMouseInputAction.SmoothDelta:
                    response = await ExecuteSmoothDelta(mouse, parameters, ct);
                    break;

                default:
                    throw new ArgumentException($"Unknown mouse input action: {parameters.Action}");
            }

            VibeLogger.LogInfo(
                "simulate_mouse_input_complete",
                $"Mouse input simulation completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            return response;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static void EnsureOverlayExists()
        {
            OverlayCanvasFactory.EnsureExists();
        }

        // Input coordinates use top-left origin; Unity Screen space uses bottom-left origin.
        // Uses Screen.height (runtime resolution) because Mouse.current.position is in
        // runtime screen space, not the editor Game view target resolution.
        private static Vector2 InputToScreen(Vector2 inputPos)
        {
            return new Vector2(inputPos.x, Screen.height - inputPos.y);
        }

        private async Task<SimulateMouseInputResponse> ExecuteClick(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            if (request.Duration < 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be non-negative, got: {request.Duration}",
                    Action = UnityCliLoopMouseInputAction.Click.ToString()
                };
            }

            Vector2 inputPos = new(request.X, request.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            RuntimeMouseButton button = ToRuntimeMouseButton(request.Button);
            string buttonName = button.ToString();

            // Set mouse position before clicking
            InputSimulationWaitOutcome positionOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetPositionState(mouse, screenPos), ct).ConfigureAwait(false);
            if (positionOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutButtonResult(UnityCliLoopMouseInputAction.Click, buttonName, inputPos);
            }

            // Press button
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            MouseInputState.SetButtonDown(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            bool pressWasApplied = false;
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.SetButtonState(mouse, button, true), ct)
                    .ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Completed)
                {
                    pressWasApplied = true;
                    waitOutcome = await InputSystemUpdateHelper.WaitForPressLifetime(request.Duration, ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutButtonCleanup(mouse, button, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await ReleaseButtonIfPossible(mouse, button).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        ScheduleTimedOutButtonCleanup(mouse, button, false);
                    }
                    else
                    {
                        await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                        MouseInputState.SetButtonUp(button);
                    }
                }
                else
                {
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                    MouseInputState.SetButtonUp(button);
                }

                if (waitOutcome != InputSimulationWaitOutcome.TimedOut)
                {
                    if (waitOutcome == InputSimulationWaitOutcome.Paused)
                    {
                        SimulateMouseInputOverlayState.Clear();
                    }
                    else
                    {
                        SimulateMouseInputOverlayState.SetButtonHeld(button, false);
                    }
                }
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return InterruptedButtonResult(
                    UnityCliLoopMouseInputAction.Click,
                    buttonName,
                    inputPos);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutButtonResult(UnityCliLoopMouseInputAction.Click, buttonName, inputPos);
            }

            string durationText = request.Duration > 0f ? $" for {InputSimulationDurationFormatter.FormatSeconds(request.Duration)}s" : "";
            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Clicked {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}){durationText}",
                Action = UnityCliLoopMouseInputAction.Click.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteLongPress(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            if (request.Duration <= 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be positive for LongPress, got: {request.Duration}",
                    Action = UnityCliLoopMouseInputAction.LongPress.ToString()
                };
            }

            Vector2 inputPos = new(request.X, request.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            RuntimeMouseButton button = ToRuntimeMouseButton(request.Button);
            string buttonName = button.ToString();

            // Set mouse position before pressing
            InputSimulationWaitOutcome positionOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetPositionState(mouse, screenPos), ct).ConfigureAwait(false);
            if (positionOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutButtonResult(UnityCliLoopMouseInputAction.LongPress, buttonName, inputPos);
            }

            // Press button
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            MouseInputState.SetButtonDown(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            bool pressWasApplied = false;
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.SetButtonState(mouse, button, true), ct)
                    .ConfigureAwait(false);

                // Hold for at least the minimum observation frames so the press
                // is visible to game code, then continue until duration elapses.
                if (waitOutcome == InputSimulationWaitOutcome.Completed)
                {
                    pressWasApplied = true;
                    waitOutcome = await InputSystemUpdateHelper.WaitForPressLifetime(request.Duration, ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutButtonCleanup(mouse, button, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await ReleaseButtonIfPossible(mouse, button).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        ScheduleTimedOutButtonCleanup(mouse, button, false);
                    }
                    else
                    {
                        await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                        MouseInputState.SetButtonUp(button);
                    }
                }
                else
                {
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                    MouseInputState.SetButtonUp(button);
                }

                if (waitOutcome != InputSimulationWaitOutcome.TimedOut)
                {
                    if (waitOutcome == InputSimulationWaitOutcome.Paused)
                    {
                        SimulateMouseInputOverlayState.Clear();
                    }
                    else
                    {
                        SimulateMouseInputOverlayState.SetButtonHeld(button, false);
                    }
                }
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return InterruptedButtonResult(
                    UnityCliLoopMouseInputAction.LongPress,
                    buttonName,
                    inputPos);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutButtonResult(UnityCliLoopMouseInputAction.LongPress, buttonName, inputPos);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Long-pressed {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}) for {InputSimulationDurationFormatter.FormatSeconds(request.Duration)}s",
                Action = UnityCliLoopMouseInputAction.LongPress.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteMoveDelta(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            Vector2 delta = new(request.DeltaX, request.DeltaY);
            SimulateMouseInputOverlayState.SetMoveDelta(delta);

            InputSimulationWaitOutcome applyOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetDeltaState(mouse, delta), ct).ConfigureAwait(false);
            if (applyOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutMouseOverlayCleanup();
                return TimedOutActionResult(UnityCliLoopMouseInputAction.MoveDelta);
            }

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                SimulateMouseInputOverlayState.Clear();
                return InterruptedActionResult(UnityCliLoopMouseInputAction.MoveDelta);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutMouseOverlayCleanup();
                return TimedOutActionResult(UnityCliLoopMouseInputAction.MoveDelta);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Mouse delta injected: ({request.DeltaX:F1}, {request.DeltaY:F1})",
                Action = UnityCliLoopMouseInputAction.MoveDelta.ToString()
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteScroll(
            Mouse mouse, SimulateMouseInputSchema request, CancellationToken ct)
        {
            Vector2 scroll = new(request.ScrollX, request.ScrollY);

            int scrollDir = request.ScrollY > 0f ? 1 : request.ScrollY < 0f ? -1 : 0;
            SimulateMouseInputOverlayState.SetScrollDirection(scrollDir);

            InputSimulationWaitOutcome applyOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetScrollState(mouse, scroll), ct).ConfigureAwait(false);
            if (applyOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutMouseOverlayCleanup();
                return TimedOutActionResult(UnityCliLoopMouseInputAction.Scroll);
            }

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                SimulateMouseInputOverlayState.Clear();
                return InterruptedActionResult(UnityCliLoopMouseInputAction.Scroll);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutMouseOverlayCleanup();
                return TimedOutActionResult(UnityCliLoopMouseInputAction.Scroll);
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
        private async Task<SimulateMouseInputResponse> ExecuteSmoothDelta(
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
                    ScheduleTimedOutDeltaCleanup(mouse);
                    return TimedOutActionResult(UnityCliLoopMouseInputAction.SmoothDelta);
                }

                previousT = t;
                InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForRuntimeFrames(1, ct)
                    .ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Paused)
                {
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                    ResetDeltaIfPossible(mouse);
                    SimulateMouseInputOverlayState.Clear();
                    return InterruptedActionResult(UnityCliLoopMouseInputAction.SmoothDelta);
                }

                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutDeltaCleanup(mouse);
                    return TimedOutActionResult(UnityCliLoopMouseInputAction.SmoothDelta);
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
                ScheduleTimedOutDeltaCleanup(mouse);
                return TimedOutActionResult(UnityCliLoopMouseInputAction.SmoothDelta);
            }

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Smooth delta ({request.DeltaX:F1}, {request.DeltaY:F1}) over {duration:F2}s",
                Action = UnityCliLoopMouseInputAction.SmoothDelta.ToString()
            };
        }

        private static SimulateMouseInputResponse InterruptedButtonResult(
            UnityCliLoopMouseInputAction action,
            string buttonName,
            Vector2 inputPos)
        {
            SimulateMouseInputResponse result = InterruptedActionResult(action);
            result.Button = buttonName;
            result.PositionX = inputPos.x;
            result.PositionY = inputPos.y;
            return result;
        }

        private static SimulateMouseInputResponse InterruptedActionResult(
            UnityCliLoopMouseInputAction action)
        {
            SimulateMouseInputResponse result = new()
            {
                Success = true,
                Message = "Mouse input stopped because Unity paused during Pause Point inspection. Unity CLI Loop released its held input bookkeeping.",
                Action = action.ToString(),
                InterruptedByPausePoint = true
            };
            AttachPausePointHit(result);
            return result;
        }

        private static SimulateMouseInputResponse TimedOutButtonResult(
            UnityCliLoopMouseInputAction action,
            string buttonName,
            Vector2 inputPos)
        {
            SimulateMouseInputResponse result = TimedOutActionResult(action);
            result.Button = buttonName;
            result.PositionX = inputPos.x;
            result.PositionY = inputPos.y;
            return result;
        }

        private static SimulateMouseInputResponse TimedOutActionResult(
            UnityCliLoopMouseInputAction action)
        {
            return new SimulateMouseInputResponse
            {
                Success = false,
                Message = "Mouse input timed out while waiting for Unity Editor update. Cleanup is queued for the next Editor tick.",
                Action = action.ToString()
            };
        }

        private static void AttachPausePointHit(SimulateMouseInputResponse result)
        {
            if (result == null)
            {
                Debug.Assert(false, "result must not be null");
                return;
            }

            UloopPausePointSnapshot? snapshot = UloopPausePointRegistry.GetLatestHitSnapshot();
            if (snapshot == null)
            {
                return;
            }

            if (!snapshot.IsHit)
            {
                return;
            }

            string? snapshotId = snapshot.Id;
            if (string.IsNullOrEmpty(snapshotId))
            {
                return;
            }

            result.PausePointId = snapshotId;
            result.PausePointHitCount = snapshot.HitCount;
            result.PausePointHits = CollectPausePointHits();
        }

        // One input can hit several markers in the same frame; the representative
        // PausePointId alone forced agents into extra status calls to find the others.
        private static List<UnityCliLoopPausePointHit> CollectPausePointHits()
        {
            List<UnityCliLoopPausePointHit> hits = new();
            foreach (UloopPausePointSnapshot snapshot in UloopPausePointRegistry.GetHitSnapshots())
            {
                if (!snapshot.IsHit || string.IsNullOrEmpty(snapshot.Id))
                {
                    continue;
                }
                hits.Add(new UnityCliLoopPausePointHit
                {
                    Id = snapshot.Id,
                    HitCount = snapshot.HitCount
                });
            }
            return hits;
        }

        private static async Task<InputSimulationWaitOutcome> ReleaseButtonIfPossible(Mouse mouse, RuntimeMouseButton button)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
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
                CancellationToken.None).ConfigureAwait(false);
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

        private static void ResetDeltaIfPossible(Mouse mouse)
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

        private static void ScheduleTimedOutButtonCleanup(Mouse mouse, RuntimeMouseButton button, bool pressWasApplied)
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
                await ReleaseButtonIfPossible(mouse, button).ConfigureAwait(false);
            }

            MouseInputState.SetButtonUp(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, false);
        }

        private static void ScheduleTimedOutMouseOverlayCleanup()
        {
            CleanupTimedOutMouseOverlayAsync(CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutMouseOverlayAsync(CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            SimulateMouseInputOverlayState.Clear();
        }

        private static void ScheduleTimedOutDeltaCleanup(Mouse mouse)
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

        private static RuntimeMouseButton ToRuntimeMouseButton(UnityCliLoopMouseButton button)
        {
            switch (button)
            {
                case UnityCliLoopMouseButton.Right:
                    return RuntimeMouseButton.Right;
                case UnityCliLoopMouseButton.Middle:
                    return RuntimeMouseButton.Middle;
                default:
                    Debug.Assert(button == UnityCliLoopMouseButton.Left, $"Unexpected mouse button value: {button}");
                    return RuntimeMouseButton.Left;
            }
        }
#endif
    }
}
