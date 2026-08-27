using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    // Orchestrates Editor startup from an instance so only Unity's entrypoint remains static.
    /// <summary>
    /// Bootstraps Unity CLI Loop Editor dependencies in a controlled order.
    /// </summary>
    internal sealed class UnityCliLoopEditorBootstrapper
    {
        private readonly UnityCliLoopApplicationRegistration _applicationRegistration;

        internal UnityCliLoopEditorBootstrapper()
        {
            _applicationRegistration = new UnityCliLoopApplicationRegistration();
        }

        internal void Initialize()
        {
            // Why: pin synchronization is now mandatory for the dispatcher, but a false result (missing
            // source pin, or destination already matches) is signalled via the return value and must not
            // abort the remaining startup steps. CliPinSynchronizer logs its own warnings on failure.
            _ = CliPinSynchronizer.SyncCurrentProjectPin();
            UnityCliLoopApplicationServices applicationServices = _applicationRegistration.Register();
            ApplicationEditorStartup.Initialize(applicationServices.DomainReloadDetectionService);
            EditorRuntimeStateSnapshotSubscriber.InitializeForEditorStartup();
            FirstPartyToolsEditorStartup.Initialize();
            InfrastructureEditorStartup.Initialize(applicationServices.EditorSettingsPort);
            PresentationEditorStartup.Initialize(
                applicationServices.EditorSettingsPort,
                applicationServices.ProjectSettingsPort,
                applicationServices.SessionFlagsRepository,
                applicationServices.ThirdPartyToolMigrationAutoScanSeedRepository,
                applicationServices.ServerApplicationService,
                applicationServices.CliSetupApplicationService,
                applicationServices.ToolSettingsUseCase,
                applicationServices.SkillSetupUseCase,
                applicationServices.ThirdPartyToolMigrationUseCase);
        }
    }
}
