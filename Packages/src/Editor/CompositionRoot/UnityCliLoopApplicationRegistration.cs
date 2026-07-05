using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;
using ToolContractsRegistrar = io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Provides Unity CLI Loop Application Registration behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class UnityCliLoopApplicationRegistration
    {
        internal UnityCliLoopApplicationServices Register()
        {
            VibeLogger.InitializeForEditorStartup();
            ToolSettingsRepository toolSettingsRepository = new();
            ToolSettingsService toolSettingsService = new(toolSettingsRepository);
            UnityCliLoopEditorSettingsRepository editorSettingsRepository = new();
            UnityCliLoopEditorSettingsService editorSettingsService = new(editorSettingsRepository);
            UnityCliLoopEditorSessionStateRepository sessionStateRepository = new();
            UnityCliLoopEditorSessionStateService sessionStateService = new(sessionStateRepository);
            UnityCliLoopEditorSessionStateFacade.RegisterService(sessionStateService);
            UnityCliLoopFirstPartyServerLifecycleBinding firstPartyServerLifecycle = new(new ProjectIpcWarmupClient());
            DomainReloadDetectionFileService domainReloadDetectionService = new(
                sessionStateService);
            MainThreadSwitcher.RegisterService(new EditorMainThreadDispatcher());
            EditorRuntimeStateService editorRuntimeStateService = new();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new SkillInstallLayoutInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService(editorRuntimeStateService),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ApplicationRegistrar.RegisterService(toolRegistrarService);
            ToolContractsRegistrar.RegisterService(toolRegistrarService);
            ToolSettingsUseCaseRegistry.Register(new ToolSettingsUseCase(
                toolSettingsService,
                toolRegistrarService,
                new SkillInstallLayoutToolSkillDescriptionProvider()));
            SkillSetupUseCase skillSetupUseCase = new(new SkillSetupService(new ToolSkillSetupService(toolSettingsService)));
            SkillSetupUseCaseRegistry.Register(skillSetupUseCase);
            ThirdPartyToolMigrationUseCaseRegistry.Register(
                new ThirdPartyToolMigrationUseCase(new ThirdPartyToolMigrationFileService()));
            CliPinReaderService cliPinReaderService = new();
            CliSetupApplicationFacade.RegisterService(new CliSetupApplicationService(
                new CliInstallationDetector(cliPinReaderService),
                new NativeCliInstallerService(),
                cliPinReaderService));
            UnityCliLoopBridgeServerInstanceFactory serverFactory = new(domainReloadDetectionService);
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry = new();
            lifecycleRegistry.RegisterSource(serverFactory);
            UnityCliLoopServerControllerService controllerService = new(
                serverFactory,
                lifecycleRegistry,
                domainReloadDetectionService,
                sessionStateService,
                firstPartyServerLifecycle,
                firstPartyServerLifecycle);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(
                domainReloadDetectionService,
                editorSettingsService,
                sessionStateService);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSettingsService editorSettingsService,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
            EditorSettingsService = editorSettingsService;
            SessionStateService = sessionStateService;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
        internal UnityCliLoopEditorSettingsService EditorSettingsService { get; }
        internal UnityCliLoopEditorSessionStateService SessionStateService { get; }
    }
}
