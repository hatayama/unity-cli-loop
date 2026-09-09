using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stops the resident transform worker when the domain is about to reload or the Editor quits,
    /// so no worker process outlives the Editor session that started it.
    /// </summary>
    internal static class TransformWorkerHostLifecycle
    {
        public static void RegisterForEditorStartup()
        {
            // Why unsubscribe first: Initialize runs once per domain, but a repeated registration
            // during tests must not stack handlers.
            AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForReload;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            EditorApplication.quitting -= ShutdownForQuit;
            EditorApplication.quitting += ShutdownForQuit;
        }

        internal static void ShutdownForReload()
        {
            TransformWorkerHost.Shared.Shutdown("beforeAssemblyReload");
        }

        internal static void ShutdownForQuit()
        {
            TransformWorkerHost.Shared.Shutdown("quitting");
        }
    }
}
