using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

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
            ULoopSettingsRepository uLoopSettingsRepository = new(toolSettingsService);
            DomainReloadDetectionFileService domainReloadDetectionService = new();
            UnityCliLoopEditorSettings.RegisterService(editorSettingsRepository);
            ULoopSettings.RegisterService(uLoopSettingsRepository);
            MainThreadSwitcher.RegisterService(new EditorMainThreadDispatcher());
            CompilationLockService.RegisterService(new CompilationLockFileService());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new SkillInstallLayoutInternalToolNameProvider(),
                toolSettingsService);
            UnityCliLoopToolRegistrar.RegisterService(toolRegistrarService);
            ToolSettingsUseCaseRegistry.Register(new ToolSettingsUseCase(
                toolSettingsService,
                toolRegistrarService));
            SkillSetupUseCase skillSetupUseCase = new(new SkillSetupService(new ToolSkillSetupService(toolSettingsService)));
            SkillSetupUseCaseRegistry.Register(skillSetupUseCase);
            CliSetupApplicationFacade.RegisterService(new CliSetupApplicationService(
                new CliInstallationDetector(),
                new ProjectLocalCliInstallerService(),
                new NativeCliInstallerService()));
            UnityCliLoopBridgeServerInstanceFactory serverFactory = new(domainReloadDetectionService);
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry = new();
            lifecycleRegistry.RegisterSource(serverFactory);
            UnityCliLoopServerControllerService controllerService = new(
                serverFactory,
                lifecycleRegistry,
                domainReloadDetectionService);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();

            return new UnityCliLoopApplicationServices(domainReloadDetectionService);
        }
    }

    internal sealed class UnityCliLoopApplicationServices
    {
        internal UnityCliLoopApplicationServices(IDomainReloadDetectionService domainReloadDetectionService)
        {
            DomainReloadDetectionService = domainReloadDetectionService;
        }

        internal IDomainReloadDetectionService DomainReloadDetectionService { get; }
    }
}
