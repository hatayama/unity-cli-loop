namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Initializes Control Play Mode editor-domain services.
    /// </summary>
    internal static class ControlPlayModeEditorStartup
    {
        internal static void Initialize()
        {
            ControlPlayModeServices.InitializeForEditorStartup();
        }
    }
}
