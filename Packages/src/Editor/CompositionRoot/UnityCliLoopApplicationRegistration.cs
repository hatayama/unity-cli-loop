using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Provides Unity CLI Loop Application Registration behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class UnityCliLoopApplicationRegistration
    {
        internal UnityCliLoopApplicationServices Register()
        {
            ToolSettingsRepository toolSettingsRepository = new();
            ToolSettingsService toolSettingsService = new(toolSettingsRepository);
            UnityCliLoopEditorSettingsRepository editorSettingsRepository = new();
            UnityCliLoopEditorSettingsService editorSettingsService = new(editorSettingsRepository);
            ServerReadinessStateStore serverReadinessStateStore = new(UnityCliLoopPathResolver.GetProjectRoot());
            UnityCliLoopFirstPartyServerLifecycleBinding serverReadinessProbe = new(new ProjectIpcWarmupClient());
            ULoopSettingsRepository uLoopSettingsRepository = new(
                toolSettingsService,
                editorSettingsService);
            DomainReloadDetectionFileService domainReloadDetectionService = new(
                editorSettingsService,
                serverReadinessStateStore);
            ULoopSettings.RegisterService(uLoopSettingsRepository);
            MainThreadSwitcher.RegisterService(new EditorMainThreadDispatcher());
            CompilationLockService.RegisterService(new CompilationLockFileService(serverReadinessStateStore));
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new SkillInstallLayoutInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(toolRegistrarService);
            ToolSettingsUseCaseRegistry.Register(new ToolSettingsUseCase(
                toolSettingsService,
                toolRegistrarService));
            SkillSetupUseCase skillSetupUseCase = new(new SkillSetupService(new ToolSkillSetupService(toolSettingsService)));
            SkillSetupUseCaseRegistry.Register(skillSetupUseCase);
            ThirdPartyToolMigrationUseCaseRegistry.Register(
                new ThirdPartyToolMigrationUseCase(new ThirdPartyToolMigrationFileService()));
            CliSetupApplicationFacade.RegisterService(new CliSetupApplicationService(
                new CliInstallationDetector(),
                new NativeCliInstallerService()));
            UnityCliLoopBridgeServerInstanceFactory serverFactory = new(
                domainReloadDetectionService,
                editorSettingsService);
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry = new();
            lifecycleRegistry.RegisterSource(serverFactory);
            UnityCliLoopServerControllerService controllerService = new(
                serverFactory,
                lifecycleRegistry,
                domainReloadDetectionService,
                editorSettingsService,
                serverReadinessStateStore,
                serverReadinessProbe);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(
                domainReloadDetectionService,
                editorSettingsService);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSettingsService editorSettingsService)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
            EditorSettingsService = editorSettingsService;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
        internal UnityCliLoopEditorSettingsService EditorSettingsService { get; }
    }
}
