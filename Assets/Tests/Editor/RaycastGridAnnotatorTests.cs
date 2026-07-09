#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Raycast Grid Annotator behavior.
    /// </summary>
    public class RaycastGridAnnotatorTests
    {
        [Test]
        public void CalculateGridInputPositionForGrid_WhenRenderingHasTopOffset_ShouldSampleInsideCapturedImage()
        {
            // Tests that a grid input position samples inside the captured image when rendering has a top offset.
            Vector2 renderingImageSize = new Vector2(1200f, 1080f);

            Vector2 inputPosition =
                RaycastGridAnnotator.CalculateGridInputPositionForGrid(renderingImageSize, 303, 5, 5, 1, 3);

            Assert.That(inputPosition.x, Is.EqualTo(600f));
            Assert.That(inputPosition.y, Is.EqualTo(483f));
        }

        [Test]
        public void CalculateGridInputPositionForGrid_WhenRenderingHasTopOffset_ShouldKeepBottomRowVisible()
        {
            // Tests that the bottom grid row stays visible within the rendering image when a top offset is applied.
            Vector2 renderingImageSize = new Vector2(1200f, 1080f);

            Vector2 inputPosition =
                RaycastGridAnnotator.CalculateGridInputPositionForGrid(renderingImageSize, 303, 5, 5, 5, 3);

            Assert.That(inputPosition.x, Is.EqualTo(600f));
            Assert.That(inputPosition.y, Is.EqualTo(1203f));
        }

        [Test]
        public void Resolve_WhenLayerNamesAreCommaSeparated_ShouldBuildMaskFromKnownLayers()
        {
            // Tests that comma-separated known layer names resolve into the combined layer mask.
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
            // Tests that an unknown layer name is reported as invalid alongside the list of valid layer names.
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Default", Index = 0 },
                new RaycastLayerDefinition { Name = "Ground", Index = 8 }
            };

            RaycastLayerMaskResolution resolution =
                RaycastLayerMaskResolver.Resolve("Missing, Ground", availableLayers);

            Assert.That(resolution.IsValid, Is.False);
            Assert.That(resolution.HasLayerNames, Is.True);
            Assert.That(resolution.InvalidLayerNames, Is.EqualTo(new List<string> { "Missing" }));
            Assert.That(resolution.ValidLayerNames, Is.EqualTo(new List<string> { "Default", "Ground" }));
        }

        [Test]
        public void CreateLayerNamesFromMask_WhenMaskMatchesSpecificLayers_ShouldReturnOnlyThoseLayerNames()
        {
            // Tests that only the layer names whose bits are set in the mask are returned.
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Default", Index = 0 },
                new RaycastLayerDefinition { Name = "Ground", Index = 8 },
                new RaycastLayerDefinition { Name = "Clickable", Index = 9 }
            };
            int mask = (1 << 8) | (1 << 9);

            List<string> layerNames = RaycastLayerMaskResolver.CreateLayerNamesFromMask(mask, availableLayers);

            Assert.That(layerNames, Is.EqualTo(new List<string> { "Ground", "Clickable" }));
        }

        [Test]
        public void CreateLayerNamesFromMask_WhenMaskIsDefaultRaycastLayers_ShouldExcludeIgnoreRaycastLayer()
        {
            // Tests that Physics.DefaultRaycastLayers excludes the built-in Ignore Raycast layer (index 2).
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Default", Index = 0 },
                new RaycastLayerDefinition { Name = "Ignore Raycast", Index = 2 },
                new RaycastLayerDefinition { Name = "Ground", Index = 8 }
            };

            List<string> layerNames = RaycastLayerMaskResolver.CreateLayerNamesFromMask(
                Physics.DefaultRaycastLayers, availableLayers);

            Assert.That(layerNames, Is.EqualTo(new List<string> { "Default", "Ground" }));
        }

        [Test]
        public void CreateLayerNamesFromMask_WhenMaskHasNoMatchingLayers_ShouldReturnEmptyList()
        {
            // Tests that a mask with no bits overlapping the available layers returns an empty list.
            List<RaycastLayerDefinition> availableLayers = new List<RaycastLayerDefinition>
            {
                new RaycastLayerDefinition { Name = "Ground", Index = 8 }
            };

            List<string> layerNames = RaycastLayerMaskResolver.CreateLayerNamesFromMask(0, availableLayers);

            Assert.That(layerNames, Is.Empty);
        }

        [Test]
        public void CreateClusters_WhenSamplesHitSameCollider_ShouldReturnOneCluster()
        {
            // Tests that samples sharing the same cluster key are grouped into a single cluster.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 100f, 100f),
                CreateSample(1, 110f, 100f),
                CreateSample(1, 100f, 110f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            Assert.That(clusters.Count, Is.EqualTo(1));
            Assert.That(clusters[0].SampleCount, Is.EqualTo(3));
        }

        [Test]
        public void CreateClusters_WhenSamplesHitDifferentColliders_ShouldReturnClusterPerCollider()
        {
            // Tests that samples with distinct cluster keys produce one cluster per collider.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 100f, 100f),
                CreateSample(2, 300f, 300f)
            };

            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(samples);

            Assert.That(clusters.Count, Is.EqualTo(2));
        }

        [Test]
        public void CreateClusterKey_WhenGameObjectHasMultipleColliders_ShouldGroupByGameObject()
        {
            // Tests that the cluster key groups multiple colliders on the same GameObject together.
            GameObject gameObject = new GameObject("ClusterKeyTest");
            try
            {
                BoxCollider firstCollider = gameObject.AddComponent<BoxCollider>();
                SphereCollider secondCollider = gameObject.AddComponent<SphereCollider>();

                int firstKey = RaycastGridAnnotator.CreateClusterKey(firstCollider);
                int secondKey = RaycastGridAnnotator.CreateClusterKey(secondCollider);

                Assert.That(firstKey, Is.EqualTo(secondKey));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateClusters_WhenSamplesFormLShape_ShouldChooseActualHitClosestToCentroid()
        {
            // Tests that the cluster representative is an actual sampled hit closest to the centroid, not a synthesized point.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(1, 100f, 0f),
                CreateSample(1, 0f, 100f)
            };

            RaycastClusterSample representative = RaycastHitClusterer.SelectRepresentativeSample(samples);

            Assert.That(samples, Has.Member(representative));
        }

        [Test]
        public void CreateClusters_WhenSamplesFormDonut_ShouldNotSynthesizeCentroidPoint()
        {
            // Tests that the representative sample for a donut-shaped cluster is a real sample, not the empty centroid.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 0f, 0f),
                CreateSample(1, 100f, 0f),
                CreateSample(1, 100f, 100f),
                CreateSample(1, 0f, 100f)
            };

            RaycastClusterSample representative = RaycastHitClusterer.SelectRepresentativeSample(samples);

            Assert.That(samples, Has.Member(representative));
        }

        [Test]
        public void SelectReachableRepresentativeSample_WhenNearestSampleIsOccluded_ShouldPromoteNextNearestSample()
        {
            // Tests that occluded samples are skipped so the next nearest reachable sample becomes representative.
            RaycastClusterSample occludedNearestSample = CreateSample(1, 50f, 50f);
            RaycastClusterSample reachableSample = CreateSample(1, 40f, 40f);
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                occludedNearestSample,
                reachableSample
            };
            HashSet<RaycastClusterSample> occludedSamples = new HashSet<RaycastClusterSample>
            {
                occludedNearestSample
            };

            RaycastClusterSample? representative = RaycastHitClusterer.SelectReachableRepresentativeSample(
                samples,
                (RaycastClusterSample sample) => occludedSamples.Contains(sample));

            Assert.That(representative, Is.EqualTo(reachableSample));
        }

        [Test]
        public void SelectReachableRepresentativeSample_WhenAllSamplesAreOccluded_ShouldReturnNull()
        {
            // Tests that no representative is selected when every sample in the cluster is occluded.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 50f, 50f),
                CreateSample(1, 60f, 60f)
            };

            RaycastClusterSample? representative = RaycastHitClusterer.SelectReachableRepresentativeSample(
                samples,
                (RaycastClusterSample sample) => true);

            Assert.That(representative, Is.Null);
        }

        [Test]
        public void CreateReachableCluster_WhenSamplesAreOccluded_ShouldExcludeOccludedSamples()
        {
            // Tests that the reachable cluster only contains samples that were not reported as occluded.
            RaycastClusterSample occludedSample = CreateSample(1, 50f, 50f);
            RaycastClusterSample reachableLeftSample = CreateSample(1, 40f, 40f);
            RaycastClusterSample reachableRightSample = CreateSample(1, 60f, 40f);
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                occludedSample,
                reachableLeftSample,
                reachableRightSample
            };
            HashSet<RaycastClusterSample> occludedSamples = new HashSet<RaycastClusterSample>
            {
                occludedSample
            };

            RaycastClusterInfo? reachableCluster = RaycastHitClusterer.CreateReachableCluster(
                samples,
                (RaycastClusterSample sample) => occludedSamples.Contains(sample));

            Assert.That(reachableCluster, Is.Not.Null);
            Assert.That(reachableCluster!.SampleCount, Is.EqualTo(2));
            Assert.That(reachableCluster.Samples, Is.EqualTo(new List<RaycastClusterSample>
            {
                reachableLeftSample,
                reachableRightSample
            }));
        }

        [Test]
        public void CreatePhysicsColliderElement_ShouldUseSampleCellBoundsInTopLeftInputSpace()
        {
            // Tests that a physics collider element uses the sampled cell bounds in top-left input space.
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
            // Tests that cell bounds touching the viewport edge are clamped to the sample coverage area.
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
            // Tests that an L-shaped sample cluster produces an axis-aligned bounding box covering all cells.
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 0f, 0f),
                SampleCount = 3,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 0f, 0f),
                    CreateSample(1, 20f, 0f),
                    CreateSample(1, 0f, 20f)
                }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, -10f, -10f, 100f, 100f);

            UIElementInfo element = RaycastGridAnnotator.CreatePhysicsColliderElement(
                "R1",
                cluster,
                CreateMetadata(),
                coverage);

            Assert.That(element.BoundsMinX, Is.EqualTo(-5f));
            Assert.That(element.BoundsMinY, Is.EqualTo(-5f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(25f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(25f));
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenClusterHasSingleSample_ShouldUseOneSampleCellBounds()
        {
            // Tests that a single-sample cluster produces bounds sized to exactly one sample cell.
            RaycastClusterInfo cluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 50f, 50f),
                SampleCount = 1,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 50f, 50f)
                }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            UIElementInfo element = RaycastGridAnnotator.CreatePhysicsColliderElement(
                "R1",
                cluster,
                CreateMetadata(),
                coverage);

            Assert.That(element.BoundsMinX, Is.EqualTo(45f));
            Assert.That(element.BoundsMinY, Is.EqualTo(45f));
            Assert.That(element.BoundsMaxX, Is.EqualTo(55f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(55f));
        }

        [Test]
        public void CreateOutlineSegments_WhenSingleCell_ShouldReturnFourEdges()
        {
            // Tests that a single occupied cell produces exactly four boundary edges.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(4));
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsAreAdjacent_ShouldMergeSharedEdges()
        {
            // Tests that adjacent occupied cells merge their shared internal edge into fewer outline segments.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(4));
            Assert.That(ContainsSegment(segments, 5f, 5f, 25f, 5f), Is.True);
            Assert.That(ContainsSegment(segments, 5f, 15f, 25f, 15f), Is.True);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsFormLShape_ShouldKeepConcaveOutline()
        {
            // Tests that an L-shaped set of cells keeps its concave outline instead of collapsing to a rectangle.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2),
                CreateSample(1, 10f, 20f, 2, 1)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(ContainsSegment(segments, 15f, 15f, 15f, 25f), Is.True);
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsHaveHole_ShouldReturnInnerAndOuterEdges()
        {
            // Tests that a donut-shaped set of cells with a missing center cell produces both inner and outer edges.
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

            Assert.That(ContainsSegment(segments, 15f, 15f, 25f, 15f), Is.True);
            Assert.That(segments.Count, Is.GreaterThan(4));
        }

        [Test]
        public void CreateOutlineSegments_WhenCellsAreDisconnected_ShouldKeepSeparateComponents()
        {
            // Tests that disconnected groups of cells produce separate outline components instead of merging.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 90f, 90f, 9, 9)
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 100f, 100f);

            List<RaycastOutlineSegment> segments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(samples, coverage);

            Assert.That(segments.Count, Is.EqualTo(8));
        }

        [Test]
        public void CreatePhysicsColliderElement_WhenSamplesHaveGridCells_ShouldAttachOutlineSegments()
        {
            // Tests that a physics collider element created from grid-cell samples carries outline segments.
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
            // Tests that a raycast result routed through a GraphicRaycaster is treated as a UI occlusion.
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
            // Tests that a raycast result routed through a PhysicsRaycaster is not treated as a UI occlusion.
            GameObject cameraObject = new GameObject("PhysicsRaycasterOcclusionTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                PhysicsRaycaster physicsRaycaster = cameraObject.AddComponent<PhysicsRaycaster>();
                RaycastResult raycastResult = new RaycastResult
                {
                    module = physicsRaycaster
                };

                bool isOccluded = RaycastGridAnnotator.IsUiOcclusionRaycastResult(raycastResult);

                Assert.That(isOccluded, Is.False);
                Assert.That(camera, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateLayerSummaries_ShouldCountHitsByLayerAndSortByHitCount()
        {
            // Tests that layer summaries count hits per layer and sort by descending hit count.
            List<RaycastLayerHitSample> points = new List<RaycastLayerHitSample>
            {
                CreateLayerHitPoint("Ground", 8, "Ground/A"),
                CreateLayerHitPoint("Ground", 8, "Ground/B"),
                CreateLayerHitPoint("Clickable", 9, "Clickable/A")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries.Count, Is.EqualTo(2));
            Assert.That(summaries[0].Layer, Is.EqualTo("Ground"));
            Assert.That(summaries[0].HitCount, Is.EqualTo(2));
            Assert.That(summaries[1].Layer, Is.EqualTo("Clickable"));
            Assert.That(summaries[1].HitCount, Is.EqualTo(1));
        }

        [Test]
        public void CreateLayerSummaries_WhenLayerCountsTie_ShouldSortByLayerIndex()
        {
            // Tests that tied hit counts fall back to sorting by ascending layer index.
            List<RaycastLayerHitSample> points = new List<RaycastLayerHitSample>
            {
                CreateLayerHitPoint("Clickable", 9, "Clickable/A"),
                CreateLayerHitPoint("Ground", 8, "Ground/A")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].Layer, Is.EqualTo("Ground"));
            Assert.That(summaries[1].Layer, Is.EqualTo("Clickable"));
        }

        [Test]
        public void CreateLayerSummaries_ShouldUseMostFrequentObjectPathAsRepresentative()
        {
            // Tests that the representative object path is the most frequently hit path within the layer.
            List<RaycastLayerHitSample> points = new List<RaycastLayerHitSample>
            {
                CreateLayerHitPoint("Ground", 8, "Ground/A"),
                CreateLayerHitPoint("Ground", 8, "Ground/A"),
                CreateLayerHitPoint("Ground", 8, "Ground/B")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].RepresentativeObjectPath, Is.EqualTo("Ground/A"));
        }

        [Test]
        public void CreateLayerSummaries_WhenObjectCountsTie_ShouldUseAlphabeticalPath()
        {
            // Tests that a tie in per-object hit counts is broken by picking the alphabetically first path.
            List<RaycastLayerHitSample> points = new List<RaycastLayerHitSample>
            {
                CreateLayerHitPoint("Ground", 8, "Ground/B"),
                CreateLayerHitPoint("Ground", 8, "Ground/A")
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries[0].RepresentativeObjectPath, Is.EqualTo("Ground/A"));
        }

        [Test]
        public void CreateLayerSummaries_WhenNoHits_ShouldReturnEmptyList()
        {
            // Tests that no layer summaries are produced when none of the grid points registered a hit.
            List<RaycastLayerHitSample> points = new List<RaycastLayerHitSample>
            {
                new RaycastLayerHitSample
                {
                    Hit = false
                }
            };

            List<RaycastLayerSummaryInfo> summaries = RaycastGridAnnotator.CreateLayerSummaries(points);

            Assert.That(summaries, Is.Empty);
        }

        [Test]
        public void SplitIntoConnectedComponents_WhenSamplesForm4ConnectedRegion_ShouldReturnOneComponent()
        {
            // Tests that an L-shaped set of 4-connected samples collapses into a single connected component.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2),
                CreateSample(1, 10f, 20f, 2, 1)
            };

            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(samples);

            Assert.That(components.Count, Is.EqualTo(1));
            Assert.That(components[0].Count, Is.EqualTo(3));
        }

        [Test]
        public void SplitIntoConnectedComponents_WhenSamplesFormTwoDisconnectedRegions_ShouldReturnTwoComponents()
        {
            // Tests that two spatially disconnected 4-connected regions come back as two separate components.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 10f, 1, 2),
                CreateSample(1, 50f, 50f, 5, 5),
                CreateSample(1, 60f, 50f, 5, 6),
                CreateSample(1, 60f, 60f, 6, 6)
            };

            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(samples);

            Assert.That(components.Count, Is.EqualTo(2));
            List<int> sizes = new List<int> { components[0].Count, components[1].Count };
            sizes.Sort();
            Assert.That(sizes, Is.EqualTo(new List<int> { 2, 3 }));
        }

        [Test]
        public void SplitIntoConnectedComponents_WhenSamplesAreOnlyDiagonallyAdjacent_ShouldReturnSeparateComponents()
        {
            // Tests that diagonal-only adjacency is not treated as 4-connectivity, so two samples become two components.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f, 1, 1),
                CreateSample(1, 20f, 20f, 2, 2)
            };

            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(samples);

            Assert.That(components.Count, Is.EqualTo(2));
            Assert.That(components[0].Count, Is.EqualTo(1));
            Assert.That(components[1].Count, Is.EqualTo(1));
        }

        [Test]
        public void SplitIntoConnectedComponents_WhenSamplesHaveNoGridCell_ShouldTreatEachAsSeparateComponent()
        {
            // Tests that samples without a grid cell (Row/Column <= 0) each become their own component instead of collapsing.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>
            {
                CreateSample(1, 10f, 10f),
                CreateSample(1, 20f, 20f),
                CreateSample(1, 30f, 30f)
            };

            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(samples);

            Assert.That(components.Count, Is.EqualTo(3));
            foreach (List<RaycastClusterSample> component in components)
            {
                Assert.That(component.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SplitIntoConnectedComponents_WhenInputIsEmpty_ShouldReturnEmpty()
        {
            // Tests that an empty sample list produces an empty component list without exceptions.
            List<RaycastClusterSample> samples = new List<RaycastClusterSample>();

            List<List<RaycastClusterSample>> components =
                RaycastHitClusterer.SplitIntoConnectedComponents(samples);

            Assert.That(components, Is.Empty);
        }

        [Test]
        public void CreateComponentElements_WhenReachableClusterSplitsIntoTwoRegions_ShouldEmitTwoEntriesWithSharedMetadataAndDistinctBoundsAndSim()
        {
            // Tests that a single reachable cluster split into two 4-connected regions produces two element entries
            // that share metadata (Name/Path/Layer/Components) but carry distinct Label/Bounds/SimX/SimY/RaycastOutlineSegments.
            RaycastClusterInfo reachableCluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 10f, 10f, 1, 1),
                SampleCount = 4,
                Samples = new List<RaycastClusterSample>
                {
                    CreateSample(1, 10f, 10f, 1, 1),
                    CreateSample(1, 20f, 10f, 1, 2),
                    CreateSample(1, 80f, 80f, 8, 8),
                    CreateSample(1, 90f, 80f, 8, 9)
                }
            };
            RaycastColliderMetadata metadata = new RaycastColliderMetadata
            {
                Name = "SplitGrid",
                Path = "Root/SplitGrid",
                Layer = "Default",
                Components = new List<string> { "BoxCollider" }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 200f, 200f);

            List<UIElementInfo> elements =
                RaycastGridAnnotator.CreateComponentElements(reachableCluster, metadata, coverage, 3);

            Assert.That(elements.Count, Is.EqualTo(2));

            // Shared metadata across component entries.
            Assert.That(elements[0].Name, Is.EqualTo("SplitGrid"));
            Assert.That(elements[1].Name, Is.EqualTo("SplitGrid"));
            Assert.That(elements[0].Path, Is.EqualTo("Root/SplitGrid"));
            Assert.That(elements[1].Path, Is.EqualTo("Root/SplitGrid"));
            Assert.That(elements[0].Layer, Is.EqualTo("Default"));
            Assert.That(elements[1].Layer, Is.EqualTo("Default"));
            Assert.That(elements[0].Components, Is.EqualTo(elements[1].Components));

            // Continuous labels starting at startLabelNumber.
            Assert.That(elements[0].Label, Is.EqualTo("R3"));
            Assert.That(elements[1].Label, Is.EqualTo("R4"));

            // Top-left first: the (1,1)-(1,2) region must precede the (8,8)-(8,9) region.
            Assert.That(elements[0].BoundsMinX, Is.LessThan(elements[1].BoundsMinX));
            Assert.That(elements[0].BoundsMinY, Is.LessThan(elements[1].BoundsMinY));

            // Distinct Sim positions.
            Assert.That(elements[0].SimX, Is.Not.EqualTo(elements[1].SimX));
            Assert.That(elements[0].SimY, Is.Not.EqualTo(elements[1].SimY));

            // Both entries must have their own outline segments.
            Assert.That(elements[0].RaycastOutlineSegments.Count, Is.GreaterThan(0));
            Assert.That(elements[1].RaycastOutlineSegments.Count, Is.GreaterThan(0));
        }

        [Test]
        public void CreateComponentElements_WhenComponentsShareMinInputYAndMinInputX_ShouldOrderByMinRowColumnLexicographically()
        {
            // Tests that when two components share the same min InputY and min InputX, the tie is broken by
            // the lexicographic minimum (Row, Column). This guarantees fully deterministic label assignment.
            // Component A: single cell at (Row=1, Column=1) -> min InputY=10, min InputX=10, min (Row,Column)=(1,1).
            // Component B: L-shape reaching down to (Row=3, Column=1) -> min InputY=10, min InputX=10, min (Row,Column)=(1,3).
            RaycastClusterInfo reachableCluster = new RaycastClusterInfo
            {
                Representative = CreateSample(1, 10f, 10f, 1, 1),
                SampleCount = 6,
                Samples = new List<RaycastClusterSample>
                {
                    // Component A: isolated single cell (1,1).
                    CreateSample(1, 10f, 10f, 1, 1),
                    // Component B: (1,3) -> (2,3) -> (3,3) -> (3,2) -> (3,1). Its column 1 cell sits at Row=3.
                    CreateSample(1, 30f, 10f, 1, 3),
                    CreateSample(1, 30f, 20f, 2, 3),
                    CreateSample(1, 30f, 30f, 3, 3),
                    CreateSample(1, 20f, 30f, 3, 2),
                    CreateSample(1, 10f, 30f, 3, 1)
                }
            };
            RaycastColliderMetadata metadata = new RaycastColliderMetadata
            {
                Name = "TieBreaker",
                Path = "Root/TieBreaker",
                Layer = "Default",
                Components = new List<string> { "BoxCollider" }
            };
            RaycastSampleCoverage coverage = CreateCoverage(5f, 5f, 0f, 0f, 200f, 200f);

            List<UIElementInfo> elements =
                RaycastGridAnnotator.CreateComponentElements(reachableCluster, metadata, coverage, 1);

            Assert.That(elements.Count, Is.EqualTo(2));
            // Component A wins the third key: (1,1) < (1,3) lexicographically, so it must receive R1.
            Assert.That(elements[0].Label, Is.EqualTo("R1"));
            Assert.That(elements[1].Label, Is.EqualTo("R2"));
            // Sanity: R1 has one cell (small bounds), R2 has the L-shape (wider bounds).
            float widthA = elements[0].BoundsMaxX - elements[0].BoundsMinX;
            float widthB = elements[1].BoundsMaxX - elements[1].BoundsMinX;
            Assert.That(widthA, Is.LessThan(widthB));
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

        private static RaycastLayerHitSample CreateLayerHitPoint(
            string layer,
            int layerIndex,
            string objectPath)
        {
            return new RaycastLayerHitSample
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
