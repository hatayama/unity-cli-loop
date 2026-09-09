using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps hot-reload startup wiring inside the hot-reload assembly so the composition
    // root only depends on the bundled-tool facade.
    internal static class HotReloadEditorStartup
    {
        public static void Initialize()
        {
            // Why not EditorApplication.delayCall: a cold-start session that hits Unity's native
            // "Scripts have compiler errors" dialog never flushes delayCall again for the rest of
            // that process's lifetime, even for later registrations — while
            // EditorApplication.update keeps ticking (see SetupWizardWindow.cs:56-70). Capture is
            // racy-safe (use-time PDB checksum), so running on the first update tick is fine.
            void CaptureOnFirstUpdateTick()
            {
                EditorApplication.update -= CaptureOnFirstUpdateTick;
                HotReloadSourceSnapshotter.CaptureAfterDomainReload();
            }

            // The services are rebuilt here rather than on first use because the introduced type
            // resolver subscribes to AppDomain.AssemblyResolve when it is built, and that
            // subscription is lost on every domain reload.
            HotReloadCompositionRoot.Initialize();
            // Why here and not in a static constructor of the counting side: a static constructor
            // runs when something first touches that type, which a domain that only introduced a
            // type may never do, and the tools that warn about a domain reload would then read a
            // null delegate as "nothing to lose".
            HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount =
                () => HotReloadCompositionRoot.Services.Domain.CountActiveChanges().RuntimeChangeTotal;
            // Why the same shape for these two: both run from Editor callbacks that take no
            // argument, so they have to read whichever services are installed when they fire.
            HotReloadAutoRefreshHold.GetServices = () => HotReloadCompositionRoot.Services;
            HotReloadPlayModeEntryDropRecorder.GetServices = () => HotReloadCompositionRoot.Services;
            EditorApplication.update += CaptureOnFirstUpdateTick;
            HotReloadPlayModeEntryDropRecorder.Initialize();
            HotReloadAutoRefreshHold.Initialize();
            TransformWorkerHostLifecycle.RegisterForEditorStartup();
        }
    }
}
