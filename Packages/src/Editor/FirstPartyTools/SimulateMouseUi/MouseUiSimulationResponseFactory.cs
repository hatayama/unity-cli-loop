#nullable enable
using System.Collections.Generic;
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

        // Message must state whether the pointer event was already dispatched, since AI
        // callers rely on this text to decide whether the click/press actually reached the
        // target instead of only its overlay animation being interrupted.
        internal static SimulateMouseUiResponse CreateInterruptedResult(
            MouseAction action,
            Vector2 position,
            string? hitGameObjectName,
            string message)
        {
            SimulateMouseUiResponse result = new()
            {
                Success = true,
                Message = message,
                Action = action.ToString(),
                HitGameObjectName = hitGameObjectName,
                PositionX = position.x,
                PositionY = position.y,
                InterruptedByPausePoint = true
            };
            AttachPausePointHit(result);
            return result;
        }

        private static void AttachPausePointHit(SimulateMouseUiResponse result)
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
    }
}
