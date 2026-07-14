#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates 3D physics click candidates for rendering screenshots.
    /// </summary>
    internal static class RaycastGridAnnotator
    {
        internal static List<RaycastLayerSummaryInfo> CollectRaycastLayerSummaries(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            List<RaycastLayerHitSample> samples = CollectLayerHitSamples(
                renderingImageSize,
                imageToInputOffsetY,
                RaycastPhysicsColliderBuilder.CLUSTERED_GRID_ROWS,
                RaycastPhysicsColliderBuilder.CLUSTERED_GRID_COLUMNS);
            return CreateLayerSummaries(samples);
        }

        private static List<RaycastLayerHitSample> CollectLayerHitSamples(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int rowCount,
            int columnCount)
        {
            List<RaycastLayerHitSample> samples = new List<RaycastLayerHitSample>();
            // Sync once for the whole grid; each candidate raycast then reads the same current physics state.
            Physics.SyncTransforms();

            for (int row = 1; row <= rowCount; row++)
            {
                for (int column = 1; column <= columnCount; column++)
                {
                    Vector2 inputPosition = CalculateGridInputPositionForGrid(
                        renderingImageSize,
                        imageToInputOffsetY,
                        rowCount,
                        columnCount,
                        row,
                        column);
                    GameViewRaycastResult raycastResult = GameViewRaycastUtility.RaycastFromInputPosition(
                        inputPosition,
                        UnityCliLoopConstants.RAYCAST_DEFAULT_MAX_DISTANCE,
                        Physics.DefaultRaycastLayers,
                        false);

                    samples.Add(RaycastLayerSummaryBuilder.CreateLayerHitSample(raycastResult));
                }
            }

            return samples;
        }

        internal static Vector2 CalculateGridInputPositionForGrid(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int rowCount,
            int columnCount,
            int row,
            int column)
        {
            Debug.Assert(renderingImageSize.x >= 0f, "Rendering image width must not be negative.");
            Debug.Assert(renderingImageSize.y >= 0f, "Rendering image height must not be negative.");
            Debug.Assert(rowCount > 0, "Grid row count must be positive.");
            Debug.Assert(columnCount > 0, "Grid column count must be positive.");
            Debug.Assert(row >= 1 && row <= rowCount, "Grid row must be within the configured grid.");
            Debug.Assert(column >= 1 && column <= columnCount, "Grid column must be within the configured grid.");

            // Grid points must be visible in the captured PNG, so sample image space before adding the input Y offset.
            return new Vector2(
                renderingImageSize.x * column / (columnCount + 1f),
                imageToInputOffsetY + renderingImageSize.y * row / (rowCount + 1f));
        }

        internal static List<UIElementInfo> CollectPhysicsColliderElements(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int layerMask)
        {
            RaycastClusterCollection clusterCollection = CollectClusterSamples(
                renderingImageSize,
                imageToInputOffsetY,
                layerMask);
            List<RaycastClusterInfo> clusters = RaycastHitClusterer.CreateClusters(clusterCollection.Samples);
            List<UIElementInfo> elements = new List<UIElementInfo>();
            UiRaycastHelper.RaycastContext? uiRaycastContext = CreateUiRaycastContext();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            RaycastSampleCoverage sampleCoverage =
                RaycastPhysicsColliderBuilder.CreateClusterSampleCoverage(renderingImageSize, imageToInputOffsetY);

            for (int i = 0; i < clusters.Count; i++)
            {
                RaycastClusterInfo? reachableCluster = CreateReachableClusterForUiContext(
                    clusters[i],
                    uiRaycastContext,
                    gameViewSize);
                if (reachableCluster == null)
                {
                    continue;
                }

                RaycastColliderMetadata metadata =
                    clusterCollection.MetadataByClusterKey[reachableCluster.Representative.ClusterKey];
                List<UIElementInfo> componentElements = CreateComponentElements(
                    reachableCluster,
                    metadata,
                    sampleCoverage,
                    elements.Count + 1);
                elements.AddRange(componentElements);
            }

            return elements;
        }

        internal static List<UIElementInfo> CreateComponentElements(
            RaycastClusterInfo reachableCluster,
            RaycastColliderMetadata metadata,
            RaycastSampleCoverage sampleCoverage,
            int startLabelNumber)
        {
            return RaycastPhysicsColliderBuilder.CreateComponentElements(
                reachableCluster,
                metadata,
                sampleCoverage,
                startLabelNumber);
        }

        private static RaycastClusterCollection CollectClusterSamples(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int layerMask)
        {
            RaycastClusterCollection clusterCollection = new RaycastClusterCollection();
            // Sync once before the dense pass so every sample reads the same current physics state.
            Physics.SyncTransforms();

            for (int row = 1; row <= RaycastPhysicsColliderBuilder.CLUSTERED_GRID_ROWS; row++)
            {
                for (int column = 1; column <= RaycastPhysicsColliderBuilder.CLUSTERED_GRID_COLUMNS; column++)
                {
                    Vector2 inputPosition = CalculateGridInputPositionForGrid(
                        renderingImageSize,
                        imageToInputOffsetY,
                        RaycastPhysicsColliderBuilder.CLUSTERED_GRID_ROWS,
                        RaycastPhysicsColliderBuilder.CLUSTERED_GRID_COLUMNS,
                        row,
                        column);
                    GameViewRaycastResult raycastResult = GameViewRaycastUtility.RaycastFromInputPosition(
                        inputPosition,
                        UnityCliLoopConstants.RAYCAST_DEFAULT_MAX_DISTANCE,
                        layerMask,
                        false);

                    if (raycastResult.Hits.Length == 0)
                    {
                        continue;
                    }

                    RaycastHit hit = raycastResult.Hits[0];
                    Collider collider = hit.collider;
                    int clusterKey = CreateClusterKey(collider);
                    if (!clusterCollection.MetadataByClusterKey.ContainsKey(clusterKey))
                    {
                        clusterCollection.MetadataByClusterKey.Add(
                            clusterKey,
                            RaycastPhysicsColliderBuilder.CreateColliderMetadata(collider));
                    }

                    clusterCollection.Samples.Add(CreateClusterSample(inputPosition, clusterKey, row, column));
                }
            }

            return clusterCollection;
        }

        internal static int CreateClusterKey(Collider collider)
        {
            Debug.Assert(collider != null, "Collider is required for raycast clustering.");

            // Cluster by GameObject so multi-collider objects share metadata (name, path, layer, components).
            // The 4-connected component split in CollectPhysicsColliderElements re-derives one annotation per
            // visually closed region so a single GameObject can produce multiple entries when UI occlusion
            // splits its reachable samples.
            return collider!.gameObject.GetInstanceID();
        }

        private static RaycastClusterSample CreateClusterSample(
            Vector2 inputPosition,
            int clusterKey,
            int row,
            int column)
        {
            return new RaycastClusterSample
            {
                ClusterKey = clusterKey,
                InputX = inputPosition.x,
                InputY = inputPosition.y,
                Row = row,
                Column = column
            };
        }

        internal static UIElementInfo CreatePhysicsColliderElement(
            string label,
            RaycastClusterInfo cluster,
            RaycastColliderMetadata metadata,
            RaycastSampleCoverage sampleCoverage)
        {
            return RaycastPhysicsColliderBuilder.CreatePhysicsColliderElement(
                label,
                cluster,
                metadata,
                sampleCoverage);
        }

        internal static List<RaycastLayerSummaryInfo> CreateLayerSummaries(List<RaycastLayerHitSample> samples)
        {
            return RaycastLayerSummaryBuilder.CreateLayerSummaries(samples);
        }

        private static UiRaycastHelper.RaycastContext? CreateUiRaycastContext()
        {
            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null || !currentEventSystem.isActiveAndEnabled)
            {
                return null;
            }

            return new UiRaycastHelper.RaycastContext(currentEventSystem);
        }

        private static RaycastClusterInfo? CreateReachableClusterForUiContext(
            RaycastClusterInfo cluster,
            UiRaycastHelper.RaycastContext? uiRaycastContext,
            Vector2 gameViewSize)
        {
            if (uiRaycastContext == null)
            {
                return cluster;
            }

            return RaycastHitClusterer.CreateReachableCluster(
                cluster.Samples,
                (RaycastClusterSample sample) => IsSampleOccludedByUi(sample, uiRaycastContext, gameViewSize));
        }

        private static bool IsSampleOccludedByUi(
            RaycastClusterSample sample,
            UiRaycastHelper.RaycastContext uiRaycastContext,
            Vector2 gameViewSize)
        {
            Vector2 inputPosition = new Vector2(sample.InputX, sample.InputY);
            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);
            return IsUiOcclusionRaycastResult(uiRaycastContext.Raycast(conversion.InjectedUnityPosition));
        }

        internal static bool IsUiOcclusionRaycastResult(RaycastResult? raycastResult)
        {
            return raycastResult != null && raycastResult.Value.module is GraphicRaycaster;
        }
    }
}
