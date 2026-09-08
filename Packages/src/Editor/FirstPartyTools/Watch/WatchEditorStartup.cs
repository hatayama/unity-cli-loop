using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps watch startup wiring inside the watch assembly so the composition root only
    // depends on the bundled-tool facade.
    internal static class WatchEditorStartup
    {
        public static void Initialize()
        {
            // Why not EditorApplication.delayCall: a cold-start session that hits Unity's native
            // "Scripts have compiler errors" dialog never flushes delayCall again for the rest of
            // that process's lifetime, even for later registrations, while EditorApplication.update
            // keeps ticking. Restoring on the first update tick also gives the dynamic-code
            // compilation pipeline a fully initialized domain to compile against.
            void RestoreOnFirstUpdateTick()
            {
                EditorApplication.update -= RestoreOnFirstUpdateTick;
                WatchExpressionServices.RestoreAfterDomainReload();
            }

            EditorApplication.update += RestoreOnFirstUpdateTick;
        }
    }
}
