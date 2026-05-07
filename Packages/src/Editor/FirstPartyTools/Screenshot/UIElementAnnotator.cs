using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Creates a temporary Screen Space Overlay Canvas that draws bounding boxes and labels
    // over interactive UI elements. The overlay is captured by GameView's m_RenderTexture
    // (OnGUI-based overlays are NOT included in the RT).
    /// <summary>
    /// Provides UI Element Annotator behavior for Unity CLI Loop.
    /// </summary>
    public static class UIElementAnnotator
    {
        private const int OVERLAY_SORT_ORDER = 32767;
        private const int LABEL_FONT_SIZE = 20;
        private const float OUTPUT_BORDER_NEUTRAL_THICKNESS = 2f;
        private const float OUTPUT_BORDER_COLOR_THICKNESS = 4f;
        private const int LABEL_PADDING_H = 6;
        private const int LABEL_PADDING_V = 3;
        private const float LABEL_DARK_TEXT_LUMINANCE_THRESHOLD = 0.62f;
        private const float OUTPUT_LABEL_OUTLINE_DISTANCE = 2f;
        private const float OUTPUT_LABEL_TO_BORDER_GAP = 4f;
        private const string INTERACTION_CLICK = "Click";
        private const string INTERACTION_DRAG = "Drag";
        private const string INTERACTION_DROP = "Drop";
        private const string INTERACTION_TEXT = "Text";
        private const string DISPLAY_LABEL_SEPARATOR = " / ";

        // Label-based colors separate dense controls where many elements share the same UI type.
        private static readonly Color[] ANNOTATION_COLORS =
        {
            new Color(1f, 0.35f, 0f, 0.95f),
            new Color(0f, 0.9f, 1f, 0.95f),
            new Color(1f, 0.15f, 0.65f, 0.95f),
            new Color(1f, 0.9f, 0f, 0.95f),
            new Color(0.2f, 1f, 0.35f, 0.95f),
            new Color(0.65f, 0.45f, 1f, 0.95f),
            new Color(1f, 1f, 1f, 0.95f),
            new Color(0.15f, 0.55f, 1f, 0.95f),
            new Color(1f, 0.55f, 0.75f, 0.95f),
            new Color(0.45f, 1f, 0.8f, 0.95f),
            new Color(0.9f, 0.45f, 0.15f, 0.95f),
            new Color(0.45f, 0.85f, 0.1f, 0.95f),
            new Color(0.95f, 0.2f, 0.2f, 0.95f),
            new Color(0.55f, 0.7f, 1f, 0.95f),
            new Color(0.95f, 0.95f, 0.45f, 0.95f),
            new Color(0.85f, 0.55f, 1f, 0.95f)
        };
        private static readonly Color FALLBACK_COLOR = new Color(1f, 1f, 0f, 0.9f);
        private static readonly Color DARK_CONTRAST_COLOR = new Color(0f, 0f, 0f, 0.95f);
        private static readonly Color LIGHT_CONTRAST_COLOR = new Color(1f, 1f, 1f, 0.95f);

        public static List<UIElementInfo> CollectInteractiveElements()
        {
            List<UIElementInfo> elements = new();
            HashSet<GameObject> processedObjects = new();

            CollectSelectables(elements, processedObjects);
            CollectEventHandlers(elements, processedObjects);

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

            foreach (UIElementInfo element in elements)
            {
                CreateAnnotationForElement(root.transform, element, font, borderMetrics);
            }

            return root;
        }

        public static void DestroyAnnotationOverlay(GameObject overlay)
        {
            if (overlay != null)
            {
                Object.DestroyImmediate(overlay);
            }
        }

        private static void CollectSelectables(List<UIElementInfo> elements, HashSet<GameObject> processedObjects)
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
                AddElementInfo(elements, selectable.gameObject, selectable.name, type);
            }
        }

        // Collects non-Selectable MonoBehaviours that implement pointer/drag event interfaces.
        // Priority: IDragHandler > IDropHandler > IPointerClickHandler > IPointerDownHandler
        private static void CollectEventHandlers(List<UIElementInfo> elements, HashSet<GameObject> processedObjects)
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

                string type = ClassifyEventHandler(behaviour);
                if (type == null)
                {
                    continue;
                }

                processedObjects.Add(behaviour.gameObject);
                AddElementInfo(elements, behaviour.gameObject, behaviour.name, type);
            }
        }

        private static string ClassifyEventHandler(MonoBehaviour behaviour)
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
        private static readonly List<RaycastResult> SharedRaycastResults = new List<RaycastResult>();
        private static PointerEventData SharedPointerEventData;
        private static EventSystem SharedPointerEventSystem;

        private static void AddElementInfo(List<UIElementInfo> elements, GameObject go, string name, string type)
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

            if (!IsRaycastReachable(go, centerX, centerY))
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
            return raycaster != null && raycaster.isActiveAndEnabled;
        }

        // simulate-mouse uses EventSystem.RaycastAll, so only advertise elements whose
        // center point is actually hittable. Skips the check when no EventSystem exists
        // (e.g. annotation-only scenes without interaction).
        private static bool IsRaycastReachable(GameObject go, float centerX, float centerY)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return true;
            }

            if (SharedPointerEventData == null || SharedPointerEventSystem != eventSystem)
            {
                SharedPointerEventData = new PointerEventData(eventSystem);
                SharedPointerEventSystem = eventSystem;
            }
            SharedPointerEventData.position = new Vector2(centerX, centerY);

            SharedRaycastResults.Clear();
            eventSystem.RaycastAll(SharedPointerEventData, SharedRaycastResults);

            Transform targetTransform = go.transform;
            foreach (RaycastResult raycastResult in SharedRaycastResults)
            {
                Transform hitTransform = raycastResult.gameObject.transform;
                if (hitTransform == targetTransform || hitTransform.IsChildOf(targetTransform))
                {
                    return true;
                }
            }

            // EventSystem clips at Screen.width/height which can be smaller than the
            // Canvas layout space (Game view target resolution). Check Canvas space directly.
            RectTransform rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                return RectTransformUtility.RectangleContainsScreenPoint(
                    rectTransform, new Vector2(centerX, centerY), null);
            }

            return false;
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

        private static void CreateAnnotationForElement(
            Transform parent,
            UIElementInfo element,
            Font font,
            AnnotationBorderMetrics borderMetrics)
        {
            float screenMinX = element.BoundsMinX;
            float screenMaxX = element.BoundsMaxX;
            float screenMinY = element.BoundsMinY;
            float screenMaxY = element.BoundsMaxY;

            Color color = GetAnnotationColorForElement(element);
            Color contrastColor = GetContrastingTextColor(color);
            AnnotationBorderColors borderColors = GetAnnotationBorderColors(color);

            CreateBorder(
                parent,
                "LightOuter",
                screenMinX - borderMetrics.OuterOffset,
                screenMinY - borderMetrics.OuterOffset,
                screenMaxX + borderMetrics.OuterOffset,
                screenMaxY + borderMetrics.OuterOffset,
                borderMetrics.NeutralThickness,
                borderColors.Outer);
            CreateBorder(
                parent,
                "ColorMiddle",
                screenMinX - borderMetrics.ColorOffset,
                screenMinY - borderMetrics.ColorOffset,
                screenMaxX + borderMetrics.ColorOffset,
                screenMaxY + borderMetrics.ColorOffset,
                borderMetrics.ColorThickness,
                borderColors.Middle);
            CreateBorder(parent, "DarkInner", screenMinX, screenMinY, screenMaxX, screenMaxY, borderMetrics.NeutralThickness, borderColors.Inner);

            string labelText = CreateDisplayLabel(element);
            CreateLabel(
                parent,
                labelText,
                screenMinX,
                screenMaxY + borderMetrics.OuterOffset + borderMetrics.LabelOutlineDistance + borderMetrics.LabelToBorderGap,
                color,
                contrastColor,
                font,
                borderMetrics.LabelOutlineDistance);
        }

        private static void CreateBorder(
            Transform parent, string name,
            float minX, float minY, float maxX, float maxY,
            float thickness, Color color)
        {
            BorderEdgeRects borderEdgeRects = CalculateBorderEdgeRects(minX, minY, maxX, maxY, thickness);

            CreateBorderEdge(parent, $"{name}_Top", borderEdgeRects.Top, color);
            CreateBorderEdge(parent, $"{name}_Bottom", borderEdgeRects.Bottom, color);
            CreateBorderEdge(parent, $"{name}_Left", borderEdgeRects.Left, color);
            CreateBorderEdge(parent, $"{name}_Right", borderEdgeRects.Right, color);
        }

        internal static BorderEdgeRects CalculateBorderEdgeRects(
            float minX, float minY, float maxX, float maxY, float thickness)
        {
            Debug.Assert(maxX >= minX, "maxX must not be smaller than minX.");
            Debug.Assert(maxY >= minY, "maxY must not be smaller than minY.");
            Debug.Assert(thickness >= 0f, "thickness must not be negative.");

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;
            float verticalEdgeHeight = Mathf.Max(0f, boxHeight - thickness * 2f);

            return new BorderEdgeRects(
                new Rect(minX, maxY - thickness, boxWidth, thickness),
                new Rect(minX, minY, boxWidth, thickness),
                new Rect(minX, minY + thickness, thickness, verticalEdgeHeight),
                new Rect(maxX - thickness, minY + thickness, thickness, verticalEdgeHeight));
        }

        private static void CreateBorderEdge(
            Transform parent, string name,
            Rect rect,
            Color color)
        {
            GameObject edgeGo = new($"Border_{name}");
            edgeGo.hideFlags = HideFlags.HideAndDontSave;
            edgeGo.transform.SetParent(parent, false);

            RectTransform rt = edgeGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);

            Image image = edgeGo.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void CreateLabel(
            Transform parent, string text,
            float x, float y,
            Color backgroundColor, Color textColor, Font font, float outlineDistance)
        {
            GameObject bgGo = new("LabelBg");
            bgGo.hideFlags = HideFlags.HideAndDontSave;
            bgGo.transform.SetParent(parent, false);

            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.zero;
            bgRt.pivot = new Vector2(0f, 0f);
            bgRt.anchoredPosition = new Vector2(x, y);

            Image bgImage = bgGo.AddComponent<Image>();
            bgImage.color = backgroundColor;
            bgImage.raycastTarget = false;

            Outline bgOutline = bgGo.AddComponent<Outline>();
            bgOutline.effectColor = GetContrastPartnerColor(backgroundColor);
            bgOutline.effectDistance = new Vector2(outlineDistance, -outlineDistance);

            ContentSizeFitter fitter = bgGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            HorizontalLayoutGroup layout = bgGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(LABEL_PADDING_H, LABEL_PADDING_H, LABEL_PADDING_V, LABEL_PADDING_V);
            layout.childAlignment = TextAnchor.MiddleLeft;

            GameObject textGo = new("LabelText");
            textGo.hideFlags = HideFlags.HideAndDontSave;
            textGo.transform.SetParent(bgGo.transform, false);

            textGo.AddComponent<RectTransform>();

            Text labelText = textGo.AddComponent<Text>();
            labelText.text = text;
            labelText.font = font;
            labelText.fontSize = LABEL_FONT_SIZE;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = textColor;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            labelText.raycastTarget = false;
        }

        internal static Color GetAnnotationColorForElement(UIElementInfo element)
        {
            Debug.Assert(element != null, "UIElementInfo must not be null.");

            int labelIndex = GetLabelIndex(element.Label);
            if (labelIndex < 0)
            {
                return FALLBACK_COLOR;
            }

            return ANNOTATION_COLORS[labelIndex % ANNOTATION_COLORS.Length];
        }

        internal static Color GetContrastingTextColor(Color backgroundColor)
        {
            float luminance = CalculateLuminance(backgroundColor);
            if (luminance >= LABEL_DARK_TEXT_LUMINANCE_THRESHOLD)
            {
                return DARK_CONTRAST_COLOR;
            }

            return LIGHT_CONTRAST_COLOR;
        }

        internal static Color GetContrastPartnerColor(Color color)
        {
            Color readableColor = GetContrastingTextColor(color);
            if (readableColor == DARK_CONTRAST_COLOR)
            {
                return LIGHT_CONTRAST_COLOR;
            }

            return DARK_CONTRAST_COLOR;
        }

        internal static AnnotationBorderColors GetAnnotationBorderColors(Color annotationColor)
        {
            return new AnnotationBorderColors(DARK_CONTRAST_COLOR, annotationColor, LIGHT_CONTRAST_COLOR);
        }

        internal static AnnotationBorderMetrics CalculateAnnotationBorderMetrics(float outputResolutionScale)
        {
            Debug.Assert(outputResolutionScale > 0f, "Output resolution scale must be positive.");

            float neutralThickness = OUTPUT_BORDER_NEUTRAL_THICKNESS / outputResolutionScale;
            float colorThickness = OUTPUT_BORDER_COLOR_THICKNESS / outputResolutionScale;
            float labelOutlineDistance = OUTPUT_LABEL_OUTLINE_DISTANCE / outputResolutionScale;
            float labelToBorderGap = OUTPUT_LABEL_TO_BORDER_GAP / outputResolutionScale;

            return new AnnotationBorderMetrics(
                neutralThickness,
                colorThickness,
                colorThickness,
                colorThickness + neutralThickness,
                labelOutlineDistance,
                labelToBorderGap);
        }

        internal static string GetInteractionForType(string type)
        {
            if (type == "Slider" || type == "Scrollbar" || type == "Draggable")
            {
                return INTERACTION_DRAG;
            }

            if (type == "DropTarget")
            {
                return INTERACTION_DROP;
            }

            if (type == "InputField")
            {
                return INTERACTION_TEXT;
            }

            return INTERACTION_CLICK;
        }

        internal static string CreateDisplayLabel(UIElementInfo element)
        {
            Debug.Assert(element != null, "UIElementInfo must not be null.");

            string interaction = element.Interaction;
            if (string.IsNullOrEmpty(interaction))
            {
                interaction = GetInteractionForType(element.Type);
            }

            if (string.IsNullOrEmpty(element.Label))
            {
                return interaction.ToUpperInvariant();
            }

            return $"{element.Label}{DISPLAY_LABEL_SEPARATOR}{interaction.ToUpperInvariant()}";
        }

        private static float CalculateLuminance(Color color)
        {
            return color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        }

        private static int GetLabelIndex(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return -1;
            }

            int index = 0;
            for (int i = 0; i < label.Length; i++)
            {
                char labelCharacter = label[i];
                if (labelCharacter < 'A' || labelCharacter > 'Z')
                {
                    return -1;
                }

                index = index * 26 + labelCharacter - 'A' + 1;
            }

            return index - 1;
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
