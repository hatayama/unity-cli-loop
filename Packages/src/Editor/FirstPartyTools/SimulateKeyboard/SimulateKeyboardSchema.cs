
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Simulate Keyboard tool.
    /// </summary>
    public class SimulateKeyboardSchema : UnityCliLoopToolSchema
    {
        public UnityCliLoopKeyboardAction Action { get; set; } = UnityCliLoopKeyboardAction.Press;
        public string Key { get; set; } = "";
        public float Duration { get; set; } = 0f;
    }
}
