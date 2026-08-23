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

            int maxPreviewElements = UloopPausePointRegistry.GetMaxPreviewElements(id);
            (UloopPausePointCapturedVariableFrame frame, List<UloopCapturedVariable> variables, bool truncated) =
                CaptureFrame(instance, parameterNamesAndValues, localNamesAndValues, maxPreviewElements);
            // The stack must be walked on the hitting thread; a deferred main-thread hit would see
            // the scheduler's stack instead of the caller chain that reached the marker.
            int maxCallerFrames = UloopPausePointRegistry.GetMaxCallerFrames(id);
            List<UloopPausePointCallerFrame> callerFrames =
                SourcePausePointCallerFrameCapture.CaptureCallerFrames(maxCallerFrames);

            if (MainThreadSwitcher.IsMainThread)
            {
                UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedFrame(
                    id, frame, variables, truncated, callerFrames);
                LogHit(snapshot, truncated);
                return;
            }

            // EditorApplication.isPaused (and the registry's own bookkeeping) may only be
            // touched from the main thread, so an off-thread hit is recorded on the next
            // main-thread tick instead of inline. HitCore re-checks IsEnabled at that point, so a
            // marker that already got disarmed by a faster hit safely no-ops there.
            MainThreadSwitcher.AddContinuation(() =>
            {
                UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedFrame(
                    id, frame, variables, truncated, callerFrames);
                LogHit(snapshot, truncated);
            });
        }

        private static void LogHit(UloopPausePointSnapshot snapshot, bool truncated)
        {
            VibeLogger.LogInfo(
                "pause_point_hit",
                $"Pause point hit: {snapshot.Id}",
                new { Id = snapshot.Id, snapshot.HitCount, CapturedVariablesTruncated = truncated });
        }

        internal static (UloopPausePointCapturedVariableFrame Frame, List<UloopCapturedVariable> Variables, bool Truncated)
            CaptureFrame(
                object instance, object[] parameterNamesAndValues, object[] localNamesAndValues,
                int maxPreviewElements = SourcePausePointConstants.MaxCollectionPreviewElementCount)
        {
            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                instance, parameterNamesAndValues, localNamesAndValues);
            (List<UloopCapturedVariable> variables, bool truncated) =
                SourcePausePointVariableFormatter.FormatFrame(frame, maxPreviewElements);
            frame = SourcePausePointTruncationAggregate.Merge(frame, variables);
            return (frame, variables, truncated || frame.Truncated);
        }
    }
}
