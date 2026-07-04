#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Creates 3D physics click candidates for rendering screenshots.
    /// </summary>
    internal static class RaycastGridAnnotator
    {
        private const int GRID_COLUMNS = 5;
        private const int GRID_ROWS = 5;
        private const int CLUSTERED_GRID_COLUMNS = 20;
        private const int CLUSTERED_GRID_ROWS = 20;
        private const float MARKER_SIZE = 18f;

        internal static List<RaycastGridPointInfo> CollectRaycastGridPoints(
            Vector2 renderingImageSize,
            int imageToInputOffsetY)
        {
            List<RaycastGridPointInfo> points = new List<RaycastGridPointInfo>();
            int labelIndex = 1;
            // Sync once for the whole grid; each candidate raycast then reads the same current physics state.
            Physics.SyncTransforms();

            for (int row = 1; row <= GRID_ROWS; row++)
            {
                for (int column = 1; column <= GRID_COLUMNS; column++)
                {
                    Vector2 inputPosition = CalculateGridInputPosition(
                        renderingImageSize,
                        imageToInputOffsetY,
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

            for (int i = 0; i < clusters.Count; i++)
            {
                RaycastColliderMetadata metadata =
                    clusterCollection.MetadataByClusterKey[clusters[i].Representative.ClusterKey];
                elements.Add(CreatePhysicsColliderElement($"R{i + 1}", clusters[i], metadata));
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
            pointInfo.Distance = hit.distance;
            return pointInfo;
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
                    int clusterKey = collider.GetInstanceID();
                    if (!clusterCollection.MetadataByClusterKey.ContainsKey(clusterKey))
                    {
                        clusterCollection.MetadataByClusterKey.Add(
                            clusterKey,
                            CreateColliderMetadata(collider));
                    }

                    clusterCollection.Samples.Add(CreateClusterSample(inputPosition, clusterKey));
                }
            }

            return clusterCollection;
        }

        private static RaycastClusterSample CreateClusterSample(
            Vector2 inputPosition,
            int clusterKey)
        {
            return new RaycastClusterSample
            {
                ClusterKey = clusterKey,
                InputX = inputPosition.x,
                InputY = inputPosition.y
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
            RaycastColliderMetadata metadata)
        {
            RaycastClusterSample representative = cluster.Representative;
            float halfSize = MARKER_SIZE / 2f;

            UIElementInfo element = new UIElementInfo
            {
                Label = label,
                Name = metadata.Name,
                Path = metadata.Path,
                Type = "PhysicsCollider",
                Interaction = "Raycast",
                SimX = representative.InputX,
                SimY = representative.InputY,
                BoundsMinX = representative.InputX - halfSize,
                BoundsMinY = representative.InputY - halfSize,
                BoundsMaxX = representative.InputX + halfSize,
                BoundsMaxY = representative.InputY + halfSize,
                SortingOrder = 0,
                SiblingIndex = 0,
                Layer = metadata.Layer,
                Components = new List<string>(metadata.Components)
            };

            Debug.Assert(
                element.SimX >= element.BoundsMinX &&
                element.SimX <= element.BoundsMaxX &&
                element.SimY >= element.BoundsMinY &&
                element.SimY <= element.BoundsMaxY,
                "Physics collider bounds must use the same top-left input coordinate space as SimX/SimY.");
            return element;
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
    }
}
