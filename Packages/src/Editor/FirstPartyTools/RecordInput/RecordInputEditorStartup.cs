
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps recording state recovery inside the record-input tool module.
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
