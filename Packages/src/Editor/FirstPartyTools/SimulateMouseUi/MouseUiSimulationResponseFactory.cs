#nullable enable
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates wire-visible responses for mouse UI simulation outcomes.
    /// </summary>
    internal static class MouseUiSimulationResponseFactory
    {
        internal static SimulateMouseUiResponse CreateFailure(
            MouseUiSimulationCommand parameters,
            string message)
        {
            return new SimulateMouseUiResponse
            {
                Success = false,
                Message = message,
                Action = parameters.Action.ToString()
            };
        }

        internal static SimulateMouseUiResponse CreateFrameTimeoutResult(
            MouseAction action,
            Vector2 position,
            Vector2? endPosition,
            string? hitGameObjectName)
        {
            return new SimulateMouseUiResponse
            {
                Success = false,
                Message = $"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for an editor frame.",
                Action = action.ToString(),
                HitGameObjectName = hitGameObjectName,
                PositionX = position.x,
                PositionY = position.y,
                EndPositionX = endPosition.HasValue ? endPosition.Value.x : null,
                EndPositionY = endPosition.HasValue ? endPosition.Value.y : null
            };
        }

        internal static SimulateMouseUiResponse CreateClickResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            string? targetName,
            bool hitTarget)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = hitTarget
                    ? parameters.BypassRaycast
                        ? $"Bypass-clicked '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}'"
                        : $"Clicked '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1})"
                    : $"Clicked at ({inputPos.x:F1}, {inputPos.y:F1}) - no UI element hit",
                Action = MouseAction.Click.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        internal static SimulateMouseUiResponse CreateLongPressResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            string? targetName,
            bool hitTarget)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = hitTarget
                    ? parameters.BypassRaycast
                        ? $"Bypass-long-pressed '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}' for {parameters.Duration:F1}s"
                        : $"Long-pressed '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s"
                    : $"Long-pressed at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s - no UI element hit",
                Action = MouseAction.LongPress.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        internal static SimulateMouseUiResponse CreateDragResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputStart,
            Vector2 inputEnd,
            string targetName)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = parameters.BypassRaycast
                    ? $"Bypass-dragged '{targetName}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) via '{parameters.TargetPath}' at {parameters.DragSpeed:F0} px/s"
                    : $"Dragged '{targetName}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.Drag.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputStart.x,
                PositionY = inputStart.y,
                EndPositionX = inputEnd.x,
                EndPositionY = inputEnd.y
            };
        }

        internal static SimulateMouseUiResponse CreateDragEndResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputEnd,
            string targetName)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag ended on '{targetName}' at ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.DragEnd.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputEnd.x,
                PositionY = inputEnd.y
            };
        }
    }
}
