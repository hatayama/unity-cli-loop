using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class RaycastGridAnnotatorTests
    {
        [Test]
        public void CalculateGridInputPosition_WhenRenderingHasTopOffset_ShouldSampleInsideCapturedImage()
        {
            Vector2 renderingImageSize = new Vector2(1200f, 1080f);

            Vector2 inputPosition =
                RaycastGridAnnotator.CalculateGridInputPosition(renderingImageSize, 303, 1, 3);

            Assert.That(inputPosition.x, Is.EqualTo(600f));
            Assert.That(inputPosition.y, Is.EqualTo(483f));
        }

        [Test]
        public void CalculateGridInputPosition_WhenRenderingHasTopOffset_ShouldKeepBottomRowVisible()
        {
            Vector2 renderingImageSize = new Vector2(1200f, 1080f);

            Vector2 inputPosition =
                RaycastGridAnnotator.CalculateGridInputPosition(renderingImageSize, 303, 5, 3);

            Assert.That(inputPosition.x, Is.EqualTo(600f));
            Assert.That(inputPosition.y, Is.EqualTo(1203f));
        }

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

        [Test]
        public void Resolve_WhenLayerNamesAreCommaSeparated_ShouldBuildMaskFromKnownLayers()
        {
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Default", Index = 0 },
                new RaycastLayerDefinition { Name = "Ground", Index = 8 },
                new RaycastLayerDefinition { Name = "Clickable", Index = 9 }
            };

            RaycastLayerMaskResolution resolution =
                RaycastLayerMaskResolver.Resolve("Ground, Clickable", availableLayers);

            Assert.That(resolution.IsValid, Is.True);
            Assert.That(resolution.HasLayerNames, Is.True);
            Assert.That(resolution.Mask, Is.EqualTo((1 << 8) | (1 << 9)));
            Assert.That(resolution.LayerNames, Is.EqualTo(new List<string> { "Ground", "Clickable" }));
        }

        [Test]
        public void Resolve_WhenLayerNameDoesNotExist_ShouldReturnInvalidNamesAndValidNames()
        {
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Default", Index = 0 },
                new RaycastLayerDefinition { Name = "Ground", Index = 8 }
            };

            RaycastLayerMaskResolution resolution =
                RaycastLayerMaskResolver.Resolve("Missing, Ground", availableLayers);

            Assert.That(resolution.IsValid, Is.False);
            Assert.That(resolution.InvalidLayerNames, Is.EqualTo(new List<string> { "Missing" }));
            Assert.That(resolution.ValidLayerNames, Is.EqualTo(new List<string> { "Default", "Ground" }));
        }

        [Test]
        public void CreateClusters_WhenSamplesHitSameCollider_ShouldReturnOneCluster()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(1, 10f, 0f),
                CreateSample(1, 0f, 10f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            Assert.That(clusters.Count, Is.EqualTo(1));
            Assert.That(clusters[0].SampleCount, Is.EqualTo(3));
        }

        [Test]
        public void CreateClusters_WhenSamplesHitDifferentColliders_ShouldReturnClusterPerCollider()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(2, 10f, 0f),
                CreateSample(1, 0f, 10f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            Assert.That(clusters.Count, Is.EqualTo(2));
            Assert.That(clusters[0].Representative.ClusterKey, Is.EqualTo(1));
            Assert.That(clusters[1].Representative.ClusterKey, Is.EqualTo(2));
        }

        [Test]
        public void CreateClusters_WhenSamplesFormLShape_ShouldChooseActualHitClosestToCentroid()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(1, 10f, 0f),
                CreateSample(1, 0f, 10f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            Assert.That(clusters[0].Representative.InputX, Is.EqualTo(0f));
            Assert.That(clusters[0].Representative.InputY, Is.EqualTo(0f));
        }

        [Test]
        public void CreateClusters_WhenSamplesFormDonut_ShouldNotSynthesizeCentroidPoint()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 5f),
                CreateSample(1, 5f, 0f),
                CreateSample(1, 10f, 5f),
                CreateSample(1, 5f, 10f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            bool representativeIsActualSample = samples.Exists(
                (RaycastClusterSample sample) =>
                    sample.InputX == clusters[0].Representative.InputX &&
                    sample.InputY == clusters[0].Representative.InputY);
            Assert.That(representativeIsActualSample, Is.True);
            bool representativeIsCentroid =
                clusters[0].Representative.InputX == 5f &&
                clusters[0].Representative.InputY == 5f;
            Assert.That(representativeIsCentroid, Is.False);
        }

        [Test]
        public void SelectReachableRepresentativeSample_WhenNearestSampleIsOccluded_ShouldPromoteNextNearestSample()
        {
            RaycastClusterSample leftSample = CreateSample(1, 0f, 0f);
            RaycastClusterSample nearestSample = CreateSample(1, 5f, 0f);
            RaycastClusterSample farSample = CreateSample(1, 20f, 0f);
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                leftSample,
                nearestSample,
                farSample
            };
            HashSet<RaycastClusterSample> occludedSamples = new HashSet<RaycastClusterSample>
            {
                nearestSample
            };

            RaycastClusterSample representative = RaycastHitClusterer.SelectReachableRepresentativeSample(
                samples,
                (RaycastClusterSample sample) => occludedSamples.Contains(sample));

            Assert.That(representative, Is.SameAs(leftSample));
        }

        [Test]
        public void SelectReachableRepresentativeSample_WhenAllSamplesAreOccluded_ShouldReturnNull()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(1, 5f, 0f),
                CreateSample(1, 20f, 0f)
            };
            HashSet<RaycastClusterSample> occludedSamples = new HashSet<RaycastClusterSample>(samples);

            RaycastClusterSample representative = RaycastHitClusterer.SelectReachableRepresentativeSample(
                samples,
                (RaycastClusterSample sample) => occludedSamples.Contains(sample));

            Assert.That(representative, Is.Null);
        }

        [Test]
        public void CreatePhysicsColliderElement_ShouldKeepBoundsInTopLeftInputSpace()
        {
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = new RaycastClusterSample
                {
                    InputX = 100f,
                    InputY = 200f
                },
                SampleCount = 3
            };
            RaycastColliderMetadata metadata = new RaycastColliderMetadata
            {
                Name = "Cube",
                Path = "Cube",
                Layer = "Default",
                Components = new List<string> { "BoxCollider" }
            };

            UIElementInfo element = RaycastGridAnnotator.CreatePhysicsColliderElement("R1", cluster, metadata);

            Assert.That(element.Type, Is.EqualTo("PhysicsCollider"));
            Assert.That(element.Interaction, Is.EqualTo("Raycast"));
            Assert.That(element.SimX, Is.EqualTo(100f));
            Assert.That(element.SimY, Is.EqualTo(200f));
            Assert.That(element.BoundsMinX, Is.EqualTo(91f));
            Assert.That(element.BoundsMinY, Is.EqualTo(191f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(109f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(209f));
            Assert.That(element.SimX, Is.InRange(element.BoundsMinX, element.BoundsMaxX));
            Assert.That(element.SimY, Is.InRange(element.BoundsMinY, element.BoundsMaxY));
        }

        [Test]
        public void IsUiOcclusionRaycastResult_WhenGraphicRaycasterHit_ShouldReturnTrue()
        {
            GameObject canvasObject = new GameObject("GraphicRaycasterOcclusionTest");
            try
            {
                GraphicRaycaster graphicRaycaster = canvasObject.AddComponent<GraphicRaycaster>();
                RaycastResult raycastResult = new RaycastResult
                {
                    module = graphicRaycaster
                };

                bool isOccluded = RaycastGridAnnotator.IsUiOcclusionRaycastResult(raycastResult);

                Assert.That(isOccluded, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void IsUiOcclusionRaycastResult_WhenPhysicsRaycasterHit_ShouldReturnFalse()
        {
            GameObject cameraObject = new GameObject("PhysicsRaycasterOcclusionTest");
            try
            {
                cameraObject.AddComponent<Camera>();
                PhysicsRaycaster physicsRaycaster = cameraObject.AddComponent<PhysicsRaycaster>();
                RaycastResult raycastResult = new RaycastResult
                {
                    module = physicsRaycaster
                };

                bool isOccluded = RaycastGridAnnotator.IsUiOcclusionRaycastResult(raycastResult);

                Assert.That(isOccluded, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static RaycastClusterSample CreateSample(int clusterKey, float inputX, float inputY)
        {
            return new RaycastClusterSample
            {
                ClusterKey = clusterKey,
                InputX = inputX,
                InputY = inputY
            };
        }
    }
}
