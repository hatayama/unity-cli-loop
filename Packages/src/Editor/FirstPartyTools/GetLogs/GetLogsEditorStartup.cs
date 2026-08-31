
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps Console log capture initialization inside the get-logs tool module.
    /// <summary>
    /// Initializes Get Logs Editor editor startup behavior.
    /// </summary>
    internal static class GetLogsEditorStartup
    {
        internal static void Initialize()
        {
            LogGetter.InitializeForEditorStartup();
        }
    }
}
