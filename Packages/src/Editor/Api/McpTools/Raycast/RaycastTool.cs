#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Checks what a screenshot-compatible Game View coordinate hits in 3D physics.
    /// </summary>
    [McpTool(Description = "Raycast from Camera.main through a top-left Game View coordinate. Use annotated SimX/SimY, raycast-grid InputX/InputY, or raw screenshot image pixels converted with ScreenshotToInputFormula.")]
    public class RaycastTool : AbstractUnityTool<RaycastSchema, RaycastResponse>
    {
        public override string ToolName => "raycast";

        protected override Task<RaycastResponse> ExecuteAsync(RaycastSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (parameters.MaxDistance <= 0f || float.IsNaN(parameters.MaxDistance) || float.IsInfinity(parameters.MaxDistance))
            {
                return Task.FromResult(new RaycastResponse
                {
                    Success = false,
                    Message = $"MaxDistance must be positive and finite, got: {parameters.MaxDistance}"
                });
            }

            Vector2 inputPosition = new Vector2(parameters.X, parameters.Y);
            GameViewRaycastResult raycastResult = GameViewRaycastUtility.RaycastFromInputPosition(
                inputPosition,
                parameters.MaxDistance,
                parameters.LayerMask,
                true);

            if (!raycastResult.CameraFound)
            {
                RaycastResponse noCameraResponse = CreateBaseResponse(raycastResult.Conversion);
                noCameraResponse.Success = false;
                noCameraResponse.Message = "Camera.main was not found. Add an active camera tagged MainCamera before using raycast.";
                return Task.FromResult(noCameraResponse);
            }

            if (raycastResult.Hits.Length == 0)
            {
                RaycastResponse noHitResponse = CreateBaseResponse(raycastResult.Conversion);
                noHitResponse.Success = true;
                noHitResponse.Hit = false;
                noHitResponse.Message = $"No physics hit at ({inputPosition.x:F1}, {inputPosition.y:F1}).";
                return Task.FromResult(noHitResponse);
            }

            RaycastHit nearestHit = raycastResult.Hits[0];
            RaycastResponse response = CreateBaseResponse(raycastResult.Conversion);
            response.Success = true;
            response.Hit = true;
            response.Message = $"Hit {nearestHit.collider.gameObject.name} at ({inputPosition.x:F1}, {inputPosition.y:F1}).";
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
            return Task.FromResult(response);
        }

        private static RaycastResponse CreateBaseResponse(GameViewCoordinateConversion conversion)
        {
            return new RaycastResponse
            {
                InputCoordinateSystem = McpConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW,
                UnityCoordinateSystem = McpConstants.COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW,
                GameViewWidth = conversion.GameViewSize.x,
                GameViewHeight = conversion.GameViewSize.y,
                InputPositionX = conversion.InputPosition.x,
                InputPositionY = conversion.InputPosition.y,
                InjectedUnityPositionX = conversion.InjectedUnityPosition.x,
                InjectedUnityPositionY = conversion.InjectedUnityPosition.y,
                CoordinateConversionFormula = McpConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY
            };
        }
    }
}
