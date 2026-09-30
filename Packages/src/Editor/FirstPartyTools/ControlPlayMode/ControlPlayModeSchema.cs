
using System.ComponentModel;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    public enum PlayModeAction
    {
        Play = 0,
        Stop = 1,
        Pause = 2,
        Step = 3,
        Status = 4,
        Resume = 5
    }

    /// <summary>
    /// How Play handles unsaved Scene and Prefab Stage changes when it enters Play Mode from Edit Mode.
    /// </summary>
    public enum ControlPlayModeUnsavedChangesMode
    {
        keep = 0,
        save = 1,
        fail = 2
    }

    /// <summary>
    /// Describes the parameters accepted by the Control Play Mode tool.
    /// </summary>
    public class ControlPlayModeSchema : UnityCliLoopToolSchema
    {
        public PlayModeAction Action { get; set; } = PlayModeAction.Play;
        public int TimeoutSeconds { get; set; } = ControlPlayModeUseCase.DefaultTimeoutSeconds;
        /// <summary>
        /// How to handle unsaved Scene and Prefab Stage changes before entering Play Mode from Edit Mode.
        /// </summary>
        public ControlPlayModeUnsavedChangesMode UnsavedChanges { get; set; } = ControlPlayModeUnsavedChangesMode.keep;
        [Browsable(false)]
        public bool StatusOnly { get; set; }
    }
}
