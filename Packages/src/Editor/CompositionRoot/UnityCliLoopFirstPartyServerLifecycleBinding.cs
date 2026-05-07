using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Binds platform server lifecycle notifications to bundled tool lifecycle hooks.
    /// </summary>
    internal sealed class UnityCliLoopFirstPartyServerLifecycleBinding
    {
        internal void Initialize()
        {
            UnityCliLoopServerApplicationFacade.AddServerStateChangedHandler(OnServerStateChanged);
        }

        private void OnServerStateChanged()
        {
            FirstPartyToolsEditorStartup.ResetServerScopedServices();
        }
    }
}
