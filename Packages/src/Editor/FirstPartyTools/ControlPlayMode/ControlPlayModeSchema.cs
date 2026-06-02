
using System.ComponentModel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    public enum PlayModeAction
    {
        Play = 0,
        Stop = 1,
        Pause = 2,
        NextFrame = 3
    }

    /// <summary>
    /// Describes the parameters accepted by the Control Play Mode tool.
    /// </summary>
    public class ControlPlayModeSchema : UnityCliLoopToolSchema
    {
        public PlayModeAction Action { get; set; } = PlayModeAction.Play;
        public int TimeoutSeconds { get; set; } = ControlPlayModeUseCase.DefaultTimeoutSeconds;
        [Browsable(false)]
        public bool StatusOnly { get; set; }
    }
}
