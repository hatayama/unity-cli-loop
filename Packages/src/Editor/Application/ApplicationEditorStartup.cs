using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Groups application-layer Editor startup callbacks behind one facade for the composition root.
    /// <summary>
    /// Initializes Application Editor editor startup behavior.
    /// </summary>
    internal static class ApplicationEditorStartup
    {
        internal static void Initialize()
        {
            UnityCliLoopEditorDomainReloadStateRegistration.RegisterForEditorStartup();
            MainThreadSwitcher.InitializeForEditorStartup();
            EditorDelayManager.InitializeForEditorStartup();
            DomainReloadDetectionService.RegisterForEditorStartup();
        }
    }
}
