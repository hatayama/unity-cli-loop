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
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService = new(
                sessionFlagsRepository,
                compileResultSessionRepository,
                pendingCompileSessionRepository);
            UnityCliLoopSessionFlagsFacade.RegisterRepository(sessionFlagsRepository);
            UnityCliLoopCompileResultSessionRepositoryFacade.RegisterRepository(compileResultSessionRepository);
            UnityCliLoopPendingCompileSessionRepositoryFacade.RegisterRepository(pendingCompileSessionRepository);
            UnityCliLoopCompileSessionLifecycleFacade.RegisterService(compileSessionLifecycleService);
            UnityCliLoopFirstPartyServerLifecycleBinding firstPartyServerLifecycle = new(new ProjectIpcWarmupClient());
            DomainReloadDetectionFileService domainReloadDetectionService = new(
                sessionFlagsRepository,
                pendingCompileSessionRepository,
                compileSessionLifecycleService);
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
            UnityCliLoopServerStartupService serverStartupService = new(
                serverFactory,
                sessionFlagsRepository);
            UnityCliLoopServerInitializationUseCase serverInitializationUseCase = new(
                new EditorSecurityValidationService(),
                serverStartupService);
            UnityCliLoopServerShutdownUseCase serverShutdownUseCase = new(serverStartupService);
            SessionRecoveryService sessionRecoveryService = new(
                domainReloadDetectionService,
                sessionFlagsRepository);
            DomainReloadRecoveryUseCase domainReloadRecoveryUseCase = new(
                sessionRecoveryService,
                domainReloadDetectionService,
                sessionFlagsRepository);
            UnityCliLoopServerReadinessService serverReadinessService = new(
                lifecycleRegistry,
                firstPartyServerLifecycle);
            UnityCliLoopServerStartupProtectionService startupProtectionService = new();
            UnityCliLoopServerControllerService controllerService = new(
                serverFactory,
                lifecycleRegistry,
                domainReloadDetectionService,
                sessionFlagsRepository,
                serverInitializationUseCase,
                serverShutdownUseCase,
                domainReloadRecoveryUseCase,
                serverReadinessService,
                startupProtectionService,
                firstPartyServerLifecycle);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(
                domainReloadDetectionService,
                editorSettingsPort,
                sessionFlagsRepository);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(
            IDomainReloadDetectionService domainReloadDetectionService,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
            EditorSettingsPort = editorSettingsPort;
            SessionFlagsRepository = sessionFlagsRepository;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
        internal IUnityCliLoopEditorSettingsPort EditorSettingsPort { get; }
        internal ISessionFlagsRepository SessionFlagsRepository { get; }
    }
}
