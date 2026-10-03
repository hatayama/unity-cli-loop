using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies an annotated element that carries raycast outline segments is drawn along those segments
    /// instead of with a rectangular border. The overlay is destroyed in TearDown.
    /// </summary>
    public sealed class UIElementAnnotationRendererOutlineTests
    {
        private GameObject _overlay;

        [TearDown]
        public void TearDown()
        {
            UIElementAnnotator.DestroyAnnotationOverlay(_overlay);
        }

        /// <summary>
        /// Verifies each border layer gets one edge per outline segment and no rectangle edges are drawn.
        /// </summary>
        [Test]
        public void CreateAnnotationOverlay_WithOutlineSegments_DrawsOneEdgePerSegmentForEachLayer()
        {
            UIElementInfo element = new UIElementInfo
            {
                Label = "A",
                Type = "Button",
                Interaction = "Click",
                SimX = 20f,
                SimY = 20f,
                BoundsMinX = 10f,
                BoundsMinY = 10f,
                BoundsMaxX = 30f,
                BoundsMaxY = 30f,
                RaycastOutlineSegments = new List<RaycastOutlineSegment>
                {
                    new RaycastOutlineSegment(10f, 10f, 30f, 10f),
                    new RaycastOutlineSegment(30f, 10f, 30f, 30f)
                }
            };

            _overlay = UIElementAnnotator.CreateAnnotationOverlay(new List<UIElementInfo> { element }, 1f);

            HashSet<string> names = CollectDescendantNames(_overlay.transform);
            foreach (string layer in new[] { "Border_LightOuter", "Border_ColorMiddle", "Border_DarkInner" })
            {
                Assert.That(names, Does.Contain(layer + "_Outline_0"));
                Assert.That(names, Does.Contain(layer + "_Outline_1"));
                Assert.That(names, Does.Not.Contain(layer + "_Outline_2"));
                Assert.That(names, Does.Not.Contain(layer + "_Top"));
            }
        }

        private static HashSet<string> CollectDescendantNames(Transform root)
        {
            HashSet<string> names = new HashSet<string>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                names.Add(child.name);
            }

            return names;
        }
    }
}
