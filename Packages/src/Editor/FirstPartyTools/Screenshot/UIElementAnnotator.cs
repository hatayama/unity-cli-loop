#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Creates a temporary Screen Space Overlay Canvas that draws bounding boxes and labels
    // over interactive UI elements. The overlay is captured by PlayModeView.m_TargetTexture
    // (OnGUI-based overlays are NOT included in the RT).
    /// <summary>
    /// Provides UI Element Annotator behavior for Unity CLI Loop.
    /// </summary>
    public static class UIElementAnnotator
    {
        private const int OVERLAY_SORT_ORDER = 32767;

        public static List<UIElementInfo> CollectInteractiveElements()
        {
            List<UIElementInfo> elements = new();
            HashSet<GameObject> processedObjects = new();
            UiRaycastHelper.RaycastContext? raycastContext = CreateRaycastContextForCurrentEventSystem();

            CollectSelectables(elements, processedObjects, raycastContext);
            CollectEventHandlers(elements, processedObjects, raycastContext);

            return elements;
        }

        // Sorts by z-order (frontmost = A) so the AI can reason about occlusion from label order alone
        public static void AssignLabels(List<UIElementInfo> elements)
        {
            elements.Sort((a, b) =>
            {
                int sortOrderCompare = b.SortingOrder.CompareTo(a.SortingOrder);
                if (sortOrderCompare != 0) return sortOrderCompare;

                return b.SiblingIndex.CompareTo(a.SiblingIndex);
            });

            for (int i = 0; i < elements.Count; i++)
            {
                elements[i].Label = GenerateLabel(i);
            }
        }

        private static string GenerateLabel(int index)
        {
            string label = "";
            int remaining = index;
            do
            {
                label = (char)('A' + remaining % 26) + label;
                remaining = remaining / 26 - 1;
            } while (remaining >= 0);

            return label;
        }

        public static void ConvertToSimCoordinates(List<UIElementInfo> elements, int screenHeight)
        {
            foreach (UIElementInfo element in elements)
            {
                element.SimY = screenHeight - element.SimY;
                float originalMinY = element.BoundsMinY;
                element.BoundsMinY = screenHeight - element.BoundsMaxY;
                element.BoundsMaxY = screenHeight - originalMinY;
            }
        }

        public static GameObject CreateAnnotationOverlay(List<UIElementInfo> elements, float outputResolutionScale)
        {
            AnnotationBorderMetrics borderMetrics = CalculateAnnotationBorderMetrics(outputResolutionScale);
            GameObject root = new("__UIAnnotation__");
            root.hideFlags = HideFlags.HideAndDontSave;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OVERLAY_SORT_ORDER;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UIElementAnnotationRenderer.RenderAnnotations(root.transform, elements, font, borderMetrics);

            return root;
        }

        public static void DestroyAnnotationOverlay(GameObject overlay)
        {
            if (overlay != null)
            {
                Object.DestroyImmediate(overlay);
            }
        }

        private static void CollectSelectables(
            List<UIElementInfo> elements,
            HashSet<GameObject> processedObjects,
            UiRaycastHelper.RaycastContext? raycastContext)
        {
            Selectable[] selectables = Selectable.allSelectablesArray;
            foreach (Selectable selectable in selectables)
            {
                if (!selectable.IsInteractable() || !selectable.isActiveAndEnabled)
                {
                    continue;
                }

                processedObjects.Add(selectable.gameObject);

                string type = ClassifySelectable(selectable);
                AddElementInfo(elements, selectable.gameObject, selectable.name, type, raycastContext);
            }
        }

        // Collects non-Selectable MonoBehaviours that implement pointer/drag event interfaces.
        // Priority: IDragHandler > IDropHandler > IPointerClickHandler > IPointerDownHandler
        private static void CollectEventHandlers(
            List<UIElementInfo> elements,
            HashSet<GameObject> processedObjects,
            UiRaycastHelper.RaycastContext? raycastContext)
        {
            MonoBehaviour[] allBehaviours = Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in allBehaviours)
            {
                if (!behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                if (processedObjects.Contains(behaviour.gameObject))
                {
                    continue;
                }

                string? type = ClassifyEventHandler(behaviour);
                if (type == null)
                {
                    continue;
                }

                processedObjects.Add(behaviour.gameObject);
                AddElementInfo(elements, behaviour.gameObject, behaviour.name, type, raycastContext);
            }
        }

        private static string? ClassifyEventHandler(MonoBehaviour behaviour)
        {
            if (behaviour is IDragHandler) return "Draggable";
            if (behaviour is IDropHandler) return "DropTarget";
            if (behaviour is IPointerClickHandler) return "Button";
            if (behaviour is IPointerDownHandler) return "Button";
            return null;
        }

        private static string ClassifySelectable(Selectable selectable)
        {
            if (selectable is Button) return "Button";
            if (selectable is Toggle) return "Toggle";
            if (selectable is Slider) return "Slider";
            if (selectable is Dropdown) return "Dropdown";
            if (selectable is InputField) return "InputField";
            if (selectable is Scrollbar) return "Scrollbar";
            if (selectable is IDragHandler) return "Draggable";
            if (selectable is IDropHandler) return "DropTarget";
            return "Selectable";
        }

        // Reusable buffers to avoid per-element allocations in AddElementInfo → GetScreenCorners
        private static readonly Vector3[] SharedWorldCorners = new Vector3[4];
        private static readonly Vector2[] SharedScreenCorners = new Vector2[4];

        private static void AddElementInfo(
            List<UIElementInfo> elements,
            GameObject go,
            string name,
            string type,
            UiRaycastHelper.RaycastContext? raycastContext)
        {
            RectTransform rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return;
            }

            Canvas canvas = go.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            // EventSystem.RaycastAll only hits elements under a Canvas with an enabled GraphicRaycaster
            if (!HasActiveGraphicRaycaster(canvas))
            {
                return;
            }

            if (!GetScreenCorners(rectTransform, canvas))
            {
                return;
            }

            float minX = Mathf.Min(SharedScreenCorners[0].x, SharedScreenCorners[1].x, SharedScreenCorners[2].x, SharedScreenCorners[3].x);
            float maxX = Mathf.Max(SharedScreenCorners[0].x, SharedScreenCorners[1].x, SharedScreenCorners[2].x, SharedScreenCorners[3].x);
            float minY = Mathf.Min(SharedScreenCorners[0].y, SharedScreenCorners[1].y, SharedScreenCorners[2].y, SharedScreenCorners[3].y);
            float maxY = Mathf.Max(SharedScreenCorners[0].y, SharedScreenCorners[1].y, SharedScreenCorners[2].y, SharedScreenCorners[3].y);

            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;

            if (!IsRaycastReachable(go, centerX, centerY, raycastContext))
            {
                return;
            }

            elements.Add(new UIElementInfo
            {
                Name = name,
                Path = GameObjectPathUtility.GetFullPath(go),
                Type = type,
                Interaction = GetInteractionForType(type),
                SimX = centerX,
                SimY = centerY,
                BoundsMinX = minX,
                BoundsMinY = minY,
                BoundsMaxX = maxX,
                BoundsMaxY = maxY,
                SortingOrder = canvas.sortingOrder,
                SiblingIndex = go.transform.GetSiblingIndex()
            });
        }

        private static bool HasActiveGraphicRaycaster(Canvas canvas)
        {
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            return canvas.isActiveAndEnabled && raycaster != null && raycaster.isActiveAndEnabled;
        }

        // Uses the same raycast path as simulate-mouse so annotations match UI input behavior.
        // Skips the check when no EventSystem exists, such as annotation-only scenes without interaction.
        private static bool IsRaycastReachable(
            GameObject go,
            float centerX,
            float centerY,
            UiRaycastHelper.RaycastContext? raycastContext)
        {
            if (raycastContext == null)
            {
                return true;
            }

            RaycastResult? raycastResult = raycastContext.Raycast(new Vector2(centerX, centerY));
            if (raycastResult == null)
            {
                return false;
            }

            Transform targetTransform = go.transform;
            Transform hitTransform = raycastResult.Value.gameObject.transform;
            return hitTransform == targetTransform || hitTransform.IsChildOf(targetTransform);
        }

        // Reuses one raycast context while collecting annotations because a screenshot can
        // test many UI elements in one frame.
        private static UiRaycastHelper.RaycastContext? CreateRaycastContextForCurrentEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return null;
            }

            return new UiRaycastHelper.RaycastContext(eventSystem);
        }

        // Writes 4 corners into SharedScreenCorners in screen pixel coordinates (bottom-left origin).
        // For ScreenSpaceOverlay: world corners == screen pixels.
        // For Camera/WorldSpace: projects through the canvas camera.
        // Returns false when the canvas camera is unavailable for non-overlay canvases.
        private static bool GetScreenCorners(RectTransform rectTransform, Canvas canvas)
        {
            rectTransform.GetWorldCorners(SharedWorldCorners);

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                for (int i = 0; i < 4; i++)
                {
                    SharedScreenCorners[i] = new Vector2(SharedWorldCorners[i].x, SharedWorldCorners[i].y);
                }
            }
            else
            {
                // Prefer the rendering canvas's camera; fall back to root canvas, then Camera.main
                Camera cam = canvas.worldCamera;
                if (cam == null)
                {
                    Canvas rootCanvas = canvas.rootCanvas;
                    if (rootCanvas != canvas)
                    {
                        cam = rootCanvas.worldCamera;
                    }
                }

                if (cam == null)
                {
                    cam = Camera.main;
                }

                if (cam == null)
                {
                    return false;
                }

                for (int i = 0; i < 4; i++)
                {
                    SharedScreenCorners[i] = RectTransformUtility.WorldToScreenPoint(cam, SharedWorldCorners[i]);
                }
            }

            return true;
        }

        internal static Color GetAnnotationColorForElement(UIElementInfo element)
        {
            return UIElementAnnotationStyling.GetAnnotationColorForElement(element);
        }

        internal static Color GetContrastingTextColor(Color backgroundColor)
        {
            return UIElementAnnotationStyling.GetContrastingTextColor(backgroundColor);
        }

        internal static Color GetContrastPartnerColor(Color color)
        {
            return UIElementAnnotationStyling.GetContrastPartnerColor(color);
        }

        internal static AnnotationBorderColors GetAnnotationBorderColors(Color annotationColor)
        {
            return UIElementAnnotationStyling.GetAnnotationBorderColors(annotationColor);
        }

        internal static AnnotationBorderMetrics CalculateAnnotationBorderMetrics(float outputResolutionScale)
        {
            return UIElementAnnotationStyling.CalculateAnnotationBorderMetrics(outputResolutionScale);
        }

        internal static string GetInteractionForType(string type)
        {
            return UIElementAnnotationStyling.GetInteractionForType(type);
        }

        internal static string CreateDisplayLabel(UIElementInfo element)
        {
            return UIElementAnnotationStyling.CreateDisplayLabel(element);
        }

        internal static RaycastOutlineSegment ConvertTopLeftOutlineSegmentToScreenSegment(
            RaycastOutlineSegment segment,
            float screenHeight)
        {
            return UIElementAnnotationRenderer.ConvertTopLeftOutlineSegmentToScreenSegment(segment, screenHeight);
        }

        internal static Rect CalculateOutlineSegmentRect(RaycastOutlineSegment segment, float thickness)
        {
            return UIElementAnnotationRenderer.CalculateOutlineSegmentRect(segment, thickness);
        }

        internal static BorderEdgeRects CalculateBorderEdgeRects(
            float minX, float minY, float maxX, float maxY, float thickness)
        {
            return UIElementAnnotationRenderer.CalculateBorderEdgeRects(minX, minY, maxX, maxY, thickness);
        }

        internal readonly struct BorderEdgeRects
        {
            public readonly Rect Top;
            public readonly Rect Bottom;
            public readonly Rect Left;
            public readonly Rect Right;

            public BorderEdgeRects(Rect top, Rect bottom, Rect left, Rect right)
            {
                Top = top;
                Bottom = bottom;
                Left = left;
                Right = right;
            }
        }

        internal readonly struct AnnotationBorderColors
        {
            public readonly Color Inner;
            public readonly Color Middle;
            public readonly Color Outer;

            public AnnotationBorderColors(Color inner, Color middle, Color outer)
            {
                Inner = inner;
                Middle = middle;
                Outer = outer;
            }
        }

        internal readonly struct AnnotationBorderMetrics
        {
            public readonly float NeutralThickness;
            public readonly float ColorThickness;
            public readonly float ColorOffset;
            public readonly float OuterOffset;
            public readonly float LabelOutlineDistance;
            public readonly float LabelToBorderGap;

            public AnnotationBorderMetrics(
                float neutralThickness,
                float colorThickness,
                float colorOffset,
                float outerOffset,
                float labelOutlineDistance,
                float labelToBorderGap)
            {
                NeutralThickness = neutralThickness;
                ColorThickness = colorThickness;
                ColorOffset = colorOffset;
                OuterOffset = outerOffset;
                LabelOutlineDistance = labelOutlineDistance;
                LabelToBorderGap = labelToBorderGap;
            }
        }
    }
}
