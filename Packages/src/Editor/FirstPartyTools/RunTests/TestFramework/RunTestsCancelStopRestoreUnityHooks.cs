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
    /// Unity Editor hooks for cancel-time stop/restore, including Option A CancelTestRun reflection.
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
            TestRunnerApiCancelBridge.EnsureResolved();

            return new RunTestsCancelStopRestoreHooks
            {
                // Why null when lookup fails: Option A is a superset of Option B. Cached miss
                // keeps cancel on Play Mode exit + bounded wait without retrying reflection.
                TryCancelTestRun = TestRunnerApiCancelBridge.HasCancelTestRun
                    ? TestRunnerApiCancelBridge.TryCancelTestRun
                    : null,
                IsRunActive = TestRunnerApiCancelBridge.HasIsRunActive
                    ? TestRunnerApiCancelBridge.TryIsRunActive
                    : null,
                IsPlaying = () => EditorApplication.isPlaying,
                RequestExitPlayMode = StopPlayingForCancel,
                DelayAsync = (milliseconds, ct) => TimerDelay.Wait(milliseconds, ct),
                LogWarning = message => Debug.LogWarning(message)
            };
        }

        /// <summary>
        /// Records the run-tests cancel stop reason, then exits Play Mode when it is running.
        /// </summary>
        internal static void StopPlayingForCancel()
        {
            PlayModeStopReasonSessionStore.SetPending(
                ControlPlayModeConstants.StoppedByCliRunTestsCancel);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif
