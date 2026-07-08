#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes click and long-press mouse UI actions.
    /// </summary>
    internal static class MouseUiPressActionExecutor
    {
        internal static async Task<SimulateMouseUiResponse> ExecuteClick(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            MouseUiMainThreadCleanupScheduler cleanupScheduler,
            CancellationToken ct)
        {
            Vector2 inputPos = new(parameters.X, parameters.Y);
            Vector2 screenPos = MouseUiCoordinateConverter.InputToScreen(inputPos);
            PointerEventData pointerData = MouseUiPointerTargetResolver.CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                MouseUiPointerTargetResolver.ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.Click);
            if (resolvedTargets.FailureResponse != null)
            {
                return resolvedTargets.FailureResponse;
            }

            if (parameters.BypassRaycast && resolvedTargets.Target == null)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"TargetPath '{parameters.TargetPath}' has no pointer click or pointer down handler.",
                    Action = MouseAction.Click.ToString(),
                    PositionX = inputPos.x,
                    PositionY = inputPos.y
                };
            }

            string? targetName = resolvedTargets.Target?.name;
            bool hitTarget = resolvedTargets.Target != null;
            SimulateMouseUiOverlayState.Update(
                MouseAction.Click, inputPos, null,
                targetName, Handles.GetMainGameViewSize());

            bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Click, inputPos, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            // Fire click events after expand animation so the user sees where the click lands
            ExecutePointerClickEvents(resolvedTargets, pointerData);

            bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateClickResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateClickResult(parameters, inputPos, targetName, hitTarget);
        }

        internal static async Task<SimulateMouseUiResponse> ExecuteLongPress(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            MouseUiMainThreadCleanupScheduler cleanupScheduler,
            CancellationToken ct)
        {
            if (parameters.Duration <= 0f || float.IsNaN(parameters.Duration) || float.IsInfinity(parameters.Duration))
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"Duration must be positive, got: {parameters.Duration}",
                    Action = MouseAction.LongPress.ToString()
                };
            }

            Vector2 inputPos = new(parameters.X, parameters.Y);
            Vector2 screenPos = MouseUiCoordinateConverter.InputToScreen(inputPos);
            PointerEventData pointerData = MouseUiPointerTargetResolver.CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                MouseUiPointerTargetResolver.ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.LongPress);
            if (resolvedTargets.FailureResponse != null)
            {
                return resolvedTargets.FailureResponse;
            }

            if (parameters.BypassRaycast && resolvedTargets.Target == null)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"TargetPath '{parameters.TargetPath}' has no pointer down or pointer click handler.",
                    Action = MouseAction.LongPress.ToString(),
                    PositionX = inputPos.x,
                    PositionY = inputPos.y
                };
            }

            string? targetName = resolvedTargets.Target?.name;
            bool hitTarget = resolvedTargets.Target != null;
            bool shouldReleasePointer = resolvedTargets.RawTarget != null && resolvedTargets.Target != null;
            SimulateMouseUiOverlayState.Update(
                MouseAction.LongPress, inputPos, null,
                targetName, Handles.GetMainGameViewSize());

            bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            ExecuteLongPressPointerDown(resolvedTargets, pointerData);

            try
            {
                // Hold for Duration seconds, updating elapsed time each frame for overlay display
                float startTime = Time.realtimeSinceStartup;
                float elapsed = 0f;
                while (elapsed < parameters.Duration)
                {
                    SimulateMouseUiOverlayState.UpdateLongPressElapsed(elapsed);
                    bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                    if (!frameReady)
                    {
                        cleanupScheduler.QueueOverlayClear();
                        return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
                    }
                    await MainThreadSwitcher.SwitchToMainThread(ct);
                    elapsed = Time.realtimeSinceStartup - startTime;
                }
                SimulateMouseUiOverlayState.UpdateLongPressElapsed(parameters.Duration);
            }
            finally
            {
                // Ensure pointerUp fires even if the hold loop is cancelled
                if (shouldReleasePointer)
                {
                    cleanupScheduler.ExecuteCleanupOnMainThread(
                        () => ExecuteEvents.Execute(resolvedTargets.Target!, pointerData, ExecuteEvents.pointerUpHandler));
                }
            }

            bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
        }

        private static void ExecutePointerClickEvents(
            ResolvedPointerTargets resolvedTargets,
            PointerEventData pointerData)
        {
            if (resolvedTargets.RawTarget == null)
            {
                return;
            }

            if (resolvedTargets.PressTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(
                    resolvedTargets.RawTarget,
                    pointerData,
                    ExecuteEvents.pointerDownHandler);
            }

            if (resolvedTargets.Target != null)
            {
                ExecuteEvents.Execute(
                    resolvedTargets.Target,
                    pointerData,
                    ExecuteEvents.pointerUpHandler);
            }

            if (resolvedTargets.ClickTarget != null)
            {
                ExecuteEvents.Execute(
                    resolvedTargets.ClickTarget,
                    pointerData,
                    ExecuteEvents.pointerClickHandler);
            }
        }

        private static void ExecuteLongPressPointerDown(
            ResolvedPointerTargets resolvedTargets,
            PointerEventData pointerData)
        {
            if (resolvedTargets.RawTarget == null || resolvedTargets.Target == null)
            {
                return;
            }

            ExecuteEvents.ExecuteHierarchy(
                resolvedTargets.RawTarget,
                pointerData,
                ExecuteEvents.pointerDownHandler);
        }
    }
}
