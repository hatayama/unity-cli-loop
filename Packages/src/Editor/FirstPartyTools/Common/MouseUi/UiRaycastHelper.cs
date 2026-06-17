#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Shared UI raycast logic used by SimulateMouseUiTool and InputReplayer.
    /// <summary>
    /// Provides helper operations for UI Raycast behavior.
    /// </summary>
    internal static class UiRaycastHelper
    {
        public static RaycastResult? RaycastUI(Vector2 screenPosition, EventSystem eventSystem)
        {
            PointerEventData pointerData = new(eventSystem)
            {
                position = screenPosition
            };
            List<RaycastResult> results = new();
            eventSystem.RaycastAll(pointerData, results);
            RaycastResult? canvasSpaceHit = RaycastCanvasSpace(screenPosition);

            if (results.Count > 0)
            {
                RaycastResult firstHit = results[0];

                if (canvasSpaceHit != null && ShouldPreferCanvasSpaceHit(canvasSpaceHit.Value, firstHit))
                {
                    return canvasSpaceHit;
                }

                return firstHit;
            }

            // EventSystem clips at Screen.width/height, which can be smaller than the
            // Canvas layout space (Game view target resolution). Fall back to manual hit testing.
            return canvasSpaceHit;
        }

        // Bypass EventSystem's Screen-bounds clipping by directly testing Graphic rects in Canvas space.
        // Only supports ScreenSpaceOverlay canvases where world positions equal Canvas-space positions.
        public static RaycastResult? RaycastCanvasSpace(Vector2 canvasPosition)
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            RaycastResult? bestHit = null;

            foreach (Canvas canvas in canvases)
            {
                if (!canvas.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                GraphicRaycaster? raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.enabled)
                {
                    continue;
                }

                Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>();
                foreach (Graphic graphic in graphics)
                {
                    if (!IsRaycastCandidate(graphic, canvas, raycaster, canvasPosition))
                    {
                        continue;
                    }

                    RaycastResult candidate = new RaycastResult
                    {
                        gameObject = graphic.gameObject,
                        module = raycaster,
                        screenPosition = canvasPosition,
                        sortingLayer = canvas.sortingLayerID,
                        sortingOrder = canvas.sortingOrder,
                        depth = graphic.depth
                    };

                    if (bestHit == null || CompareRaycastPriority(candidate, bestHit.Value) > 0)
                    {
                        bestHit = candidate;
                    }
                }
            }

            return bestHit;
        }

        // Respects CanvasGroup.blocksRaycasts, Mask, RectMask2D, and custom ICanvasRaycastFilter
        private static bool IsRaycastCandidate(Graphic graphic, Canvas canvas, GraphicRaycaster raycaster, Vector2 canvasPosition)
        {
            if (!graphic.gameObject.activeInHierarchy || !graphic.enabled)
            {
                return false;
            }

            // Why: GraphicRaycaster reads GraphicRegistry per Canvas; child Canvas graphics belong to their own raycaster.
            if (graphic.canvas != canvas)
            {
                return false;
            }

            if (!graphic.raycastTarget || graphic.depth == -1 || graphic.canvasRenderer.cull)
            {
                return false;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    graphic.rectTransform, canvasPosition, null, graphic.raycastPadding))
            {
                return false;
            }

            // Why: Canvas-space fallback bypasses GraphicRaycaster.Raycast, so mirror its default reversed-graphic filter.
            if (raycaster.ignoreReversedGraphics && !IsFacingForward(graphic))
            {
                return false;
            }

            return graphic.Raycast(canvasPosition, null);
        }

        private static bool IsFacingForward(Graphic graphic)
        {
            Transform transform = graphic.transform;
            Vector3 direction = transform.rotation * Vector3.forward;
            return Vector3.Dot(Vector3.forward, direction) > 0f;
        }

        private static bool ShouldPreferCanvasSpaceHit(RaycastResult canvasSpaceHit, RaycastResult eventSystemHit)
        {
            if (!IsGraphicRaycast(canvasSpaceHit))
            {
                return false;
            }

            if (!IsGraphicRaycast(eventSystemHit))
            {
                return true;
            }

            // Why: for normal in-screen UI hits, EventSystem ordering is authoritative; fallback is only for Screen-bounds clipping.
            if (IsInsideScreenBounds(canvasSpaceHit.screenPosition))
            {
                return false;
            }

            return CompareRaycastPriority(canvasSpaceHit, eventSystemHit) > 0;
        }

        private static bool IsGraphicRaycast(RaycastResult raycastResult)
        {
            return raycastResult.module is GraphicRaycaster;
        }

        private static bool IsInsideScreenBounds(Vector2 screenPosition)
        {
            return screenPosition.x >= 0f &&
                   screenPosition.x <= Screen.width &&
                   screenPosition.y >= 0f &&
                   screenPosition.y <= Screen.height;
        }

        private static int CompareRaycastPriority(RaycastResult left, RaycastResult right)
        {
            int sortOrderPriority = Compare(GetSortOrderPriority(left), GetSortOrderPriority(right));
            if (sortOrderPriority != 0)
            {
                return sortOrderPriority;
            }

            int renderOrderPriority = Compare(GetRenderOrderPriority(left), GetRenderOrderPriority(right));
            if (renderOrderPriority != 0)
            {
                return renderOrderPriority;
            }

            int sortingLayer = Compare(GetSortingLayerValue(left), GetSortingLayerValue(right));
            if (sortingLayer != 0)
            {
                return sortingLayer;
            }

            int sortingOrder = Compare(left.sortingOrder, right.sortingOrder);
            if (sortingOrder != 0)
            {
                return sortingOrder;
            }

            return Compare(left.depth, right.depth);
        }

        private static int GetSortOrderPriority(RaycastResult result)
        {
            return result.module != null ? result.module.sortOrderPriority : 0;
        }

        private static int GetRenderOrderPriority(RaycastResult result)
        {
            return result.module != null ? result.module.renderOrderPriority : 0;
        }

        private static int GetSortingLayerValue(RaycastResult result)
        {
            return SortingLayer.GetLayerValueFromID(result.sortingLayer);
        }

        private static int Compare(int left, int right)
        {
            if (left > right)
            {
                return 1;
            }

            if (left < right)
            {
                return -1;
            }

            return 0;
        }
    }
}
