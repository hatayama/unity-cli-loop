
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps replay state recovery inside the replay-input tool module.
    /// <summary>
    /// Initializes Replay Input Editor editor startup behavior.
    /// </summary>
    internal static class ReplayInputEditorStartup
    {
        internal static void Initialize()
        {
            InputReplayer.InitializeForEditorStartup();
        }
    }
}
