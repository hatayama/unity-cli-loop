using System;
using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the public apply response from an orchestrator result, including warning extras.
    /// </summary>
    internal static class HotReloadApplyResponseBuilder
    {
        public static HotReloadResponse Build(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings)
        {
            Debug.Assert(result != null, "result must not be null.");

            List<HotReloadMethodResult> methods = new List<HotReloadMethodResult>(result.Methods.Count);
            bool hasFailure = false;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = result.Methods[index];
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    hasFailure = true;
                }

                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = outcome.Kind.ToString(),
                        Method = outcome.Method,
                        Reason = outcome.Reason ?? string.Empty,
                        FilePath = outcome.FilePath ?? string.Empty,
                        InvocationCount = outcome.Kind == HotReloadMethodOutcomeKind.AlreadyActive
                            ? HotReloadInvocationRegistry.GetCount(outcome.Method)
                            : 0L,
                        LifecycleNote = outcome.LifecycleNote ?? string.Empty
                    });
            }

            List<string> warnings = new List<string>(result.Warnings);
            if (additionalWarnings != null)
            {
                warnings.AddRange(additionalWarnings);
            }

            // Why before the pause-point extras: this warning is cleared by compile, so it must
            // count toward the single-compile resolution suffix instead of suppressing it.
            HotReloadUnpatchedMethodLineShiftWarningBuilder.Append(
                warnings,
                result.Methods,
                HotReloadUnpatchedMethodLineShiftWarningBuilder.ReadEditedSourceFromDisk,
                HotReloadUnpatchedMethodLineShiftWarningBuilder.ReadCompiledSnapshot);
            int orchestratorWarningCount = warnings.Count;
            AppendRetargetLineDriftWarnings(warnings);
            AppendExpiredNotRetargetedWarnings(warnings);

            if (result.RetargetedPausePointIds != null && result.RetargetedPausePointIds.Count > 0)
            {
                List<string> details = new List<string>(result.RetargetedPausePointIds.Count);
                for (int index = 0; index < result.RetargetedPausePointIds.Count; index++)
                {
                    details.Add(FormatRetargetedPausePointIdDetail(result.RetargetedPausePointIds[index]));
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.RetargetedPausePointsMessageFormat,
                        string.Join(", ", details)));
            }

            if (result.SuppressedPausePointIds != null && result.SuppressedPausePointIds.Count > 0)
            {
                string ids = string.Join(", ", result.SuppressedPausePointIds);
                warnings.Add(
                    "Armed pause points could not be re-targeted and will not fire until the patch "
                    + $"is reverted or compiled for real: {ids}");
            }

            return new HotReloadResponse
            {
                Success = !hasFailure,
                Methods = methods,
                Warnings = warnings,
                PatchedTotal = result.PatchedTotal,
                ActivePatchTotal = result.ActivePatchTotal,
                AddedFieldTotal = HotReloadAddedFieldRegistry.DescribeAll().Count,
                UnchangedTotal = result.UnchangedTotal,
                AddedFields = result.AddedFields ?? Array.Empty<string>(),
                Message = BuildApplyMessage(
                    result,
                    hasFailure,
                    warnings.Count,
                    appendCompileResolution: orchestratorWarningCount >= 2
                        && orchestratorWarningCount == warnings.Count),
                RecommendedNextAction = HotReloadRecommendedNextAction.Resolve(
                    hasFailure,
                    result.PatchedTotal,
                    CountAddedOutcomes(result))
            };
        }

        private static void AppendRetargetLineDriftWarnings(List<string> warnings)
        {
            IReadOnlyList<(string Id, string OldText, string NewText)> driftWarnings =
                HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings?.Invoke();
            if (driftWarnings == null || driftWarnings.Count == 0)
            {
                return;
            }

            for (int index = 0; index < driftWarnings.Count; index++)
            {
                (string id, string oldText, string newText) = driftWarnings[index];
                warnings.Add(
                    string.Format(
                        HotReloadConstants.RetargetLineDriftWarningFormat,
                        id,
                        oldText,
                        newText));
            }
        }

        private static void AppendExpiredNotRetargetedWarnings(List<string> warnings)
        {
            IReadOnlyList<string> expiredIds =
                HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds?.Invoke();
            if (expiredIds == null || expiredIds.Count == 0)
            {
                return;
            }

            warnings.Add(
                string.Format(
                    HotReloadConstants.ExpiredPausePointsNotRetargetedMessageFormat,
                    string.Join(", ", expiredIds)));
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

        private static string BuildApplyMessage(
            HotReloadOrchestratorResult result,
            bool hasFailure,
            int warningCount,
            bool appendCompileResolution)
        {
            // Why: when every method was left untouched, the empty Methods list is intentional —
            // report the unchanged count instead of the generic "no patchable bodies" message.
            if (!hasFailure && result.Methods.Count == 0 && result.UnchangedTotal > 0)
            {
                return AppendWarningCount(
                    "All " + result.UnchangedTotal
                    + " methods are unchanged since the last compile; nothing to patch.",
                    warningCount,
                    appendCompileResolution);
            }

            string message;
            bool isAppliedBranch;
            (message, isAppliedBranch) = BuildApplyOutcomeMessage(result, hasFailure);
            message = AppendUnchangedAndLifecycleNotes(message, result);

            if (isAppliedBranch && HasSkippedOutcome(result))
            {
                message += " See Methods for Skipped reasons.";
            }

            return AppendWarningCount(message, warningCount, appendCompileResolution);
        }

        private static (string Message, bool IsAppliedBranch) BuildApplyOutcomeMessage(
            HotReloadOrchestratorResult result,
            bool hasFailure)
        {
            int addedCount = CountAddedOutcomes(result);
            if (hasFailure)
            {
                return ("Hot reload finished with one or more Failed method outcomes. See Methods.", false);
            }

            if (result.Methods.Count == 0)
            {
                return (
                    "Hot reload found no patchable method bodies in the given files; nothing was changed. "
                    + "Hot reload only replaces existing ordinary method bodies; use uloop compile for other edits.",
                    false);
            }

            if (AreAllOutcomesAlreadyActive(result))
            {
                return (
                    string.Format(
                        HotReloadConstants.AlreadyActiveApplyMessageFormat,
                        result.Methods.Count),
                    false);
            }

            if (result.PatchedTotal == 0 && addedCount == 0)
            {
                return (HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage, false);
            }

            string message = "Hot reload applied. PatchedTotal=" + result.PatchedTotal
                + ", ActivePatchTotal=" + result.ActivePatchTotal + ".";
            if (addedCount > 0)
            {
                message += " Added: " + addedCount + ".";
            }

            return (message, true);
        }

        private static string AppendUnchangedAndLifecycleNotes(
            string message,
            HotReloadOrchestratorResult result)
        {
            if (result.UnchangedTotal > 0)
            {
                message += " " + result.UnchangedTotal + " unchanged methods were left untouched.";
            }

            int lifecycleNoteCount = CountLifecycleNotes(result);
            if (lifecycleNoteCount > 0)
            {
                // Why aggregate: per-method text already lives on Methods[].LifecycleNote;
                // dumping every note into Message repeats nearly identical paragraphs.
                message += " " + string.Format(
                    HotReloadConstants.LifecycleNotesAggregatedMessageFormat,
                    lifecycleNoteCount);
            }

            return message;
        }

        private static int CountLifecycleNotes(HotReloadOrchestratorResult result)
        {
            int lifecycleNoteCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (!string.IsNullOrEmpty(result.Methods[index].LifecycleNote))
                {
                    lifecycleNoteCount++;
                }
            }

            return lifecycleNoteCount;
        }

        private static bool AreAllOutcomesAlreadyActive(HotReloadOrchestratorResult result)
        {
            if (result.Methods.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind != HotReloadMethodOutcomeKind.AlreadyActive)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSkippedOutcome(HotReloadOrchestratorResult result)
        {
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind == HotReloadMethodOutcomeKind.Skipped)
                {
                    return true;
                }
            }

            return false;
        }

        private static string AppendWarningCount(
            string message,
            int warningCount,
            bool appendCompileResolution)
        {
            if (warningCount <= 0)
            {
                return message;
            }

            string withCount = message + " " + warningCount + " warning(s). See Warnings.";
            if (!appendCompileResolution)
            {
                return withCount;
            }

            return withCount + " " + HotReloadConstants.MultiWarningSingleCompileResolutionMessage;
        }

        private static int CountAddedOutcomes(HotReloadOrchestratorResult result)
        {
            int addedCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind == HotReloadMethodOutcomeKind.Added)
                {
                    addedCount++;
                }
            }

            return addedCount;
        }
    }
}
