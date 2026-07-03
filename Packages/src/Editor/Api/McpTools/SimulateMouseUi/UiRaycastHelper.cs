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
            RaycastContext context = new RaycastContext(eventSystem);
            return context.Raycast(screenPosition);
        }

        // Reuses EventSystem and Canvas-space raycast buffers for callers that test
        // many positions in one frame, such as screenshot annotation collection.
        // Keep this scoped to one frame because it snapshots Canvas and Graphic state.
        internal sealed class RaycastContext
        {
            private readonly EventSystem _eventSystem;
            private readonly PointerEventData _pointerData;
            private readonly List<RaycastResult> _eventSystemResults = new List<RaycastResult>();
            private readonly List<CanvasRaycastSource> _canvasRaycastSources;

            internal RaycastContext(EventSystem eventSystem)
            {
                Debug.Assert(eventSystem != null, "EventSystem is required for UI raycasting.");
                _eventSystem = eventSystem!;
                _pointerData = new PointerEventData(eventSystem!);
                _canvasRaycastSources = CollectCanvasRaycastSources();
            }

            public RaycastResult? Raycast(Vector2 screenPosition)
            {
                _eventSystemResults.Clear();
                _pointerData.position = screenPosition;
                _eventSystem.RaycastAll(_pointerData, _eventSystemResults);
                RaycastResult? canvasSpaceHit = RaycastCanvasSpaceFromSources(screenPosition, _canvasRaycastSources);

                if (_eventSystemResults.Count > 0)
                {
                    RaycastResult firstHit = _eventSystemResults[0];

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
        }

        private static List<CanvasRaycastSource> CollectCanvasRaycastSources()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            List<CanvasRaycastSource> canvasRaycastSources = new List<CanvasRaycastSource>();

            foreach (Canvas canvas in canvases)
            {
                if (!canvas.isActiveAndEnabled)
                {
                    continue;
                }

                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                GraphicRaycaster? raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.isActiveAndEnabled)
                {
                    continue;
                }

                Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>();
                canvasRaycastSources.Add(new CanvasRaycastSource(canvas, raycaster, graphics));
            }

            return canvasRaycastSources;
        }

        private static RaycastResult? RaycastCanvasSpaceFromSources(
            Vector2 canvasPosition,
            List<CanvasRaycastSource> canvasRaycastSources)
        {
            RaycastResult? bestHit = null;

            foreach (CanvasRaycastSource source in canvasRaycastSources)
            {
                foreach (Graphic graphic in source.Graphics)
                {
                    if (!IsRaycastCandidate(graphic, canvasPosition))
                    {
                        continue;
                    }

                    RaycastResult candidate = new RaycastResult
                    {
                        gameObject = graphic.gameObject,
                        module = source.Raycaster,
                        screenPosition = canvasPosition,
                        sortingLayer = source.Canvas.sortingLayerID,
                        sortingOrder = source.Canvas.sortingOrder,
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

        private readonly struct CanvasRaycastSource
        {
            public Canvas Canvas { get; }
            public GraphicRaycaster Raycaster { get; }
            public Graphic[] Graphics { get; }

            public CanvasRaycastSource(Canvas canvas, GraphicRaycaster raycaster, Graphic[] graphics)
            {
                Canvas = canvas;
                Raycaster = raycaster;
                Graphics = graphics;
            }
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

            return CompareRaycastPriority(canvasSpaceHit, eventSystemHit) > 0;
        }

        private static bool IsGraphicRaycast(RaycastResult raycastResult)
        {
            return raycastResult.module is GraphicRaycaster;
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
