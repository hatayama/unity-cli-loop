using System;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps pause-point startup wiring inside the pause-point assembly so the composition
    // root only depends on the bundled-tool facade.
    internal static class PausePointEditorStartup
    {
        public static void Initialize()
        {
            PausePointDomainReloadTracker.MarkDomainLoaded();

            IPausePointPersistenceStore store = new PausePointSessionStateStore();
            PausePointPersistenceReloadHook.Initialize(
                store,
                handler => AssemblyReloadEvents.beforeAssemblyReload += handler);

            // Why the first update tick rather than this call: re-arming applies Harmony patches
            // and reads CompilationPipeline.codeOptimization, both of which need a fully built
            // domain. Why not delayCall: Unity stops flushing it while the "Scripts have compiler
            // errors" dialog is up, which is exactly when a re-arm is expected.
            new PausePointRearmScheduler(
                () => RearmAfterDomainReload(store),
                handler => EditorApplication.update += handler,
                handler => EditorApplication.update -= handler).Schedule();
        }

        private static void RearmAfterDomainReload(IPausePointPersistenceStore store)
        {
            try
            {
                new PausePointRearmService(store).RearmAfterDomainReload();
            }
            catch (Exception exception)
            {
                VibeLogger.LogError(
                    "pause_point_rearm_after_domain_reload_failed",
                    "Persisted pause points could not be re-armed after the domain reload.",
                    new { error = exception.Message });
                UnityEngine.Debug.LogWarning(
                    "Persisted pause points could not be re-armed after the domain reload: "
                    + exception.Message);
            }
        }
    }
}
