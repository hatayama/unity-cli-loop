using System.ComponentModel;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    public class RaycastSchema : BaseToolSchema
    {
        [Description("Target X position in Game View pixels (origin: top-left). Use AnnotatedElements[].SimX, RaycastGridPoints[].InputX, or raw screenshot image pixels converted with ScreenshotToInputFormula.")]
        public float X { get; set; } = 0f;

        [Description("Target Y position in Game View pixels (origin: top-left). Use AnnotatedElements[].SimY, RaycastGridPoints[].InputY, or raw screenshot image pixels converted with ScreenshotToInputFormula.")]
        public float Y { get; set; } = 0f;

        [Description("Physics layer mask used by the raycast.")]
        public int LayerMask { get; set; } = Physics.DefaultRaycastLayers;

        [Description("Maximum raycast distance in world units.")]
        public float MaxDistance { get; set; } = McpConstants.RAYCAST_DEFAULT_MAX_DISTANCE;
    }
}
