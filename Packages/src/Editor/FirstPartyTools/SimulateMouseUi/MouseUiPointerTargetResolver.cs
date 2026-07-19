#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves pointer, hierarchy path, and drop targets for mouse UI simulation.
    /// </summary>
    internal static class MouseUiPointerTargetResolver
    {
        private static PointerEventData.InputButton ToInputButton(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right:
                    return PointerEventData.InputButton.Right;
                case MouseButton.Middle:
                    return PointerEventData.InputButton.Middle;
                default:
                    return PointerEventData.InputButton.Left;
            }
        }

        internal static PointerEventData CreatePointerPressData(
            EventSystem eventSystem,
            Vector2 screenPos,
            MouseButton button)
        {
            return new PointerEventData(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = ToInputButton(button)
            };
        }

        internal static ResolvedPointerTargets ResolvePressablePointerTargets(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            Vector2 inputPos,
            Vector2 screenPos,
            PointerEventData pointerData,
            MouseAction action)
        {
            if (parameters.BypassRaycast)
            {
                return ResolveBypassPressablePointerTargets(parameters, inputPos, pointerData, action);
            }

            RaycastResult? hit = UiRaycastHelper.RaycastUI(screenPos, eventSystem);
            if (hit == null)
            {
                return ResolvedPointerTargets.Empty;
            }

            return ResolveRaycastPressablePointerTargets(hit.Value, pointerData);
        }

        private static ResolvedPointerTargets ResolveBypassPressablePointerTargets(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            PointerEventData pointerData,
            MouseAction action)
        {
            (GameObject? rawTarget, SimulateMouseUiResponse? failureResponse) =
                ResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    action,
                    inputPos);
            if (failureResponse != null)
            {
                return ResolvedPointerTargets.Failure(failureResponse);
            }

            RaycastResult directRaycast = CreateDirectRaycastResult(rawTarget!);
            pointerData.pointerCurrentRaycast = directRaycast;
            pointerData.pointerPressRaycast = directRaycast;

            return CreateResolvedPressablePointerTargets(rawTarget!, pointerData);
        }

        private static ResolvedPointerTargets ResolveRaycastPressablePointerTargets(
            RaycastResult hit,
            PointerEventData pointerData)
        {
            GameObject rawTarget = hit.gameObject;
            pointerData.pointerCurrentRaycast = hit;
            pointerData.pointerPressRaycast = hit;

            return CreateResolvedPressablePointerTargets(rawTarget, pointerData);
        }

        private static ResolvedPointerTargets CreateResolvedPressablePointerTargets(
            GameObject rawTarget,
            PointerEventData pointerData)
        {
            // Execute dispatches only to the exact target; composite controls need hierarchy traversal.
            GameObject? pressTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget);
            GameObject? clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget);
            GameObject? target = pressTarget ?? clickTarget;
            if (target != null)
            {
                pointerData.pointerPress = target;
                pointerData.rawPointerPress = rawTarget;
            }

            return ResolvedPointerTargets.Success(rawTarget, pressTarget, clickTarget, target);
        }

        internal static (GameObject? Target, SimulateMouseUiResponse? FailureResponse) ResolveGameObjectPath(
            string targetPath,
            string parameterName,
            MouseAction action,
            Vector2 inputPosition)
        {
            TargetPathLookupResult lookupResult = FindActiveGameObjectByPath(targetPath);
            if (lookupResult.Target != null)
            {
                return (lookupResult.Target, null);
            }

            string message = lookupResult.MatchCount == 0
                ? $"{parameterName} '{targetPath}' was not found."
                : $"{parameterName} '{targetPath}' matched {lookupResult.MatchCount} active GameObjects. Use a unique hierarchy path.";

            SimulateMouseUiResponse failureResponse = new()
            {
                Success = false,
                Message = message,
                Action = action.ToString(),
                PositionX = inputPosition.x,
                PositionY = inputPosition.y
            };
            return (null, failureResponse);
        }

        internal static (GameObject? Target, SimulateMouseUiResponse? FailureResponse) ResolveDropTargetPath(
            MouseUiSimulationCommand parameters,
            MouseAction action,
            Vector2 inputPosition)
        {
            if (string.IsNullOrWhiteSpace(parameters.DropTargetPath))
            {
                return (null, null);
            }

            (GameObject? rawDropTarget, SimulateMouseUiResponse? failureResponse) =
                ResolveGameObjectPath(
                    parameters.DropTargetPath,
                    "DropTargetPath",
                    action,
                    inputPosition);
            if (failureResponse != null)
            {
                return (null, failureResponse);
            }

            GameObject? dropHandler = ExecuteEvents.GetEventHandler<IDropHandler>(rawDropTarget!);
            if (dropHandler == null)
            {
                failureResponse = new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"DropTargetPath '{parameters.DropTargetPath}' has no drop handler.",
                    Action = action.ToString(),
                    PositionX = inputPosition.x,
                    PositionY = inputPosition.y
                };
                return (null, failureResponse);
            }

            return (rawDropTarget, null);
        }

        private static TargetPathLookupResult FindActiveGameObjectByPath(string targetPath)
        {
            string normalizedPath = targetPath.Trim().Trim('/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return new TargetPathLookupResult(null, 0);
            }

#if UNITY_6000_4_OR_NEWER
            GameObject[] gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>();
#else
            GameObject[] gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#endif
            GameObject? matchedTarget = null;
            int matchCount = 0;

            foreach (GameObject gameObject in gameObjects)
            {
                if (!string.Equals(
                    GameObjectPathUtility.GetFullPath(gameObject),
                    normalizedPath,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                matchCount++;
                matchedTarget = gameObject;
            }

            return matchCount == 1
                ? new TargetPathLookupResult(matchedTarget, matchCount)
                : new TargetPathLookupResult(null, matchCount);
        }

        internal static RaycastResult CreateDirectRaycastResult(GameObject target)
        {
            return new RaycastResult
            {
                gameObject = target
            };
        }

        private readonly struct TargetPathLookupResult
        {
            public TargetPathLookupResult(GameObject? target, int matchCount)
            {
                Target = target;
                MatchCount = matchCount;
            }

            public GameObject? Target { get; }
            public int MatchCount { get; }
        }
    }
}
