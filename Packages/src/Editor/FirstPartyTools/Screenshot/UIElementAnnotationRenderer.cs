#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Draws annotation borders, outlines, and labels onto a Screen Space Overlay canvas.
    /// </summary>
    internal static class UIElementAnnotationRenderer
    {
        private const int LABEL_FONT_SIZE = 20;
        private const int LABEL_PADDING_H = 6;
        private const int LABEL_PADDING_V = 3;

        internal static void RenderAnnotations(
            Transform parent,
            List<UIElementInfo> elements,
            Font font,
            UIElementAnnotator.AnnotationBorderMetrics borderMetrics)
        {
            List<AnnotationDrawInfo> drawInfos = new(elements.Count);
            float physicsAnnotationScreenHeight = CalculatePhysicsAnnotationScreenHeight(elements);

            foreach (UIElementInfo element in elements)
            {
                drawInfos.Add(CreateAnnotationDrawInfo(element, physicsAnnotationScreenHeight));
            }

            foreach (AnnotationDrawInfo drawInfo in drawInfos)
            {
                CreateAnnotationBorderForElement(parent, drawInfo, borderMetrics);
            }

            foreach (AnnotationDrawInfo drawInfo in drawInfos)
            {
                CreateAnnotationLabelForElement(parent, drawInfo, font, borderMetrics);
            }
        }

        internal static RaycastOutlineSegment ConvertTopLeftOutlineSegmentToScreenSegment(
            RaycastOutlineSegment segment,
            float screenHeight)
        {
            return new RaycastOutlineSegment(
                segment.StartX,
                screenHeight - segment.StartY,
                segment.EndX,
                screenHeight - segment.EndY);
        }

        internal static Rect CalculateOutlineSegmentRect(RaycastOutlineSegment segment, float thickness)
        {
            Debug.Assert(thickness >= 0f, "Outline thickness must not be negative.");
            bool horizontal = Mathf.Approximately(segment.StartY, segment.EndY);
            bool vertical = Mathf.Approximately(segment.StartX, segment.EndX);
            Debug.Assert(horizontal || vertical, "Raycast outline segments must be axis-aligned.");

            if (horizontal)
            {
                float minX = Mathf.Min(segment.StartX, segment.EndX) - thickness / 2f;
                float width = Mathf.Abs(segment.EndX - segment.StartX) + thickness;
                return new Rect(minX, segment.StartY - thickness / 2f, width, thickness);
            }

            float minY = Mathf.Min(segment.StartY, segment.EndY) - thickness / 2f;
            float height = Mathf.Abs(segment.EndY - segment.StartY) + thickness;
            return new Rect(segment.StartX - thickness / 2f, minY, thickness, height);
        }

        internal static UIElementAnnotator.BorderEdgeRects CalculateBorderEdgeRects(
            float minX, float minY, float maxX, float maxY, float thickness)
        {
            Debug.Assert(maxX >= minX, "maxX must not be smaller than minX.");
            Debug.Assert(maxY >= minY, "maxY must not be smaller than minY.");
            Debug.Assert(thickness >= 0f, "thickness must not be negative.");

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;
            float verticalEdgeHeight = Mathf.Max(0f, boxHeight - thickness * 2f);

            return new UIElementAnnotator.BorderEdgeRects(
                new Rect(minX, maxY - thickness, boxWidth, thickness),
                new Rect(minX, minY, boxWidth, thickness),
                new Rect(minX, minY + thickness, thickness, verticalEdgeHeight),
                new Rect(maxX - thickness, minY + thickness, thickness, verticalEdgeHeight));
        }

        private static void CreateAnnotationBorderForElement(
            Transform parent,
            AnnotationDrawInfo drawInfo,
            UIElementAnnotator.AnnotationBorderMetrics borderMetrics)
        {
            if (drawInfo.OutlineSegments.Count > 0)
            {
                CreateAnnotationOutlineForElement(parent, drawInfo, borderMetrics);
                return;
            }

            CreateBorder(
                parent,
                "LightOuter",
                drawInfo.ScreenMinX - borderMetrics.OuterOffset,
                drawInfo.ScreenMinY - borderMetrics.OuterOffset,
                drawInfo.ScreenMaxX + borderMetrics.OuterOffset,
                drawInfo.ScreenMaxY + borderMetrics.OuterOffset,
                borderMetrics.NeutralThickness,
                drawInfo.BorderColors.Outer);
            CreateBorder(
                parent,
                "ColorMiddle",
                drawInfo.ScreenMinX - borderMetrics.ColorOffset,
                drawInfo.ScreenMinY - borderMetrics.ColorOffset,
                drawInfo.ScreenMaxX + borderMetrics.ColorOffset,
                drawInfo.ScreenMaxY + borderMetrics.ColorOffset,
                borderMetrics.ColorThickness,
                drawInfo.BorderColors.Middle);
            CreateBorder(
                parent,
                "DarkInner",
                drawInfo.ScreenMinX,
                drawInfo.ScreenMinY,
                drawInfo.ScreenMaxX,
                drawInfo.ScreenMaxY,
                borderMetrics.NeutralThickness,
                drawInfo.BorderColors.Inner);
        }

        private static void CreateAnnotationOutlineForElement(
            Transform parent,
            AnnotationDrawInfo drawInfo,
            UIElementAnnotator.AnnotationBorderMetrics borderMetrics)
        {
            float outerThickness = borderMetrics.ColorThickness + borderMetrics.NeutralThickness * 2f;
            CreateOutline(
                parent,
                "LightOuter",
                drawInfo.OutlineSegments,
                outerThickness,
                drawInfo.BorderColors.Outer);
            CreateOutline(
                parent,
                "ColorMiddle",
                drawInfo.OutlineSegments,
                borderMetrics.ColorThickness,
                drawInfo.BorderColors.Middle);
            CreateOutline(
                parent,
                "DarkInner",
                drawInfo.OutlineSegments,
                borderMetrics.NeutralThickness,
                drawInfo.BorderColors.Inner);
        }

        private static void CreateAnnotationLabelForElement(
            Transform parent,
            AnnotationDrawInfo drawInfo,
            Font font,
            UIElementAnnotator.AnnotationBorderMetrics borderMetrics)
        {
            CreateLabel(
                parent,
                drawInfo.DisplayLabel,
                drawInfo.ScreenMinX,
                drawInfo.ScreenMaxY + borderMetrics.OuterOffset + borderMetrics.LabelOutlineDistance + borderMetrics.LabelToBorderGap,
                drawInfo.Color,
                drawInfo.ContrastColor,
                font,
                borderMetrics.LabelOutlineDistance);
        }

        private static AnnotationDrawInfo CreateAnnotationDrawInfo(
            UIElementInfo element,
            float physicsAnnotationScreenHeight)
        {
            Color color = UIElementAnnotationStyling.GetAnnotationColorForElement(element);
            Color contrastColor = UIElementAnnotationStyling.GetContrastingTextColor(color);
            UIElementAnnotator.AnnotationBorderColors borderColors = UIElementAnnotationStyling.GetAnnotationBorderColors(color);
            string displayLabel = UIElementAnnotationStyling.CreateDisplayLabel(element);
            float screenMinX = element.BoundsMinX;
            float screenMinY = element.BoundsMinY;
            float screenMaxX = element.BoundsMaxX;
            float screenMaxY = element.BoundsMaxY;
            List<RaycastOutlineSegment> outlineSegments = element.RaycastOutlineSegments;

            if (IsPhysicsColliderElement(element))
            {
                Debug.Assert(
                    physicsAnnotationScreenHeight >= 0f,
                    "Physics collider annotations require a non-negative Game View height.");
                screenMinY = physicsAnnotationScreenHeight - element.BoundsMaxY;
                screenMaxY = physicsAnnotationScreenHeight - element.BoundsMinY;
                outlineSegments = ConvertTopLeftOutlineSegmentsToScreenSegments(
                    element.RaycastOutlineSegments,
                    physicsAnnotationScreenHeight);
            }

            return new AnnotationDrawInfo(
                screenMinX,
                screenMinY,
                screenMaxX,
                screenMaxY,
                color,
                contrastColor,
                borderColors,
                displayLabel,
                outlineSegments);
        }

        private static float CalculatePhysicsAnnotationScreenHeight(List<UIElementInfo> elements)
        {
            foreach (UIElementInfo element in elements)
            {
                if (!IsPhysicsColliderElement(element))
                {
                    continue;
                }

                return GameViewCoordinateUtility.GetMainGameViewSize().y;
            }

            return 0f;
        }

        private static bool IsPhysicsColliderElement(UIElementInfo element)
        {
            return element.Type == RaycastPhysicsColliderBuilder.PhysicsColliderElementType;
        }

        private static List<RaycastOutlineSegment> ConvertTopLeftOutlineSegmentsToScreenSegments(
            List<RaycastOutlineSegment> outlineSegments,
            float screenHeight)
        {
            List<RaycastOutlineSegment> screenSegments = new(outlineSegments.Count);
            foreach (RaycastOutlineSegment segment in outlineSegments)
            {
                screenSegments.Add(ConvertTopLeftOutlineSegmentToScreenSegment(segment, screenHeight));
            }

            return screenSegments;
        }

        private static void CreateBorder(
            Transform parent, string name,
            float minX, float minY, float maxX, float maxY,
            float thickness, Color color)
        {
            UIElementAnnotator.BorderEdgeRects borderEdgeRects = CalculateBorderEdgeRects(minX, minY, maxX, maxY, thickness);

            CreateBorderEdge(parent, $"{name}_Top", borderEdgeRects.Top, color);
            CreateBorderEdge(parent, $"{name}_Bottom", borderEdgeRects.Bottom, color);
            CreateBorderEdge(parent, $"{name}_Left", borderEdgeRects.Left, color);
            CreateBorderEdge(parent, $"{name}_Right", borderEdgeRects.Right, color);
        }

        private static void CreateOutline(
            Transform parent,
            string name,
            List<RaycastOutlineSegment> outlineSegments,
            float thickness,
            Color color)
        {
            for (int i = 0; i < outlineSegments.Count; i++)
            {
                Rect rect = CalculateOutlineSegmentRect(outlineSegments[i], thickness);
                CreateBorderEdge(parent, $"{name}_Outline_{i}", rect, color);
            }
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
            bgOutline.effectColor = UIElementAnnotationStyling.GetContrastPartnerColor(backgroundColor);
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

        private readonly struct AnnotationDrawInfo
        {
            public readonly float ScreenMinX;
            public readonly float ScreenMinY;
            public readonly float ScreenMaxX;
            public readonly float ScreenMaxY;
            public readonly Color Color;
            public readonly Color ContrastColor;
            public readonly UIElementAnnotator.AnnotationBorderColors BorderColors;
            public readonly string DisplayLabel;
            public readonly List<RaycastOutlineSegment> OutlineSegments;

            public AnnotationDrawInfo(
                float screenMinX,
                float screenMinY,
                float screenMaxX,
                float screenMaxY,
                Color color,
                Color contrastColor,
                UIElementAnnotator.AnnotationBorderColors borderColors,
                string displayLabel,
                List<RaycastOutlineSegment> outlineSegments)
            {
                ScreenMinX = screenMinX;
                ScreenMinY = screenMinY;
                ScreenMaxX = screenMaxX;
                ScreenMaxY = screenMaxY;
                Color = color;
                ContrastColor = contrastColor;
                BorderColors = borderColors;
                DisplayLabel = displayLabel;
                OutlineSegments = outlineSegments;
            }
        }
    }
}
