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
            // Why last: a capture that throws stops nothing after it; Unity logs the exception and
            // hot reload captures again on the first update tick. Why inside this
            // InitializeOnLoadMethod and not afterAssemblyReload: the IPC listener opens in
            // afterAssemblyReload, and every command except get-editor-status runs on the main
            // thread's update or tick, so no command of this domain is answered before this
            // returns — an edit made after `uloop compile` returns can no longer be captured as
            // compiled source.
            FirstPartyToolsEditorStartup.PrepareBeforeServingCommands();
        }
    }
}
