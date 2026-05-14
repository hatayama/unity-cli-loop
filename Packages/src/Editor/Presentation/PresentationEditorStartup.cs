using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        internal static void Initialize(UnityCliLoopEditorSettingsService editorSettingsService)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(editorSettingsService);
            SetupWizardWindow.InitializeForEditorStartup(editorSettingsService);
            ThirdPartyToolMigrationWizardWindow.InitializeForEditorStartup();
        }
    }
}
