#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unity Editor hooks for cancel-time stop/restore. Option B baseline (public API only).
    /// Option A (CancelTestRun reflection) can plug into TryCancelTestRun later with B fallback.
    /// </summary>
    internal static class RunTestsCancelStopRestoreUnityHooks
    {
        /// <summary>
        /// Optional override for pure unit / EditMode stubbing. Null uses production hooks.
        /// </summary>
        internal static RunTestsCancelStopRestoreHooks OverrideHooksForTests { get; set; }

        internal static RunTestsCancelStopRestoreHooks Resolve()
        {
            return OverrideHooksForTests ?? CreateDefault();
        }

        internal static RunTestsCancelStopRestoreHooks CreateDefault()
        {
            return new RunTestsCancelStopRestoreHooks
            {
                // Why null TryCancelTestRun until Option A is user-approved: TF 1.3.9 exposes
                // CancelTestRun only as an internal API, and reflection requires explicit permission.
                // When Option A lands, resolve CancelTestRun once, cache it, and fall back here on failure.
                TryCancelTestRun = null,
                IsRunActive = null,
                IsPlaying = () => EditorApplication.isPlaying,
                RequestExitPlayMode = () =>
                {
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                    }
                },
                DelayAsync = (milliseconds, ct) => TimerDelay.Wait(milliseconds, ct),
                LogWarning = message => Debug.LogWarning(message)
            };
        }
    }
}
#endif
