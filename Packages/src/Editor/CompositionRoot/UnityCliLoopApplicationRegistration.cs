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
            IToolSettingsPort toolSettingsPort = new ToolSettingsRepository();
            IUnityCliLoopEditorSettingsPort editorSettingsPort = new UnityCliLoopEditorSettingsRepository();
            ISessionFlagsRepository sessionFlagsRepository = new UnityCliLoopSessionFlagsRepository();
            ICompileResultSessionRepository compileResultSessionRepository =
                new UnityCliLoopCompileResultSessionRepository();
            IPendingCompileSessionRepository pendingCompileSessionRepository =
                new UnityCliLoopPendingCompileSessionRepository();
            UnityCliLoopEditorSessionStateService sessionStateService = new(
                sessionFlagsRepository,
                compileResultSessionRepository,
                pendingCompileSessionRepository);
            UnityCliLoopSessionFlagsFacade.RegisterRepository(sessionFlagsRepository);
            UnityCliLoopEditorSessionStateFacade.RegisterService(sessionStateService);
            UnityCliLoopFirstPartyServerLifecycleBinding firstPartyServerLifecycle = new(new ProjectIpcWarmupClient());
            DomainReloadDetectionFileService domainReloadDetectionService = new(
                sessionFlagsRepository,
                pendingCompileSessionRepository,
                sessionStateService);
            MainThreadSwitcher.RegisterService(new EditorMainThreadDispatcher());
            EditorRuntimeStateService editorRuntimeStateService = new();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new SkillInstallLayoutInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(editorRuntimeStateService),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ApplicationRegistrar.RegisterService(toolRegistrarService);
            ToolContractsRegistrar.RegisterService(toolRegistrarService);
            ToolSettingsUseCaseRegistry.Register(new ToolSettingsUseCase(
                toolSettingsPort,
                toolRegistrarService,
                new SkillInstallLayoutToolSkillDescriptionProvider()));
            ISkillSetupPort skillSetupPort = new ToolSkillSetupService(toolSettingsPort);
            SkillSetupUseCase skillSetupUseCase = new(skillSetupPort);
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
                sessionFlagsRepository,
                sessionStateService,
                firstPartyServerLifecycle,
                firstPartyServerLifecycle);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(
                domainReloadDetectionService,
                editorSettingsPort,
                sessionFlagsRepository,
                sessionStateService);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(
            IDomainReloadDetectionService domainReloadDetectionService,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
            EditorSettingsPort = editorSettingsPort;
            SessionFlagsRepository = sessionFlagsRepository;
            SessionStateService = sessionStateService;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
        internal IUnityCliLoopEditorSettingsPort EditorSettingsPort { get; }
        internal ISessionFlagsRepository SessionFlagsRepository { get; }
        internal UnityCliLoopEditorSessionStateService SessionStateService { get; }
    }
}
