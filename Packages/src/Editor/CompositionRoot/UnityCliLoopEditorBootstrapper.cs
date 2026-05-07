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
        private readonly UnityCliLoopFirstPartyServerLifecycleBinding _firstPartyServerLifecycleBinding;

        internal UnityCliLoopEditorBootstrapper()
        {
            _applicationRegistration = new UnityCliLoopApplicationRegistration();
            _firstPartyServerLifecycleBinding = new UnityCliLoopFirstPartyServerLifecycleBinding();
        }

        internal void Initialize()
        {
            _applicationRegistration.Register();
            ApplicationEditorStartup.Initialize();
            FirstPartyToolsEditorStartup.Initialize();
            _firstPartyServerLifecycleBinding.Initialize();
            InfrastructureEditorStartup.Initialize();
            PresentationEditorStartup.Initialize();
        }
    }
}
