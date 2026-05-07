
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unity EditorPrefs "ScriptCompilationDuringPlay" value options
    /// Maps to Unity Editor's Preferences > General > Script Changes While Playing setting
    /// </summary>
    public enum ScriptChangesDuringPlayOptions
    {
        RecompileAndContinuePlaying = 0,
        RecompileAfterFinishedPlaying = 1,
        StopPlayingAndRecompile = 2
    }
}
