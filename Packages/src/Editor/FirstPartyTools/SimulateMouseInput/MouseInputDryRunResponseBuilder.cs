#nullable enable
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds simulate-mouse-input dry-run responses from Game View physics raycast results.
    /// Kept outside ULOOP_HAS_INPUT_SYSTEM guards so dry-run works without the Input System package.
    /// </summary>
    internal static class MouseInputDryRunResponseBuilder
    {
        internal static SimulateMouseInputResponse Build(
            Vector2 inputPosition,
            float maxDistance,
            int layerMask)
        {
            if (maxDistance <= 0f || float.IsNaN(maxDistance) || float.IsInfinity(maxDistance))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"MaxDistance must be positive and finite, got: {maxDistance}"
                };
            }

            GameViewRaycastResult raycastResult = GameViewRaycastUtility.RaycastFromInputPosition(
                inputPosition,
                maxDistance,
                layerMask,
                true);

            if (!raycastResult.CameraFound)
            {
                SimulateMouseInputResponse noCameraResponse = CreateBaseResponse(raycastResult.Conversion);
                noCameraResponse.Success = false;
                noCameraResponse.Message =
                    "Camera.main was not found. Add an active camera tagged MainCamera before using simulate-mouse-input --dry-run.";
                LogDryRunExecuted(inputPosition, noCameraResponse);
                return noCameraResponse;
            }

            if (raycastResult.Hits.Length == 0)
            {
                SimulateMouseInputResponse noHitResponse = CreateBaseResponse(raycastResult.Conversion);
                noHitResponse.Success = true;
                noHitResponse.Hit = false;
                noHitResponse.Message = $"No physics hit at ({inputPosition.x:F1}, {inputPosition.y:F1}).";
                noHitResponse.CameraName = raycastResult.Camera.name;
                noHitResponse.CameraPath = GameObjectPathUtility.GetFullPath(raycastResult.Camera.gameObject);
                LogDryRunExecuted(inputPosition, noHitResponse);
                return noHitResponse;
            }

            RaycastHit nearestHit = raycastResult.Hits[0];
            SimulateMouseInputResponse response = CreateBaseResponse(raycastResult.Conversion);
            response.Success = true;
            response.Hit = true;
            response.Message = $"Hit {nearestHit.collider.gameObject.name} at ({inputPosition.x:F1}, {inputPosition.y:F1}).";
            response.CameraName = raycastResult.Camera.name;
            response.CameraPath = GameObjectPathUtility.GetFullPath(raycastResult.Camera.gameObject);
            response.HitGameObjectName = nearestHit.collider.gameObject.name;
            response.HitGameObjectPath = GameObjectPathUtility.GetFullPath(nearestHit.collider.gameObject);
            response.HitLayer = nearestHit.collider.gameObject.layer;
            response.HitLayerName = LayerMask.LayerToName(nearestHit.collider.gameObject.layer);
            response.Distance = nearestHit.distance;
            response.HitPointX = nearestHit.point.x;
            response.HitPointY = nearestHit.point.y;
            response.HitPointZ = nearestHit.point.z;
            response.HitNormalX = nearestHit.normal.x;
            response.HitNormalY = nearestHit.normal.y;
            response.HitNormalZ = nearestHit.normal.z;
            LogDryRunExecuted(inputPosition, response);
            return response;
        }

        private static void LogDryRunExecuted(Vector2 inputPosition, SimulateMouseInputResponse response)
        {
            VibeLogger.LogInfo(
                "simulate_mouse_input_dry_run",
                $"simulate-mouse-input dry-run executed at ({inputPosition.x:F1}, {inputPosition.y:F1})",
                new
                {
                    CameraName = response.CameraName,
                    InputPositionX = inputPosition.x,
                    InputPositionY = inputPosition.y,
                    Hit = response.Hit,
                    HitGameObjectName = response.HitGameObjectName
                });
        }

        private static SimulateMouseInputResponse CreateBaseResponse(GameViewCoordinateConversion conversion)
        {
            return new SimulateMouseInputResponse
            {
                InputCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW,
                UnityCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW,
                GameViewWidth = conversion.GameViewSize.x,
                GameViewHeight = conversion.GameViewSize.y,
                InputPositionX = conversion.InputPosition.x,
                InputPositionY = conversion.InputPosition.y,
                InjectedUnityPositionX = conversion.InjectedUnityPosition.x,
                InjectedUnityPositionY = conversion.InjectedUnityPosition.y,
                CoordinateConversionFormula = UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY
            };
        }
    }
}
