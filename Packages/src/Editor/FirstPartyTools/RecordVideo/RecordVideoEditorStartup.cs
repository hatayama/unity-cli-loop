namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Registers record-video Editor hooks at package startup.
    /// </summary>
    internal static class RecordVideoEditorStartup
    {
        internal static void Initialize()
        {
            RecordVideoService.InitializeForEditorStartup();
        }
    }
}
