#nullable enable
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Runs physics raycasts from screenshot-compatible Game View coordinates.
    /// </summary>
    internal static class GameViewRaycastUtility
    {
        internal static GameViewRaycastResult RaycastFromInputPosition(
            Vector2 inputPosition,
            float maxDistance,
            int layerMask,
            bool syncTransforms)
        {
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            GameViewCoordinateConversion conversion =
                GameViewCoordinateUtility.ConvertInputToUnity(inputPosition, gameViewSize);

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return new GameViewRaycastResult(false, conversion, new RaycastHit[0]);
            }

            Ray ray = mainCamera.ScreenPointToRay(conversion.InjectedUnityPosition);
            if (syncTransforms)
            {
                // Transform changes can be visible before the physics scene updates, so sync before screenshot-based queries.
                Physics.SyncTransforms();
            }

            int visibleLayerMask = layerMask & mainCamera.cullingMask;
            RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, visibleLayerMask);
            System.Array.Sort(hits, CompareHitsByDistance);

            return new GameViewRaycastResult(true, conversion, hits);
        }

        private static int CompareHitsByDistance(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }
    }

    /// <summary>
    /// Contains the camera lookup result, coordinate conversion, and sorted physics hits.
    /// </summary>
    internal readonly struct GameViewRaycastResult
    {
        public readonly bool CameraFound;
        public readonly GameViewCoordinateConversion Conversion;
        public readonly RaycastHit[] Hits;

        public GameViewRaycastResult(
            bool cameraFound,
            GameViewCoordinateConversion conversion,
            RaycastHit[] hits)
        {
            CameraFound = cameraFound;
            Conversion = conversion;
            Hits = hits;
        }
    }
}
