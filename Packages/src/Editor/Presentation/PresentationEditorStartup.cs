using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        internal static void Initialize(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopServerApplicationService serverApplicationService)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(
                editorSettingsPort,
                sessionFlagsRepository,
                serverApplicationService);
            ServerEditorWindow.InitializeEditorServices(serverApplicationService);
            ThirdPartyToolMigrationWizardWindow.InitializeEditorServices(sessionFlagsRepository);
            SetupWizardWindow.InitializeForEditorStartup(editorSettingsPort, sessionFlagsRepository);
        }
    }
}
