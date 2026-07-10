using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The method Harmony-injected IL calls at a source pause point. Checks the armed marker
    /// first so an inactive (not-yet-enabled) patch costs a single dictionary lookup on the hot path.
    /// </summary>
    internal static class SourcePausePointCapture
    {
        public static void Capture(
            string id, object instance, object[] parameterNamesAndValues, object[] localNamesAndValues)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty");
            Debug.Assert(parameterNamesAndValues != null, "parameterNamesAndValues must not be null");
            Debug.Assert(localNamesAndValues != null, "localNamesAndValues must not be null");

            if (!UloopPausePointRegistry.IsArmed(id))
            {
                return;
            }

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                instance, parameterNamesAndValues, localNamesAndValues);

            if (MainThreadSwitcher.IsMainThread)
            {
                UloopPausePointRegistry.HitWithCapturedVariables(id, variables, truncated);
                return;
            }

            // EditorApplication.isPaused (and the registry's own bookkeeping) may only be
            // touched from the main thread, so an off-thread hit is recorded on the next
            // main-thread tick instead of inline. HitCore re-checks IsEnabled at that point, so a
            // marker that already got disarmed by a faster hit safely no-ops there.
            MainThreadSwitcher.AddContinuation(() => UloopPausePointRegistry.HitWithCapturedVariables(id, variables, truncated));
        }
    }
}
