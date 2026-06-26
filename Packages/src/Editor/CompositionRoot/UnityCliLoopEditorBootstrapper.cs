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
            CliPinSynchronizer.SyncCurrentProjectPin();
            UnityCliLoopApplicationServices applicationServices = _applicationRegistration.Register();
            ApplicationEditorStartup.Initialize(applicationServices.DomainReloadDetectionService);
            FirstPartyToolsEditorStartup.Initialize();
            InfrastructureEditorStartup.Initialize(applicationServices.EditorSettingsService);
            PresentationEditorStartup.Initialize(
                applicationServices.EditorSettingsService,
                applicationServices.SessionStateService);
        }
    }
}
