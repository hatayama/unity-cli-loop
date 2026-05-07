#nullable enable
using System;
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
    public class SimulateMouseInputUseCase : IUnityCliLoopMouseInputSimulationService
    {
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<UnityCliLoopMouseInputSimulationResult> SimulateMouseInputAsync(
            UnityCliLoopMouseInputSimulationRequest request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = false,
                Message = "simulate-mouse-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = request.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            if (!EditorApplication.isPlaying)
            {
                return new UnityCliLoopMouseInputSimulationResult
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = request.Action.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new UnityCliLoopMouseInputSimulationResult
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before simulating mouse input.",
                    Action = request.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseInputAction), request.Action))
            {
                return new UnityCliLoopMouseInputSimulationResult
                {
                    Success = false,
                    Message = $"Invalid Action value: {(int)request.Action}. Use Click, LongPress, MoveDelta, Scroll, or SmoothDelta.",
                    Action = request.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseButton), request.Button))
            {
                return new UnityCliLoopMouseInputSimulationResult
                {
                    Success = false,
                    Message = $"Invalid Button value: {(int)request.Button}. Use Left, Right, or Middle.",
                    Action = request.Action.ToString()
                };
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return new UnityCliLoopMouseInputSimulationResult
                {
                    Success = false,
                    Message = "No mouse device found in Input System. Ensure the Input System package is properly configured.",
                    Action = request.Action.ToString()
                };
            }

            VibeLogger.LogInfo(
                "simulate_mouse_input_start",
                "Mouse input simulation started",
                new { Action = request.Action.ToString(), Button = request.Button.ToString() },
                correlationId: correlationId
            );

            EnsureOverlayExists();

            UnityCliLoopMouseInputSimulationResult response;

            switch (request.Action)
            {
                case UnityCliLoopMouseInputAction.Click:
                    response = await ExecuteClick(mouse, request, ct);
                    break;

                case UnityCliLoopMouseInputAction.LongPress:
                    response = await ExecuteLongPress(mouse, request, ct);
                    break;

                case UnityCliLoopMouseInputAction.MoveDelta:
                    response = await ExecuteMoveDelta(mouse, request, ct);
                    break;

                case UnityCliLoopMouseInputAction.Scroll:
                    response = await ExecuteScroll(mouse, request, ct);
                    break;

                case UnityCliLoopMouseInputAction.SmoothDelta:
                    response = await ExecuteSmoothDelta(mouse, request, ct);
                    break;

                default:
                    throw new ArgumentException($"Unknown mouse input action: {request.Action}");
            }

            VibeLogger.LogInfo(
                "simulate_mouse_input_complete",
                $"Mouse input simulation completed: {response.Message}",
                new { Action = request.Action.ToString(), Success = response.Success },
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

        private async Task<UnityCliLoopMouseInputSimulationResult> ExecuteClick(
            Mouse mouse, UnityCliLoopMouseInputSimulationRequest request, CancellationToken ct)
        {
            if (request.Duration < 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new UnityCliLoopMouseInputSimulationResult
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
            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetPositionState(mouse, screenPos), ct);

            // Press button
            MouseInputState.SetButtonDown(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            bool pressWasApplied = false;

            try
            {
                await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.SetButtonState(mouse, button, true), ct);
                pressWasApplied = true;
                await InputSystemUpdateHelper.WaitForPressLifetime(request.Duration, ct);
            }
            finally
            {
                if (pressWasApplied)
                {
                    await ReleaseButtonIfPossible(mouse, button);
                    MouseInputState.SetButtonUp(button);
                }
                else
                {
                    MouseInputState.SetButtonUp(button);
                }
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }

            string durationText = request.Duration > 0f ? $" for {request.Duration:F1}s" : "";
            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = true,
                Message = $"Clicked {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}){durationText}",
                Action = UnityCliLoopMouseInputAction.Click.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<UnityCliLoopMouseInputSimulationResult> ExecuteLongPress(
            Mouse mouse, UnityCliLoopMouseInputSimulationRequest request, CancellationToken ct)
        {
            if (request.Duration <= 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new UnityCliLoopMouseInputSimulationResult
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
            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetPositionState(mouse, screenPos), ct);

            // Press button
            MouseInputState.SetButtonDown(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            bool pressWasApplied = false;

            try
            {
                await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.SetButtonState(mouse, button, true), ct);
                pressWasApplied = true;

                // Hold for at least the minimum observation frames so the press
                // is visible to game code, then continue until duration elapses.
                await InputSystemUpdateHelper.WaitForPressLifetime(request.Duration, ct);
            }
            finally
            {
                if (pressWasApplied)
                {
                    await ReleaseButtonIfPossible(mouse, button);
                }
                MouseInputState.SetButtonUp(button);
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }

            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = true,
                Message = $"Long-pressed {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}) for {request.Duration:F1}s",
                Action = UnityCliLoopMouseInputAction.LongPress.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<UnityCliLoopMouseInputSimulationResult> ExecuteMoveDelta(
            Mouse mouse, UnityCliLoopMouseInputSimulationRequest request, CancellationToken ct)
        {
            Vector2 delta = new(request.DeltaX, request.DeltaY);
            SimulateMouseInputOverlayState.SetMoveDelta(delta);

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetDeltaState(mouse, delta), ct);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = true,
                Message = $"Mouse delta injected: ({request.DeltaX:F1}, {request.DeltaY:F1})",
                Action = UnityCliLoopMouseInputAction.MoveDelta.ToString()
            };
        }

        private async Task<UnityCliLoopMouseInputSimulationResult> ExecuteScroll(
            Mouse mouse, UnityCliLoopMouseInputSimulationRequest request, CancellationToken ct)
        {
            Vector2 scroll = new(request.ScrollX, request.ScrollY);

            int scrollDir = request.ScrollY > 0f ? 1 : request.ScrollY < 0f ? -1 : 0;
            SimulateMouseInputOverlayState.SetScrollDirection(scrollDir);

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetScrollState(mouse, scroll), ct);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = true,
                Message = $"Scroll injected: ({request.ScrollX:F1}, {request.ScrollY:F1})",
                Action = UnityCliLoopMouseInputAction.Scroll.ToString()
            };
        }

        // Distributes totalDelta across frames over duration for human-like smooth movement.
        // Uses ApplyOnNextConfiguredUpdate per frame so the delta is visible to game code
        // in the same Input System update cycle. Resets delta to zero only after the final frame.
        private async Task<UnityCliLoopMouseInputSimulationResult> ExecuteSmoothDelta(
            Mouse mouse, UnityCliLoopMouseInputSimulationRequest request, CancellationToken ct)
        {
            if (request.Duration <= 0f || float.IsNaN(request.Duration) || float.IsInfinity(request.Duration))
            {
                return new UnityCliLoopMouseInputSimulationResult
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
                float elapsed = Time.realtimeSinceStartup - startTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float frameFraction = t - previousT;
                Vector2 frameDelta = totalDelta * frameFraction;
                SimulateMouseInputOverlayState.SetMoveDelta(frameDelta);

                await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => MouseInputState.InjectDelta(mouse, frameDelta), ct);

                previousT = t;

                if (t >= 1f)
                {
                    break;
                }

                // Explicit/manual update completes synchronously, so an extra
                // frame delay is needed to prevent the loop from collapsing into
                // a single burst. Dynamic/Fixed modes already yield naturally via
                // ApplyOnNextConfiguredUpdate's onBeforeUpdate callback.
                if (InputUpdateTypeResolver.RequiresExplicitUpdate())
                {
                    await EditorDelay.DelayFrame(1, ct);
                }
            }

            // Reset delta to zero after the smooth operation completes
            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.InjectDelta(mouse, Vector2.zero), ct);

            return new UnityCliLoopMouseInputSimulationResult
            {
                Success = true,
                Message = $"Smooth delta ({request.DeltaX:F1}, {request.DeltaY:F1}) over {duration:F2}s",
                Action = UnityCliLoopMouseInputAction.SmoothDelta.ToString()
            };
        }

        private static async Task ReleaseButtonIfPossible(Mouse mouse, RuntimeMouseButton button)
        {
            if (!CanInjectMouseState(mouse))
            {
                return;
            }

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetButtonState(mouse, button, false), CancellationToken.None);
        }

        private static bool CanInjectMouseState(Mouse mouse)
        {
            return EditorApplication.isPlaying && !EditorApplication.isPaused && Mouse.current == mouse;
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
