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
        public void CreateClusterKey_WhenGameObjectHasMultipleColliders_ShouldGroupByGameObject()
        {
            GameObject gameObject = new GameObject("MultiColliderPlacementArea");
            try
            {
                BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
                SphereCollider sphereCollider = gameObject.AddComponent<SphereCollider>();

                int boxClusterKey = RaycastGridAnnotator.CreateClusterKey(boxCollider);
                int sphereClusterKey = RaycastGridAnnotator.CreateClusterKey(sphereCollider);

                Assert.That(boxClusterKey, Is.EqualTo(sphereClusterKey));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
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
        public void CreateReachableCluster_WhenSamplesAreOccluded_ShouldExcludeOccludedSamples()
        {
            RaycastClusterSample reachableLeftSample = CreateSample(1, 0f, 0f);
            RaycastClusterSample reachableRightSample = CreateSample(1, 10f, 0f);
            RaycastClusterSample occludedSample = CreateSample(1, 100f, 100f);
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                reachableLeftSample,
                reachableRightSample,
                occludedSample
            };
            HashSet<RaycastClusterSample> occludedSamples = new HashSet<RaycastClusterSample>
            {
                occludedSample
            };

            RaycastClusterInfo reachableCluster = RaycastHitClusterer.CreateReachableCluster(
                samples,
                (RaycastClusterSample sample) => occludedSamples.Contains(sample));

            Assert.That(reachableCluster, Is.Not.Null);
            Assert.That(reachableCluster.SampleCount, Is.EqualTo(2));
            Assert.That(reachableCluster.Samples, Is.EqualTo(new List<RaycastClusterSample>
            {
                reachableLeftSample,
                reachableRightSample
            }));
        }

        [Test]
        public void CreatePhysicsColliderElement_ShouldUseSampleCellBoundsInTopLeftInputSpace()
        {
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = new RaycastClusterSample
                {
                    InputX = 100f,
                    InputY = 200f
                },
                SampleCount = 3,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 80f, 180f),
                    CreateSample(1, 100f, 200f),
                    CreateSample(1, 130f, 220f)
                }
            };
            RaycastColliderMetadata metadata = CreateMetadata();
            RaycastSampleCoverage coverage = CreateCoverage(5f, 10f, 0f, 0f, 200f, 300f);

            UIElementInfo element =
                RaycastGridAnnotator.CreatePhysicsColliderElement("R1", cluster, metadata, coverage);

            Assert.That(element.Type, Is.EqualTo("PhysicsCollider"));
            Assert.That(element.Interaction, Is.EqualTo("Raycast"));
            Assert.That(element.SimX, Is.EqualTo(100f));
            Assert.That(element.SimY, Is.EqualTo(200f));
            Assert.That(element.BoundsMinX, Is.EqualTo(75f));
            Assert.That(element.BoundsMinY, Is.EqualTo(170f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(135f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(230f));
            Assert.That(element.SimX, Is.InRange(element.BoundsMinX, element.BoundsMaxX));
            Assert.That(element.SimY, Is.InRange(element.BoundsMinY, element.BoundsMaxY));
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenSamplesTouchViewportEdge_ShouldClampCellBounds()
        {
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 3f, 4f),
                SampleCount = 1,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 3f, 4f)
                }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 10f, 0f, 0f, 200f, 300f);

            UIElementInfo element = RaycastGridAnnotator.CreatePhysicsColliderElement(
                "R1",
                cluster,
                CreateMetadata(),
                coverage);

            Assert.That(element.BoundsMinX, Is.EqualTo(0f));
            Assert.That(element.BoundsMinY, Is.EqualTo(0f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(8f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(14f));
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenSamplesFormLShape_ShouldUseAxisAlignedCellBoundingBox()
        {
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 0f, 0f),
                SampleCount = 3,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 0f, 0f),
                    CreateSample(1, 10f, 0f),
                    CreateSample(1, 0f, 10f)
                }
            };
            RaycastColliderMetadata metadata = CreateMetadata();
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            UIElementInfo element =
                RaycastGridAnnotator.CreatePhysicsColliderElement("R1", cluster, metadata, coverage);

            Assert.That(element.BoundsMinX, Is.EqualTo(0f));
            Assert.That(element.BoundsMinY, Is.EqualTo(0f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(15f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(15f));
            Assert.That(element.SimX, Is.EqualTo(0f));
            Assert.That(element.SimY, Is.EqualTo(0f));
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenClusterHasSingleSample_ShouldUseOneSampleCellBounds()
        {
            RaycastClusterSample sample = CreateSample(1, 100f, 200f);
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = sample,
                SampleCount = 1,
                Samples = new List<RaycastClusterSample> { sample }
            };
            RaycastColliderMetadata metadata = CreateMetadata();
            RaycastSampleCoverage coverage = CreateCoverage(5f, 10f, 0f, 0f, 200f, 300f);

            UIElementInfo element =
                RaycastGridAnnotator.CreatePhysicsColliderElement("R1", cluster, metadata, coverage);

            Assert.That(element.BoundsMinX, Is.EqualTo(95f));
            Assert.That(element.BoundsMinY, Is.EqualTo(190f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(105f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(210f));
        }

        [Test]
        public void CreateOutlineSegments_WhenSingleCell_ShouldReturnFourEdges()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 20f, 1, 1)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 10f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(4));
            AssertSegment(segments[0], 5f, 10f, 15f, 10f);
            AssertSegment(segments[1], 5f, 30f, 15f, 30f);
            AssertSegment(segments[2], 5f, 10f, 5f, 30f);
            AssertSegment(segments[3], 15f, 10f, 15f, 30f);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsAreAdjacent_ShouldMergeSharedEdges()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 20f, 1, 1),
                CreateSample(1, 20f, 20f, 1, 2)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 10f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(4));
            AssertSegment(segments[0], 5f, 10f, 25f, 10f);
            AssertSegment(segments[1], 5f, 30f, 25f, 30f);
            AssertSegment(segments[2], 5f, 10f, 5f, 30f);
            AssertSegment(segments[3], 25f, 10f, 25f, 30f);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsFormLShape_ShouldKeepConcaveOutline()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2),
                CreateSample(1, 10f, 20f, 2, 1)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(6));
            AssertSegment(segments[0], 5f, 5f, 25f, 5f);
            AssertSegment(segments[1], 15f, 15f, 25f, 15f);
            AssertSegment(segments[2], 5f, 25f, 15f, 25f);
            AssertSegment(segments[3], 5f, 5f, 5f, 25f);
            AssertSegment(segments[4], 15f, 15f, 15f, 25f);
            AssertSegment(segments[5], 25f, 5f, 25f, 15f);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsHaveHole_ShouldReturnInnerAndOuterEdges()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2),
                CreateSample(1, 30f, 10f, 1, 3),
                CreateSample(1, 10f, 20f, 2, 1),
                CreateSample(1, 30f, 20f, 2, 3),
                CreateSample(1, 10f, 30f, 3, 1),
                CreateSample(1, 20f, 30f, 3, 2),
                CreateSample(1, 30f, 30f, 3, 3)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(8));
            Assert.That(ContainsSegment(segments, 15f, 15f, 25f, 15f), Is.True);
            Assert.That(ContainsSegment(segments, 15f, 25f, 25f, 25f), Is.True);
            Assert.That(ContainsSegment(segments, 15f, 15f, 15f, 25f), Is.True);
            Assert.That(ContainsSegment(segments, 25f, 15f, 25f, 25f), Is.True);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsAreDisconnected_ShouldKeepSeparateComponents()
        {
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 30f, 10f, 1, 3)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(8));
            Assert.That(ContainsSegment(segments, 5f, 5f, 15f, 5f), Is.True);
            Assert.That(ContainsSegment(segments, 25f, 5f, 35f, 5f), Is.True);
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenSamplesHaveGridCells_ShouldAttachOutlineSegments()
        {
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 10f, 10f, 1, 1),
                SampleCount = 2,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 10f, 10f, 1, 1),
                    CreateSample(1, 20f, 10f, 1, 2)
                }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            UIElementInfo element = RaycastGridAnnotator.CreatePhysicsColliderElement(
                "R1",
                cluster,
                CreateMetadata(),
                coverage);

            Assert.That(element.RaycastOutlineSegments.Count, Is.EqualTo(4));
            AssertSegment(element.RaycastOutlineSegments[0], 5f, 5f, 25f, 5f);
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

        [Test]
        public void CreateLayerSummaries_ShouldCountHitsByLayerAndSortByHitCount()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                CreateLayerHitPoint("Default", 0, "CubeA"),
                CreateLayerHitPoint("Clickable", 8, "Button"),
                CreateLayerHitPoint("Clickable", 8, "Button"),
                CreateLayerHitPoint("Default", 0, "CubeB"),
                CreateLayerHitPoint("Clickable", 8, "Button")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries.Count, Is.EqualTo(2));
            Assert.That(summaries[0].Layer, Is.EqualTo("Clickable"));
            Assert.That(summaries[0].LayerIndex, Is.EqualTo(8));
            Assert.That(summaries[0].HitCount, Is.EqualTo(3));
            Assert.That(summaries[0].RepresentativeObjectPath, Is.EqualTo("Button"));
            Assert.That(summaries[1].Layer, Is.EqualTo("Default"));
            Assert.That(summaries[1].LayerIndex, Is.EqualTo(0));
            Assert.That(summaries[1].HitCount, Is.EqualTo(2));
        }

        [Test]
        public void CreateLayerSummaries_WhenLayerCountsTie_ShouldSortByLayerIndex()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                CreateLayerHitPoint("Clickable", 8, "Button"),
                CreateLayerHitPoint("Default", 0, "Cube")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].LayerIndex, Is.EqualTo(0));
            Assert.That(summaries[1].LayerIndex, Is.EqualTo(8));
        }

        [Test]
        public void CreateLayerSummaries_ShouldUseMostFrequentObjectPathAsRepresentative()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                CreateLayerHitPoint("Default", 0, "CubeA"),
                CreateLayerHitPoint("Default", 0, "CubeB"),
                CreateLayerHitPoint("Default", 0, "CubeB"),
                CreateLayerHitPoint("Default", 0, "CubeA"),
                CreateLayerHitPoint("Default", 0, "CubeA")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].RepresentativeObjectPath, Is.EqualTo("CubeA"));
        }

        [Test]
        public void CreateLayerSummaries_WhenObjectCountsTie_ShouldUseAlphabeticalPath()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                CreateLayerHitPoint("Default", 0, "CubeB"),
                CreateLayerHitPoint("Default", 0, "CubeA")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].RepresentativeObjectPath, Is.EqualTo("CubeA"));
        }

        [Test]
        public void CreateLayerSummaries_WhenNoHits_ShouldReturnEmptyList()
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>
            {
                new RaycastGridPointInfo
                {
                    Hit = false
                }
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries, Is.Empty);
        }

        private static RaycastClusterSample CreateSample(
            int clusterKey,
            float inputX,
            float inputY,
            int row = 0,
            int column = 0)
        {
            return new RaycastClusterSample
            {
                ClusterKey = clusterKey,
                InputX = inputX,
                InputY = inputY,
                Row = row,
                Column = column
            };
        }

        private static bool ContainsSegment(
            List<RaycastOutlineSegment> segments,
            float startX,
            float startY,
            float endX,
            float endY)
        {
            foreach (RaycastOutlineSegment segment in segments)
            {
                if (segment.StartX == startX &&
                    segment.StartY == startY &&
                    segment.EndX == endX &&
                    segment.EndY == endY)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertSegment(
            RaycastOutlineSegment segment,
            float startX,
            float startY,
            float endX,
            float endY)
        {
            Assert.That(segment.StartX, Is.EqualTo(startX));
            Assert.That(segment.StartY, Is.EqualTo(startY));
            Assert.That(segment.EndX, Is.EqualTo(endX));
            Assert.That(segment.EndY, Is.EqualTo(endY));
        }

        private static RaycastGridPointInfo CreateLayerHitPoint(
            string layer,
            int layerIndex,
            string objectPath)
        {
            return new RaycastGridPointInfo
            {
                Hit = true,
                HitLayer = layer,
                HitLayerIndex = layerIndex,
                HitGameObjectPath = objectPath
            };
        }

        private static RaycastColliderMetadata CreateMetadata()
        {
            return new RaycastColliderMetadata
            {
                Name = "Cube",
                Path = "Cube",
                Layer = "Default",
                Components = new List<string> { "BoxCollider" }
            };
        }

        private static RaycastSampleCoverage CreateCoverage(
            float halfStepX,
            float halfStepY,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            return new RaycastSampleCoverage(halfStepX, halfStepY, minX, minY, maxX, maxY);
        }
    }
}
