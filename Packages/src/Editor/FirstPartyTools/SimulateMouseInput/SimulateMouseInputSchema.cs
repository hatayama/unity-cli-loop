
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Simulate Mouse Input tool.
    /// </summary>
    public class SimulateMouseInputSchema : UnityCliLoopToolSchema
    {
        public UnityCliLoopMouseInputAction Action { get; set; } = UnityCliLoopMouseInputAction.Click;
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public UnityCliLoopMouseButton Button { get; set; } = UnityCliLoopMouseButton.Left;
        public float Duration { get; set; } = 0f;
        public float DeltaX { get; set; } = 0f;
        public float DeltaY { get; set; } = 0f;
        public float ScrollX { get; set; } = 0f;
        public float ScrollY { get; set; } = 0f;
    }
}
