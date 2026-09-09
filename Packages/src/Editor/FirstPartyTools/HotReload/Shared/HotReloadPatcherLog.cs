using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// VibeLogger wrappers for the patching aggregate. Kept apart from the orchestrator log so the
    /// patching assembly does not have to reference the application assembly it is called from.
    /// </summary>
    internal static class HotReloadPatcherLog
    {
        internal static void LogHotReloadRevertFailed(string methodKey, Exception exception)
        {
            VibeLogger.LogWarning(
                HotReloadConstants.VibeLogRevertFailed,
                "Hot reload could not restore a method's original body.",
                new
                {
                    method = methodKey,
                    error = exception.Message
                });
        }
    }
}
