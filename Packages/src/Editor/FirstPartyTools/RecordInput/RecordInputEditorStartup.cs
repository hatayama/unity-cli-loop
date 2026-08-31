#if ULOOP_HAS_INPUT_SYSTEM
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps recording state recovery inside the Recordings window module.
    /// <summary>
    /// Initializes Record Input Editor editor startup behavior.
    /// </summary>
    internal static class RecordInputEditorStartup
    {
        internal static void Initialize()
        {
            InputRecorder.InitializeForEditorStartup();
        }
    }
}
#endif
