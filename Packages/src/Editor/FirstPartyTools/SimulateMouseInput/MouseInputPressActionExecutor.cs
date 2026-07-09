#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes click and long-press mouse input actions.
    /// </summary>
    internal static class MouseInputPressActionExecutor
    {
        // Input coordinates use top-left origin; Unity Screen space uses bottom-left origin.
        // Uses Screen.height (runtime resolution) because Mouse.current.position is in
        // runtime screen space, not the editor Game view target resolution.
        private static Vector2 InputToScreen(Vector2 inputPos)
        {
            return new Vector2(inputPos.x, Screen.height - inputPos.y);
        }

        internal static async Task<SimulateMouseInputResponse> ExecuteClick(
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
                return MouseInputSimulationResponseFactory.TimedOutButtonResult(
                    UnityCliLoopMouseInputAction.Click,
                    buttonName,
                    inputPos);
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
                    MouseInputMainThreadCleanup.ScheduleTimedOutButtonCleanup(mouse, button, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await MouseInputMainThreadCleanup.ReleaseButtonIfPossible(
                            mouse,
                            button,
                            CancellationToken.None).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        MouseInputMainThreadCleanup.ScheduleTimedOutButtonCleanup(mouse, button, false);
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
                return MouseInputSimulationResponseFactory.InterruptedButtonResult(
                    UnityCliLoopMouseInputAction.Click,
                    buttonName,
                    inputPos);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return MouseInputSimulationResponseFactory.TimedOutButtonResult(
                    UnityCliLoopMouseInputAction.Click,
                    buttonName,
                    inputPos);
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

        internal static async Task<SimulateMouseInputResponse> ExecuteLongPress(
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
                return MouseInputSimulationResponseFactory.TimedOutButtonResult(
                    UnityCliLoopMouseInputAction.LongPress,
                    buttonName,
                    inputPos);
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
                    MouseInputMainThreadCleanup.ScheduleTimedOutButtonCleanup(mouse, button, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await MouseInputMainThreadCleanup.ReleaseButtonIfPossible(
                            mouse,
                            button,
                            CancellationToken.None).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        MouseInputMainThreadCleanup.ScheduleTimedOutButtonCleanup(mouse, button, false);
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
                return MouseInputSimulationResponseFactory.InterruptedButtonResult(
                    UnityCliLoopMouseInputAction.LongPress,
                    buttonName,
                    inputPos);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return MouseInputSimulationResponseFactory.TimedOutButtonResult(
                    UnityCliLoopMouseInputAction.LongPress,
                    buttonName,
                    inputPos);
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
    }
}
#endif
