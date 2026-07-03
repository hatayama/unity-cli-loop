using System.Collections.Generic;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class RaycastGridAnnotatorTests
    {
        [Test]
        public void CreateOverlayElements_WhenPointsIncludeMisses_ShouldAnnotateOnlyHits()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                new RaycastGridPointInfo
                {
                    Label = "R1",
                    Hit = true,
                    InputX = 100f,
                    InputY = 200f,
                    InjectedUnityPositionX = 100f,
                    InjectedUnityPositionY = 880f,
                    HitGameObjectName = "Cube",
                    HitGameObjectPath = "Cube"
                },
                new RaycastGridPointInfo
                {
                    Label = "R2",
                    Hit = false,
                    InputX = 200f,
                    InputY = 300f,
                    InjectedUnityPositionX = 200f,
                    InjectedUnityPositionY = 780f
                }
            };

            List<UIElementInfo> overlayElements = RaycastGridAnnotator.CreateOverlayElements(points);

            Assert.That(overlayElements.Count, Is.EqualTo(1));
            Assert.That(overlayElements[0].Label, Is.EqualTo("R1"));
            Assert.That(overlayElements[0].Name, Is.EqualTo("Cube"));
            Assert.That(overlayElements[0].Type, Is.EqualTo("RaycastHit"));
            Assert.That(overlayElements[0].Interaction, Is.EqualTo("Raycast"));
        }
    }
}
