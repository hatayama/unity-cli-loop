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
            ISessionFlagsRepository sessionFlagsRepository)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(editorSettingsPort, sessionFlagsRepository);
            ThirdPartyToolMigrationWizardWindow.InitializeEditorServices(sessionFlagsRepository);
            SetupWizardWindow.InitializeForEditorStartup(editorSettingsPort, sessionFlagsRepository);
        }
    }
}
