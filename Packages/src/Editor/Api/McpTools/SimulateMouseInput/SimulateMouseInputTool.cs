#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace io.github.hatayama.UnityCliLoop
{
    [McpTool(Description = "Simulate mouse input in PlayMode via Input System. Injects button clicks, mouse delta, and scroll wheel directly into Mouse.current for game logic that reads Input System (e.g. WasPressedThisFrame). Requires the Input System package.")]
    public class SimulateMouseInputTool : AbstractUnityTool<SimulateMouseInputSchema, SimulateMouseInputResponse>
    {
        public override string ToolName => "simulate-mouse-input";

        protected override
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
            async Task<SimulateMouseInputResponse> ExecuteAsync(
            SimulateMouseInputSchema parameters,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new SimulateMouseInputResponse
            {
                Success = false,
                Message = "simulate-mouse-input requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = parameters.Action.ToString()
            };
#else
            string correlationId = McpConstants.GenerateCorrelationId();

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

            if (!System.Enum.IsDefined(typeof(MouseInputAction), parameters.Action))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Invalid Action value: {(int)parameters.Action}. Use Click, LongPress, MoveDelta, Scroll, or SmoothDelta.",
                    Action = parameters.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(MouseButton), parameters.Button))
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

            VibeLogger.LogInfo(
                "simulate_mouse_input_start",
                "Mouse input simulation started",
                new { Action = parameters.Action.ToString(), Button = parameters.Button.ToString() },
                correlationId: correlationId
            );

            EnsureOverlayExists();

            SimulateMouseInputResponse response;

            switch (parameters.Action)
            {
                case MouseInputAction.Click:
                    response = await ExecuteClick(mouse, parameters, ct);
                    break;

                case MouseInputAction.LongPress:
                    response = await ExecuteLongPress(mouse, parameters, ct);
                    break;

                case MouseInputAction.MoveDelta:
                    response = await ExecuteMoveDelta(mouse, parameters, ct);
                    break;

                case MouseInputAction.Scroll:
                    response = await ExecuteScroll(mouse, parameters, ct);
                    break;

                case MouseInputAction.SmoothDelta:
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
            Mouse mouse, SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            if (parameters.Duration < 0f || float.IsNaN(parameters.Duration) || float.IsInfinity(parameters.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be non-negative, got: {parameters.Duration}",
                    Action = MouseInputAction.Click.ToString()
                };
            }

            Vector2 inputPos = new Vector2(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            MouseButton button = parameters.Button;
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
                await InputSystemUpdateHelper.WaitForPressLifetime(parameters.Duration, ct);
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

            string durationText = parameters.Duration > 0f ? $" for {parameters.Duration:F1}s" : "";
            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Clicked {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}){durationText}",
                Action = MouseInputAction.Click.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteLongPress(
            Mouse mouse, SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            if (parameters.Duration <= 0f || float.IsNaN(parameters.Duration) || float.IsInfinity(parameters.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be positive for LongPress, got: {parameters.Duration}",
                    Action = MouseInputAction.LongPress.ToString()
                };
            }

            Vector2 inputPos = new Vector2(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            MouseButton button = parameters.Button;
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
                await InputSystemUpdateHelper.WaitForPressLifetime(parameters.Duration, ct);
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

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Long-pressed {buttonName} at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s",
                Action = MouseInputAction.LongPress.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteMoveDelta(
            Mouse mouse, SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            Vector2 delta = new Vector2(parameters.DeltaX, parameters.DeltaY);
            SimulateMouseInputOverlayState.SetMoveDelta(delta);

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetDeltaState(mouse, delta), ct);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Mouse delta injected: ({parameters.DeltaX:F1}, {parameters.DeltaY:F1})",
                Action = MouseInputAction.MoveDelta.ToString()
            };
        }

        private async Task<SimulateMouseInputResponse> ExecuteScroll(
            Mouse mouse, SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            Vector2 scroll = new Vector2(parameters.ScrollX, parameters.ScrollY);

            int scrollDir = parameters.ScrollY > 0f ? 1 : parameters.ScrollY < 0f ? -1 : 0;
            SimulateMouseInputOverlayState.SetScrollDirection(scrollDir);

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => MouseInputState.SetScrollState(mouse, scroll), ct);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Scroll injected: ({parameters.ScrollX:F1}, {parameters.ScrollY:F1})",
                Action = MouseInputAction.Scroll.ToString()
            };
        }

        // Distributes totalDelta across frames over duration for human-like smooth movement.
        // Uses ApplyOnNextConfiguredUpdate per frame so the delta is visible to game code
        // in the same Input System update cycle. Resets delta to zero only after the final frame.
        private async Task<SimulateMouseInputResponse> ExecuteSmoothDelta(
            Mouse mouse, SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            if (parameters.Duration <= 0f || float.IsNaN(parameters.Duration) || float.IsInfinity(parameters.Duration))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Duration must be positive for SmoothDelta, got: {parameters.Duration}",
                    Action = MouseInputAction.SmoothDelta.ToString()
                };
            }

            Vector2 totalDelta = new Vector2(parameters.DeltaX, parameters.DeltaY);
            float duration = parameters.Duration;
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

            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = $"Smooth delta ({parameters.DeltaX:F1}, {parameters.DeltaY:F1}) over {duration:F2}s",
                Action = MouseInputAction.SmoothDelta.ToString()
            };
        }

        private static async Task ReleaseButtonIfPossible(Mouse mouse, MouseButton button)
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
#endif
    }
}
