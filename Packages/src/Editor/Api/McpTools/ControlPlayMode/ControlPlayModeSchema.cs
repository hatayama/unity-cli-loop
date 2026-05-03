using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    public enum PlayModeAction
    {
        Play = 0,
        Stop = 1,
        Pause = 2
    }

    public class ControlPlayModeSchema : BaseToolSchema
    {
        [Description("Action to perform: Play(0) - Start play mode, Stop(1) - Stop play mode, Pause(2) - Pause play mode")]
        public PlayModeAction Action { get; set; } = PlayModeAction.Play;
    }
}

