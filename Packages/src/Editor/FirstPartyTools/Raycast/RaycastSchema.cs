using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Raycast tool.
    /// </summary>
    public class RaycastSchema : UnityCliLoopToolSchema
    {
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public int LayerMask { get; set; } = Physics.DefaultRaycastLayers;
        public float MaxDistance { get; set; } = UnityCliLoopConstants.RAYCAST_DEFAULT_MAX_DISTANCE;
    }
}
