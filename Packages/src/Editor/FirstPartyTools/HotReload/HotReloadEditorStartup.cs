using UnityEditor;

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

            EditorApplication.update += CaptureOnFirstUpdateTick;
        }
    }
}
