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
            Debug.Assert(renderingImageSize.x >= 0f, "Rendering image width must not be negative.");
            Debug.Assert(renderingImageSize.y >= 0f, "Rendering image height must not be negative.");
            Debug.Assert(row >= 1 && row <= GRID_ROWS, "Grid row must be within the configured grid.");
            Debug.Assert(column >= 1 && column <= GRID_COLUMNS, "Grid column must be within the configured grid.");

            // Grid points must be visible in the captured PNG, so sample image space before adding the input Y offset.
            return new Vector2(
                renderingImageSize.x * column / (GRID_COLUMNS + 1f),
                imageToInputOffsetY + renderingImageSize.y * row / (GRID_ROWS + 1f));
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
    }
}
