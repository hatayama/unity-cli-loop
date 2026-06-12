#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    // Shared UI raycast logic used by SimulateMouseUiTool and InputReplayer.
    internal static class UiRaycastHelper
    {
        public static RaycastResult? RaycastUI(Vector2 screenPosition, EventSystem eventSystem)
        {
            PointerEventData pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };
            List<RaycastResult> results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            if (results.Count > 0)
            {
                RaycastResult firstHit = results[0];

                if (firstHit.module is GraphicRaycaster)
                {
                    return firstHit;
                }

                // EventSystem clips GraphicRaycaster hits at Screen.width/height, so a non-UI
                // raycaster (e.g. PhysicsRaycaster) can win by default even though
                // ScreenSpaceOverlay UI always renders in front of world-space content.
                // Prefer the Canvas-space UI hit when one exists at this position.
                RaycastResult? canvasSpaceHit = RaycastCanvasSpace(screenPosition);
                return canvasSpaceHit ?? firstHit;
            }

            // EventSystem clips at Screen.width/height, which can be smaller than the
            // Canvas layout space (Game view target resolution). Fall back to manual hit testing.
            return RaycastCanvasSpace(screenPosition);
        }

        // Bypass EventSystem's Screen-bounds clipping by directly testing Graphic rects in Canvas space.
        // Only supports ScreenSpaceOverlay canvases where world positions equal Canvas-space positions.
        public static RaycastResult? RaycastCanvasSpace(Vector2 canvasPosition)
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Graphic? bestHit = null;
            int bestSortingOrder = int.MinValue;
            int bestDepth = -1;

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
                    if (!IsRaycastCandidate(graphic, canvasPosition))
                    {
                        continue;
                    }

                    int sortingOrder = canvas.sortingOrder;

                    if (sortingOrder > bestSortingOrder ||
                        (sortingOrder == bestSortingOrder && graphic.depth > bestDepth))
                    {
                        bestHit = graphic;
                        bestSortingOrder = sortingOrder;
                        bestDepth = graphic.depth;
                    }
                }
            }

            if (bestHit == null)
            {
                return null;
            }

            return new RaycastResult
            {
                gameObject = bestHit.gameObject,
                sortingOrder = bestSortingOrder
            };
        }

        // Respects CanvasGroup.blocksRaycasts, Mask, RectMask2D, and custom ICanvasRaycastFilter
        private static bool IsRaycastCandidate(Graphic graphic, Vector2 canvasPosition)
        {
            if (!graphic.gameObject.activeInHierarchy || !graphic.enabled)
            {
                return false;
            }

            if (!graphic.raycastTarget || graphic.depth == -1 || graphic.canvasRenderer.cull)
            {
                return false;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    graphic.rectTransform, canvasPosition, null))
            {
                return false;
            }

            return graphic.Raycast(canvasPosition, null);
        }
    }
}
