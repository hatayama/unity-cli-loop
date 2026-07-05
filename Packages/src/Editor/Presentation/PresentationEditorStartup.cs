using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        internal static void Initialize(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(editorSettingsPort, sessionStateService);
            ThirdPartyToolMigrationWizardWindow.InitializeEditorServices(sessionStateService);
            SetupWizardWindow.InitializeForEditorStartup(editorSettingsPort, sessionStateService);
        }
    }
}
