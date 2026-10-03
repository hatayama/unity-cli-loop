using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the annotation styling fallbacks: the color for labels that are not letter labels, the drop
    /// interaction, and display labels built from a missing interaction or label.
    /// </summary>
    public sealed class UIElementAnnotationStylingEdgeTests
    {
        private static readonly Color FallbackColor = new Color(1f, 1f, 0f, 0.9f);

        /// <summary>
        /// Verifies an element without a label gets the fallback color.
        /// </summary>
        [Test]
        public void GetAnnotationColorForElement_WithoutALabel_UsesTheFallbackColor()
        {
            Color color = UIElementAnnotationStyling.GetAnnotationColorForElement(new UIElementInfo { Label = "" });

            Assert.That(color, Is.EqualTo(FallbackColor));
        }

        /// <summary>
        /// Verifies a label with a character outside A to Z gets the fallback color.
        /// </summary>
        [Test]
        public void GetAnnotationColorForElement_WithANonLetterLabel_UsesTheFallbackColor()
        {
            Color color = UIElementAnnotationStyling.GetAnnotationColorForElement(new UIElementInfo { Label = "B1" });

            Assert.That(color, Is.EqualTo(FallbackColor));
        }

        /// <summary>
        /// Verifies a drop target is annotated with the drop interaction.
        /// </summary>
        [Test]
        public void GetInteractionForType_WithADropTarget_ReturnsDrop()
        {
            Assert.That(UIElementAnnotationStyling.GetInteractionForType("DropTarget"), Is.EqualTo("Drop"));
        }

        /// <summary>
        /// Verifies a missing interaction is derived from the element type.
        /// </summary>
        [Test]
        public void CreateDisplayLabel_WithoutAnInteraction_DerivesItFromTheType()
        {
            UIElementInfo element = new UIElementInfo { Label = "C", Type = "Slider", Interaction = "" };

            Assert.That(UIElementAnnotationStyling.CreateDisplayLabel(element), Is.EqualTo("C / DRAG"));
        }

        /// <summary>
        /// Verifies an element without a label shows only its interaction.
        /// </summary>
        [Test]
        public void CreateDisplayLabel_WithoutALabel_ShowsOnlyTheInteraction()
        {
            UIElementInfo element = new UIElementInfo { Label = "", Type = "Button", Interaction = "Click" };

            Assert.That(UIElementAnnotationStyling.CreateDisplayLabel(element), Is.EqualTo("CLICK"));
        }
    }
}
