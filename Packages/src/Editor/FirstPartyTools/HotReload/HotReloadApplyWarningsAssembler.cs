using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Gathers the Warnings lines of an apply response in their response order, each group tagged
    /// with how it is cleared.
    /// </summary>
    internal static class HotReloadApplyWarningsAssembler
    {
        public static HotReloadResponseWarnings Assemble(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings,
            IReadOnlyList<string> rewireFields,
            IReadOnlyList<HotReloadWiredValueRestoreFailure> unrestoredWiredValues,
            bool isPlaying,
            bool isPaused)
        {
            Debug.Assert(result != null, "result must not be null.");

            HotReloadResponseWarnings warnings = new HotReloadResponseWarnings();
            warnings.Add(
                HotReloadWarningResolution.ClearedByCompile,
                CollectClearedByCompile(result, additionalWarnings));
            warnings.Add(
                HotReloadWarningResolution.NeedsCallerAction,
                CollectNeedsCallerAction(result, rewireFields, unrestoredWiredValues, isPlaying, isPaused));
            warnings.Add(
                HotReloadWarningResolution.NotCounted,
                CollectAutoRefreshHold(result));
            return warnings;
        }

        private static List<string> CollectClearedByCompile(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings)
        {
            List<string> lines = new List<string>(result.Warnings);
            if (additionalWarnings != null)
            {
                lines.AddRange(additionalWarnings);
            }

            // A Skipped method is applied by 'uloop compile' like the warnings above it, so its
            // line counts as one. Whether the Message may add the single-compile sentence is
            // decided from the method rows themselves.
            HotReloadSkippedWarningCollapser.Append(lines, result.Methods);
            return lines;
        }

        // Why these need the caller: a pause point has to be re-armed or re-checked by hand, and a
        // compile does not bring back values wired into added fields.
        private static List<string> CollectNeedsCallerAction(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> rewireFields,
            IReadOnlyList<HotReloadWiredValueRestoreFailure> unrestoredWiredValues,
            bool isPlaying,
            bool isPaused)
        {
            List<string> lines = new List<string>();
            AppendRetargetLineDriftWarnings(lines);
            AppendExpiredNotRetargetedWarnings(lines);
            AppendRetargetedPausePointsWarning(lines, result.RetargetedPausePointIds);
            AppendSuppressedPausePointsWarning(lines, result.SuppressedPausePointIds);
            HotReloadRewireAfterDomainReloadWarning.Append(lines, rewireFields);
            HotReloadWiredValueRestoreWarning.Append(lines, unrestoredWiredValues);
            HotReloadPauseBeforeWiringWarning.Append(
                lines,
                isPlaying,
                isPaused,
                namesFieldsToWire: rewireFields.Count > 0
                    || unrestoredWiredValues.Count > 0
                    || result.SerializedAddedFieldsReported.Count > 0);
            return lines;
        }

        private static List<string> CollectAutoRefreshHold(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            HotReloadAutoRefreshHoldResponseEnricher.AppendDeferredWarning(
                lines,
                result.AutoRefreshHoldReleaseDeferred);
            HotReloadAutoRefreshHoldResponseEnricher.AppendSceneRefreshWarning(
                lines,
                result.AutoRefreshHoldSceneRefreshWarning);
            return lines;
        }

        private static void AppendRetargetLineDriftWarnings(List<string> lines)
        {
            IReadOnlyList<(string Id, string OldText, string NewText)> driftWarnings =
                HotReloadPausePointCoordination.PausePointSide?.ConsumeRetargetLineDriftWarnings();
            if (driftWarnings == null || driftWarnings.Count == 0)
            {
                return;
            }

            for (int index = 0; index < driftWarnings.Count; index++)
            {
                (string id, string oldText, string newText) = driftWarnings[index];
                lines.Add(
                    string.Format(
                        HotReloadConstants.RetargetLineDriftWarningFormat,
                        id,
                        oldText,
                        newText));
            }
        }

        private static void AppendExpiredNotRetargetedWarnings(List<string> lines)
        {
            IReadOnlyList<string> expiredIds =
                HotReloadPausePointCoordination.PausePointSide?.ConsumeExpiredNotRetargetedMarkerIds();
            if (expiredIds == null || expiredIds.Count == 0)
            {
                return;
            }

            lines.Add(
                string.Format(
                    HotReloadConstants.ExpiredPausePointsNotRetargetedMessageFormat,
                    string.Join(", ", expiredIds)));
        }

        private static void AppendRetargetedPausePointsWarning(
            List<string> lines,
            IReadOnlyList<string> retargetedPausePointIds)
        {
            if (retargetedPausePointIds == null || retargetedPausePointIds.Count == 0)
            {
                return;
            }

            List<string> details = new List<string>(retargetedPausePointIds.Count);
            for (int index = 0; index < retargetedPausePointIds.Count; index++)
            {
                details.Add(FormatRetargetedPausePointIdDetail(retargetedPausePointIds[index]));
            }

            lines.Add(
                string.Format(
                    HotReloadConstants.RetargetedPausePointsMessageFormat,
                    string.Join(", ", details)));
        }

        private static void AppendSuppressedPausePointsWarning(
            List<string> lines,
            IReadOnlyList<string> suppressedPausePointIds)
        {
            if (suppressedPausePointIds == null || suppressedPausePointIds.Count == 0)
            {
                return;
            }

            string ids = string.Join(", ", suppressedPausePointIds);
            lines.Add(
                "Armed pause points could not be re-targeted and will not fire until the patch "
                + $"is reverted or compiled for real: {ids}");
        }

        private static string FormatRetargetedPausePointIdDetail(string id)
        {
            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(id);
            string lineText = status.ResolvedLineText ?? string.Empty;
            return string.Format(
                HotReloadConstants.RetargetedPausePointIdDetailFormat,
                id,
                status.ResolvedLine,
                lineText);
        }
    }
}
