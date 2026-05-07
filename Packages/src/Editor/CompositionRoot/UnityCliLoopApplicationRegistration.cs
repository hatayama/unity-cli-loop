using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Provides Unity CLI Loop Application Registration behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class UnityCliLoopApplicationRegistration
    {
        internal void Register()
        {
            ToolSettingsRepository toolSettingsRepository = new();
            UnityCliLoopEditorSettingsRepository editorSettingsRepository = new();
            ULoopSettingsRepository uLoopSettingsRepository = new();
            ToolSettings.RegisterService(toolSettingsRepository);
            UnityCliLoopEditorSettings.RegisterService(editorSettingsRepository);
            ULoopSettings.RegisterService(uLoopSettingsRepository);
            MainThreadSwitcher.RegisterService(new EditorMainThreadDispatcher());
            CompilationLockService.RegisterService(new CompilationLockFileService());
            DomainReloadDetectionService.RegisterService(new DomainReloadDetectionFileService());
            UnityCliLoopToolRegistrar.RegisterService(new UnityCliLoopToolRegistrarService(
                new SkillInstallLayoutInternalToolNameProvider()));
            SkillSetupApplicationFacade.RegisterService(new SkillSetupApplicationService(
                new ToolSkillSetupService()));
            CliSetupApplicationFacade.RegisterService(new CliSetupApplicationService(
                new CliInstallationDetector(),
                new ProjectLocalCliInstallerService(),
                new NativeCliInstallerService()));
            UnityCliLoopBridgeServerInstanceFactory serverFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry = new();
            lifecycleRegistry.RegisterSource(serverFactory);
            UnityCliLoopServerControllerService controllerService = new(serverFactory, lifecycleRegistry);
            UnityCliLoopServerApplicationService applicationService = new(controllerService);
            UnityCliLoopServerApplicationFacade.RegisterService(applicationService);
            controllerService.InitializeForEditorStartup();
        }
    }
}
