#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Creates 3D physics click candidates for rendering screenshots.
    /// </summary>
    internal static class RaycastGridAnnotator
    {
        private const int GRID_COLUMNS = 5;
        private const int GRID_ROWS = 5;
        private const int CLUSTERED_GRID_COLUMNS = 40;
        private const int CLUSTERED_GRID_ROWS = 40;
        private const float MARKER_SIZE = 18f;

        internal static List<RaycastGridPointInfo> CollectRaycastGridPoints(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            return CollectRaycastGridPointsForGrid(
                renderingImageSize,
                imageToInputOffsetY,
                GRID_ROWS,
                GRID_COLUMNS);
        }

        internal static List<RaycastLayerSummaryInfo> CollectRaycastLayerSummaries(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            List<RaycastGridPointInfo> points = CollectRaycastGridPointsForGrid(
                renderingImageSize,
                imageToInputOffsetY,
                CLUSTERED_GRID_ROWS,
                CLUSTERED_GRID_COLUMNS);
            return CreateLayerSummaries(points);
        }

        private static List<RaycastGridPointInfo> CollectRaycastGridPointsForGrid(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int rowCount,
            int columnCount)
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>();
            int labelIndex = 1;
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
                        McpConstants.RAYCAST_DEFAULT_MAX_DISTANCE,
                        Physics.DefaultRaycastLayers,
                        false);

                    points.Add(CreatePointInfo($"R{labelIndex}", inputPosition, raycastResult));
                    labelIndex++;
                }
            }

            return points;
        }

        internal static Vector2 CalculateGridInputPosition(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int row,
            int column)
        {
            return CalculateGridInputPositionForGrid(
                renderingImageSize,
                imageToInputOffsetY,
                GRID_ROWS,
                GRID_COLUMNS,
                row,
                column);
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
                CreateClusterSampleCoverage(renderingImageSize, imageToInputOffsetY);

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
                elements.Add(CreatePhysicsColliderElement(
                    $"R{elements.Count + 1}",
                    reachableCluster,
                    metadata,
                    sampleCoverage));
            }

            return elements;
        }

        internal static List<UIElementInfo> CreateOverlayElements(List<RaycastGridPointInfo> points)
        {
            List<UIElementInfo> overlayElements = new List<UIElementInfo>();

            foreach (RaycastGridPointInfo point in points)
            {
                if (!point.Hit)
                {
                    continue;
                }

                float halfSize = MARKER_SIZE / 2f;
                overlayElements.Add(new UIElementInfo
                {
                    Label = point.Label,
                    Name = point.HitGameObjectName ?? "",
                    Path = point.HitGameObjectPath ?? "",
                    Type = "RaycastHit",
                    Interaction = "Raycast",
                    SimX = point.InputX,
                    SimY = point.InputY,
                    BoundsMinX = point.InjectedUnityPositionX - halfSize,
                    BoundsMinY = point.InjectedUnityPositionY - halfSize,
                    BoundsMaxX = point.InjectedUnityPositionX + halfSize,
                    BoundsMaxY = point.InjectedUnityPositionY + halfSize,
                    SortingOrder = 0,
                    SiblingIndex = 0
                });
            }

            return overlayElements;
        }

        private static RaycastGridPointInfo CreatePointInfo(
            string label,
            Vector2 inputPosition,
            GameViewRaycastResult raycastResult)
        {
            RaycastGridPointInfo pointInfo = new RaycastGridPointInfo
            {
                Label = label,
                Hit = raycastResult.Hits.Length > 0,
                InputX = inputPosition.x,
                InputY = inputPosition.y,
                InjectedUnityPositionX = raycastResult.Conversion.InjectedUnityPosition.x,
                InjectedUnityPositionY = raycastResult.Conversion.InjectedUnityPosition.y
            };

            if (!pointInfo.Hit)
            {
                return pointInfo;
            }

            RaycastHit hit = raycastResult.Hits[0];
            pointInfo.HitGameObjectName = hit.collider.gameObject.name;
            pointInfo.HitGameObjectPath = GameObjectPathUtility.GetFullPath(hit.collider.gameObject);
            pointInfo.HitLayerIndex = hit.collider.gameObject.layer;
            pointInfo.HitLayer = LayerMask.LayerToName(hit.collider.gameObject.layer);
            pointInfo.Distance = hit.distance;
            return pointInfo;
        }

        internal static List<RaycastLayerSummaryInfo> CreateLayerSummaries(List<RaycastGridPointInfo> points)
        {
            Dictionary<int, RaycastLayerSummaryAccumulator> accumulatorsByLayerIndex =
                new Dictionary<int, RaycastLayerSummaryAccumulator>();

            foreach (RaycastGridPointInfo point in points)
            {
                if (!point.Hit || point.HitLayerIndex == null)
                {
                    continue;
                }

                int layerIndex = point.HitLayerIndex.Value;
                if (!accumulatorsByLayerIndex.ContainsKey(layerIndex))
                {
                    accumulatorsByLayerIndex.Add(
                        layerIndex,
                        new RaycastLayerSummaryAccumulator(point.HitLayer ?? "", layerIndex));
                }

                RaycastLayerSummaryAccumulator accumulator = accumulatorsByLayerIndex[layerIndex];
                accumulator.AddHit(point.HitGameObjectPath ?? "");
            }

            List<RaycastLayerSummaryInfo> summaries = new List<RaycastLayerSummaryInfo>();
            foreach (RaycastLayerSummaryAccumulator accumulator in accumulatorsByLayerIndex.Values)
            {
                summaries.Add(accumulator.CreateSummary());
            }

            summaries.Sort(CompareLayerSummaries);
            return summaries;
        }

        private static int CompareLayerSummaries(
            RaycastLayerSummaryInfo left,
            RaycastLayerSummaryInfo right)
        {
            int hitCountComparison = right.HitCount.CompareTo(left.HitCount);
            if (hitCountComparison != 0)
            {
                return hitCountComparison;
            }

            return left.LayerIndex.CompareTo(right.LayerIndex);
        }

        private sealed class RaycastLayerSummaryAccumulator
        {
            private readonly Dictionary<string, int> _objectHitCounts = new Dictionary<string, int>();
            private string _representativeObjectPath = "";
            private int _representativeObjectHitCount;

            public string Layer { get; }
            public int LayerIndex { get; }
            public int HitCount { get; private set; }

            public RaycastLayerSummaryAccumulator(string layer, int layerIndex)
            {
                Layer = layer;
                LayerIndex = layerIndex;
            }

            public void AddHit(string objectPath)
            {
                HitCount++;
                int objectHitCount = 1;
                if (_objectHitCounts.ContainsKey(objectPath))
                {
                    objectHitCount = _objectHitCounts[objectPath] + 1;
                }

                _objectHitCounts[objectPath] = objectHitCount;
                if (ShouldUseAsRepresentative(objectPath, objectHitCount))
                {
                    _representativeObjectPath = objectPath;
                    _representativeObjectHitCount = objectHitCount;
                }
            }

            public RaycastLayerSummaryInfo CreateSummary()
            {
                return new RaycastLayerSummaryInfo
                {
                    Layer = Layer,
                    LayerIndex = LayerIndex,
                    HitCount = HitCount,
                    RepresentativeObjectPath = _representativeObjectPath
                };
            }

            private bool ShouldUseAsRepresentative(string objectPath, int objectHitCount)
            {
                if (objectHitCount > _representativeObjectHitCount)
                {
                    return true;
                }

                if (objectHitCount < _representativeObjectHitCount)
                {
                    return false;
                }

                return string.CompareOrdinal(objectPath, _representativeObjectPath) < 0;
            }
        }

        private static RaycastClusterCollection CollectClusterSamples(
            Vector2 renderingImageSize,
            int imageToInputOffsetY,
            int layerMask)
        {
            RaycastClusterCollection clusterCollection = new RaycastClusterCollection();
            // Sync once before the dense pass so every sample reads the same current physics state.
            Physics.SyncTransforms();

            for (int row = 1; row <= CLUSTERED_GRID_ROWS; row++)
            {
                for (int column = 1; column <= CLUSTERED_GRID_COLUMNS; column++)
                {
                    Vector2 inputPosition = CalculateGridInputPositionForGrid(
                        renderingImageSize,
                        imageToInputOffsetY,
                        CLUSTERED_GRID_ROWS,
                        CLUSTERED_GRID_COLUMNS,
                        row,
                        column);
                    GameViewRaycastResult raycastResult = GameViewRaycastUtility.RaycastFromInputPosition(
                        inputPosition,
                        McpConstants.RAYCAST_DEFAULT_MAX_DISTANCE,
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
                            CreateColliderMetadata(collider));
                    }

                    clusterCollection.Samples.Add(CreateClusterSample(inputPosition, clusterKey, row, column));
                }
            }

            return clusterCollection;
        }

        internal static int CreateClusterKey(Collider collider)
        {
            Debug.Assert(collider != null, "Collider is required for raycast clustering.");

            // A placement area can use several colliders on one object; one annotation should describe the clickable object.
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

        private static RaycastColliderMetadata CreateColliderMetadata(Collider collider)
        {
            GameObject hitObject = collider.gameObject;
            return new RaycastColliderMetadata
            {
                Name = hitObject.name,
                Path = GameObjectPathUtility.GetFullPath(hitObject),
                Layer = LayerMask.LayerToName(hitObject.layer),
                Components = GetRelevantComponentTypeNames(hitObject)
            };
        }

        internal static UIElementInfo CreatePhysicsColliderElement(
            string label,
            RaycastClusterInfo cluster,
            RaycastColliderMetadata metadata,
            RaycastSampleCoverage sampleCoverage)
        {
            Debug.Assert(cluster.Samples.Count > 0, "Physics collider cluster must contain sampled hits.");
            RaycastClusterSample representative = cluster.Representative;
            RaycastSampleBounds sampleBounds = CalculateSampleCellBounds(cluster.Samples, sampleCoverage);
            List<RaycastOutlineSegment> outlineSegments =
                RaycastSampleOutlineBuilder.CreateOutlineSegments(cluster.Samples, sampleCoverage);

            UIElementInfo element = new UIElementInfo
            {
                Label = label,
                Name = metadata.Name,
                Path = metadata.Path,
                Type = "PhysicsCollider",
                Interaction = "Raycast",
                SimX = representative.InputX,
                SimY = representative.InputY,
                BoundsMinX = sampleBounds.MinX,
                BoundsMinY = sampleBounds.MinY,
                BoundsMaxX = sampleBounds.MaxX,
                BoundsMaxY = sampleBounds.MaxY,
                SortingOrder = 0,
                SiblingIndex = 0,
                Layer = metadata.Layer,
                Components = new List<string>(metadata.Components),
                RaycastOutlineSegments = outlineSegments
            };

            Debug.Assert(
                element.SimX >= element.BoundsMinX &&
                element.SimX <= element.BoundsMaxX &&
                element.SimY >= element.BoundsMinY &&
                element.SimY <= element.BoundsMaxY,
                "Physics collider bounds must use the same top-left input coordinate space as SimX/SimY.");
            return element;
        }

        private static RaycastSampleCoverage CreateClusterSampleCoverage(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            float stepX = renderingImageSize.x / (CLUSTERED_GRID_COLUMNS + 1f);
            float stepY = renderingImageSize.y / (CLUSTERED_GRID_ROWS + 1f);
            return new RaycastSampleCoverage(
                stepX / 2f,
                stepY / 2f,
                0f,
                imageToInputOffsetY,
                renderingImageSize.x,
                imageToInputOffsetY + renderingImageSize.y);
        }

        private static RaycastSampleBounds CalculateSampleCellBounds(
            List<RaycastClusterSample> samples,
            RaycastSampleCoverage sampleCoverage)
        {
            Debug.Assert(samples.Count > 0, "At least one raycast sample is required.");

            float minX = samples[0].InputX - sampleCoverage.HalfStepX;
            float minY = samples[0].InputY - sampleCoverage.HalfStepY;
            float maxX = samples[0].InputX + sampleCoverage.HalfStepX;
            float maxY = samples[0].InputY + sampleCoverage.HalfStepY;

            for (int i = 1; i < samples.Count; i++)
            {
                RaycastClusterSample sample = samples[i];
                minX = Mathf.Min(minX, sample.InputX - sampleCoverage.HalfStepX);
                minY = Mathf.Min(minY, sample.InputY - sampleCoverage.HalfStepY);
                maxX = Mathf.Max(maxX, sample.InputX + sampleCoverage.HalfStepX);
                maxY = Mathf.Max(maxY, sample.InputY + sampleCoverage.HalfStepY);
            }

            return new RaycastSampleBounds(
                Mathf.Clamp(minX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(minY, sampleCoverage.MinY, sampleCoverage.MaxY),
                Mathf.Clamp(maxX, sampleCoverage.MinX, sampleCoverage.MaxX),
                Mathf.Clamp(maxY, sampleCoverage.MinY, sampleCoverage.MaxY));
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

        private static List<string> GetRelevantComponentTypeNames(GameObject hitObject)
        {
            List<string> componentTypeNames = new List<string>();
            HashSet<System.Type> seenTypes = new HashSet<System.Type>();
            Component[] components = hitObject.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                if (!(component is Collider) && !(component is MonoBehaviour))
                {
                    continue;
                }

                System.Type componentType = component.GetType();
                if (seenTypes.Contains(componentType))
                {
                    continue;
                }

                seenTypes.Add(componentType);
                componentTypeNames.Add(componentType.Name);
            }

            return componentTypeNames;
        }

        private readonly struct RaycastSampleBounds
        {
            public readonly float MinX;
            public readonly float MinY;
            public readonly float MaxX;
            public readonly float MaxY;

            public RaycastSampleBounds(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }
    }
}
