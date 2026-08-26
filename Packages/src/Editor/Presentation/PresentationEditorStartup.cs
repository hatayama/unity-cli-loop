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
            IUnityCliLoopProjectSettingsPort projectSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            IThirdPartyToolMigrationAutoScanSeedRepository thirdPartyToolMigrationAutoScanSeedRepository,
            UnityCliLoopServerApplicationService serverApplicationService,
            CliSetupApplicationService cliSetupApplicationService,
            ToolSettingsUseCase toolSettingsUseCase,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase)
        {
            UnityCliLoopSettingsWindow.InitializeEditorServices(
                editorSettingsPort,
                sessionFlagsRepository,
                serverApplicationService,
                cliSetupApplicationService,
                toolSettingsUseCase,
                skillSetupUseCase);
            ServerEditorWindow.InitializeEditorServices(serverApplicationService);
            ThirdPartyToolMigrationWizardWindow.InitializeEditorServices(
                sessionFlagsRepository,
                thirdPartyToolMigrationAutoScanSeedRepository,
                skillSetupUseCase,
                thirdPartyToolMigrationUseCase);
            SetupWizardWindow.InitializeForEditorStartup(
                editorSettingsPort,
                projectSettingsPort,
                sessionFlagsRepository,
                thirdPartyToolMigrationAutoScanSeedRepository,
                cliSetupApplicationService,
                skillSetupUseCase,
                thirdPartyToolMigrationUseCase);
        }
    }
}
