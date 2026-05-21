using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        internal static void Initialize(
            UnityCliLoopEditorSettingsService editorSettingsService,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(editorSettingsService, sessionStateService);
            ThirdPartyToolMigrationWizardWindow.InitializeEditorServices(sessionStateService);
            SetupWizardWindow.InitializeForEditorStartup(editorSettingsService, sessionStateService);
        }
    }
}
