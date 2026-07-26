#nullable enable
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates mouse UI simulation preconditions and request options.
    /// </summary>
    internal static class MouseUiSimulationValidator
    {
        internal static SimulateMouseUiResponse? ValidateSimulationStart(
            MouseUiSimulationCommand parameters,
            EventSystem? eventSystem,
            string pausedActionDescription)
        {
            PlayModeToolPreflightResult playModeResult =
                PlayModeToolPreflightService.RequireActiveAndNotPaused(pausedActionDescription);
            if (!playModeResult.IsValid)
            {
                return MouseUiSimulationResponseFactory.CreatePreflightFailure(parameters, playModeResult);
            }

            if (eventSystem == null)
            {
                return MouseUiSimulationResponseFactory.CreateFailure(
                    parameters,
                    "No EventSystem found in the scene. Ensure an EventSystem GameObject exists.");
            }

            return ValidateSimulationRequestOptions(parameters);
        }

        internal static SimulateMouseUiResponse? ValidateActiveDragState(MouseUiSimulationCommand parameters)
        {
            if (!MouseDragState.IsDragging || !RequiresIdlePointer(parameters.Action))
            {
                return null;
            }

            return MouseUiSimulationResponseFactory.CreateFailure(
                parameters,
                $"Cannot {parameters.Action.ToString()} while a split drag is active. Call DragEnd first.");
        }

        private static SimulateMouseUiResponse? ValidateSimulationRequestOptions(
            MouseUiSimulationCommand parameters)
        {
            if (parameters.Action != MouseAction.Click && parameters.Action != MouseAction.LongPress && parameters.DragSpeed < 0f)
            {
                return MouseUiSimulationResponseFactory.CreateFailure(parameters, $"DragSpeed must be non-negative, got: {parameters.DragSpeed}");
            }

            if (IsDragAction(parameters.Action) && parameters.Button != MouseButton.Left)
            {
                return MouseUiSimulationResponseFactory.CreateFailure(
                    parameters,
                    $"Drag actions only support Left button (uGUI ignores non-left drags), got: {parameters.Button}");
            }

            if (parameters.BypassRaycast &&
                RequiresBypassTargetPath(parameters.Action) &&
                string.IsNullOrWhiteSpace(parameters.TargetPath))
            {
                return MouseUiSimulationResponseFactory.CreateFailure(
                    parameters,
                    "TargetPath is required when BypassRaycast is true for Click, LongPress, Drag, or DragStart.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.DropTargetPath) &&
                parameters.Action != MouseAction.Drag &&
                parameters.Action != MouseAction.DragEnd)
            {
                return MouseUiSimulationResponseFactory.CreateFailure(parameters, "DropTargetPath supports Drag and DragEnd only.");
            }

            return null;
        }

        private static bool RequiresIdlePointer(MouseAction action)
        {
            return action == MouseAction.Click || action == MouseAction.Drag || action == MouseAction.LongPress;
        }

        private static bool RequiresBypassTargetPath(MouseAction action)
        {
            return action == MouseAction.Click
                || action == MouseAction.LongPress
                || action == MouseAction.Drag
                || action == MouseAction.DragStart;
        }

        private static bool IsDragAction(MouseAction action)
        {
            return action == MouseAction.Drag
                || action == MouseAction.DragStart
                || action == MouseAction.DragMove
                || action == MouseAction.DragEnd;
        }
    }
}
