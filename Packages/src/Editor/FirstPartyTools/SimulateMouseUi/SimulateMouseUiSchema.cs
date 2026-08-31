
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Simulate Mouse UI tool.
    /// </summary>
    public class SimulateMouseUiSchema : UnityCliLoopToolSchema
    {
        public UnityCliLoopMouseUiAction Action { get; set; } = UnityCliLoopMouseUiAction.Click;
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float FromX { get; set; } = 0f;
        public float FromY { get; set; } = 0f;
        public float DragSpeed { get; set; } = UnityCliLoopInputSimulationDefaults.MouseUiDragSpeed;
        public float Duration { get; set; } = UnityCliLoopInputSimulationDefaults.MouseUiDuration;
        public UnityCliLoopMouseButton Button { get; set; } = UnityCliLoopMouseButton.Left;
        public bool BypassRaycast { get; set; } = false;
        public string TargetPath { get; set; } = "";
        public string DropTargetPath { get; set; } = "";
    }
}
