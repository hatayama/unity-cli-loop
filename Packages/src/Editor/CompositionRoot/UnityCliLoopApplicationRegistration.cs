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
            ToolSettingsUseCase toolSettingsUseCase = new(
                toolSettingsPort,
                toolRegistrarService,
                new SkillInstallLayoutToolSkillDescriptionProvider());
            ISkillSetupPort skillSetupPort = new ToolSkillSetupService(toolSettingsPort);
            SkillSetupUseCase skillSetupUseCase = new(skillSetupPort);
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase =
                new(new ThirdPartyToolMigrationFileService());
            IThirdPartyToolMigrationAutoScanSeedRepository thirdPartyToolMigrationAutoScanSeedRepository =
                new UnityCliLoopThirdPartyToolMigrationAutoScanSeedRepository();
            CliPinReaderService cliPinReaderService = new();
            CliSetupApplicationService cliSetupApplicationService = new(
                new CliInstallationDetector(cliPinReaderService),
                new NativeCliInstallerService(),
                cliPinReaderService);
            UnityCliLoopBridgeServerInstanceFactory serverFactory = new(
                domainReloadDetectionService,
                toolRegistrarService);
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
            UnityCliLoopServerRecoveryTrackingService recoveryTrackingService = new(sessionFlagsRepository);
            UnityCliLoopServerControllerService controllerService = new(
                serverFactory,
                lifecycleRegistry,
                domainReloadDetectionService,
                sessionFlagsRepository,
                serverInitializationUseCase,
                serverShutdownUseCase,
                sessionRecoveryService,
                domainReloadRecoveryUseCase,
                toolRegistrarService,
                serverReadinessService,
                startupProtectionService,
                recoveryTrackingService,
                firstPartyServerLifecycle);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(
                domainReloadDetectionService,
                editorSettingsPort,
                sessionFlagsRepository,
                thirdPartyToolMigrationAutoScanSeedRepository,
                applicationService,
                cliSetupApplicationService,
                toolSettingsUseCase,
                skillSetupUseCase,
                thirdPartyToolMigrationUseCase);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(
            IDomainReloadDetectionService domainReloadDetectionService,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            IThirdPartyToolMigrationAutoScanSeedRepository thirdPartyToolMigrationAutoScanSeedRepository,
            UnityCliLoopServerApplicationService serverApplicationService,
            CliSetupApplicationService cliSetupApplicationService,
            ToolSettingsUseCase toolSettingsUseCase,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
            EditorSettingsPort = editorSettingsPort;
            SessionFlagsRepository = sessionFlagsRepository;
            ThirdPartyToolMigrationAutoScanSeedRepository = thirdPartyToolMigrationAutoScanSeedRepository;
            ServerApplicationService = serverApplicationService;
            CliSetupApplicationService = cliSetupApplicationService;
            ToolSettingsUseCase = toolSettingsUseCase;
            SkillSetupUseCase = skillSetupUseCase;
            ThirdPartyToolMigrationUseCase = thirdPartyToolMigrationUseCase;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
        internal IUnityCliLoopEditorSettingsPort EditorSettingsPort { get; }
        internal ISessionFlagsRepository SessionFlagsRepository { get; }
        internal IThirdPartyToolMigrationAutoScanSeedRepository ThirdPartyToolMigrationAutoScanSeedRepository { get; }
        internal UnityCliLoopServerApplicationService ServerApplicationService { get; }
        internal CliSetupApplicationService CliSetupApplicationService { get; }
        internal ToolSettingsUseCase ToolSettingsUseCase { get; }
        internal SkillSetupUseCase SkillSetupUseCase { get; }
        internal ThirdPartyToolMigrationUseCase ThirdPartyToolMigrationUseCase { get; }
    }
}
