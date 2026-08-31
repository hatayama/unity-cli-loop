#nullable enable
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves annotation colors, contrast, border metrics, and display labels for UI elements.
    /// </summary>
    internal static class UIElementAnnotationStyling
    {
        private const float OUTPUT_BORDER_NEUTRAL_THICKNESS = 2f;
        private const float OUTPUT_BORDER_COLOR_THICKNESS = 4f;
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

        internal static Color GetAnnotationColorForElement(UIElementInfo element)
        {
            Debug.Assert(element != null, "UIElementInfo must not be null.");

            int labelIndex = GetLabelIndex(element!.Label);
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

        internal static UIElementAnnotator.AnnotationBorderColors GetAnnotationBorderColors(Color annotationColor)
        {
            return new UIElementAnnotator.AnnotationBorderColors(DARK_CONTRAST_COLOR, annotationColor, LIGHT_CONTRAST_COLOR);
        }

        internal static UIElementAnnotator.AnnotationBorderMetrics CalculateAnnotationBorderMetrics(float outputResolutionScale)
        {
            Debug.Assert(outputResolutionScale > 0f, "Output resolution scale must be positive.");

            float neutralThickness = OUTPUT_BORDER_NEUTRAL_THICKNESS / outputResolutionScale;
            float colorThickness = OUTPUT_BORDER_COLOR_THICKNESS / outputResolutionScale;
            float labelOutlineDistance = OUTPUT_LABEL_OUTLINE_DISTANCE / outputResolutionScale;
            float labelToBorderGap = OUTPUT_LABEL_TO_BORDER_GAP / outputResolutionScale;

            return new UIElementAnnotator.AnnotationBorderMetrics(
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

            string interaction = element!.Interaction;
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
    }
}
