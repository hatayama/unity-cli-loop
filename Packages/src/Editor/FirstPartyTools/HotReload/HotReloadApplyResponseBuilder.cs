using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the public apply response from an orchestrator result, including warning extras.
    /// </summary>
    internal static class HotReloadApplyResponseBuilder
    {
        public static HotReloadResponse Build(
            HotReloadServices services,
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> additionalWarnings,
            IReadOnlyList<string> recoveredDropIdentities)
        {
            Debug.Assert(services != null, "services must not be null.");
            Debug.Assert(result != null, "result must not be null.");

            Func<string, string> toProjectRelativeScriptPath =
                path => HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                    services.PackageRootCapture,
                    path);
            HotReloadReappliedSiblingFiles reappliedSiblingFiles =
                new HotReloadReappliedSiblingFiles(result.ReappliedSiblingPaths, toProjectRelativeScriptPath);
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
                        // Why these two kinds: both describe a patch that was already installed
                        // before this run, so the ledger counter is the only invocation figure
                        // that means anything for them.
                        InvocationCount = ReadsInvocationCountFromLedger(outcome.Kind)
                            ? HotReloadInvocationRegistry.GetCount(outcome.Method)
                            : 0L,
                        LifecycleNote = outcome.LifecycleNote ?? string.Empty,
                        ReappliedFromSibling = reappliedSiblingFiles.Contains(outcome.FilePath)
                    });
            }

            // Why kept apart: the messages a type failure reports differ depending on whether the
            // methods failed too, so the method verdict has to survive the fold below.
            bool hasMethodFailure = hasFailure;

            // Why folded in here: the type rows are a failure section of their own, and a run
            // whose only failure was a refused declaration would otherwise answer Success.
            hasFailure = hasFailure || HotReloadIntroducedTypeResponseSection.HoldsFailure(result.IntroducedTypes);

            HotReloadResponseWarnings warnings = HotReloadApplyWarningsAssembler.Assemble(
                result,
                additionalWarnings,
                recoveredDropIdentities,
                toProjectRelativeScriptPath);
            bool allRequestedSkipped = DecideAllRequestedSkipped(result, toProjectRelativeScriptPath);
            int reappliedSiblingCount = HotReloadRequestedFileOutcomeSummary.CountReappliedSiblingOutcomes(
                result.Methods,
                result.ReappliedSiblingPaths,
                toProjectRelativeScriptPath);
            string message = BuildApplyMessage(
                result,
                hasFailure,
                hasMethodFailure,
                warnings.Count,
                appendCompileResolution: DecideAppendCompileResolution(result, warnings),
                allRequestedSkipped,
                reappliedSiblingCount);
            return new HotReloadResponse
            {
                Success = !hasFailure,
                Methods = methods,
                Warnings = warnings.ToList(),
                IntroducedTypes = HotReloadIntroducedTypeResponseSection.BuildRows(result.IntroducedTypes),
                ActiveIntroducedTypeTotal = services.Domain.IntroducedTypeCount,
                PatchedTotal = result.PatchedTotal,
                ActivePatchTotal = result.ActivePatchTotal,
                AddedFieldTotal = services.Domain.DescribeAddedFields().Count,
                UnchangedTotal = result.UnchangedTotal,
                ClearedCount = result.RevertedUnchangedTotal,
                AddedFields = result.AddedFields,
                AddedConsts = result.AddedConsts,
                AutoRefreshHeld = result.AutoRefreshHeld,
                Message = HotReloadAutoRefreshHoldResponseEnricher.AppendNewlyArmedMessage(
                    message,
                    result.AutoRefreshHoldNewlyArmed),
                RecommendedNextAction = HotReloadRecommendedNextAction.Resolve(
                    hasFailure,
                    result.PatchedTotal,
                    CountAddedOutcomes(result),
                    HotReloadIntroducedTypeResponseSection.CountIntroducedTypes(result.IntroducedTypes),
                    allRequestedSkipped)
            };
        }

        // Why the types gate it: a run that introduced or bound a declaration applied part of
        // what the requested files hold, and the type message reports that instead, so claiming
        // nothing from those files was applied would contradict it.
        private static bool DecideAllRequestedSkipped(
            HotReloadOrchestratorResult result,
            Func<string, string> toProjectRelativeScriptPath)
        {
            if (result.IntroducedTypes.Count > 0)
            {
                return false;
            }

            return HotReloadRequestedFileOutcomeSummary.AreAllRequestedOutcomesSkipped(
                result.Methods,
                result.ReappliedSiblingPaths,
                toProjectRelativeScriptPath);
        }

        private static string BuildApplyMessage(
            HotReloadOrchestratorResult result,
            bool hasFailure,
            bool hasMethodFailure,
            int warningCount,
            bool appendCompileResolution,
            bool allRequestedSkipped,
            int reappliedSiblingCount)
        {
            // Why asked first: the file a run introduces a type into usually holds untouched
            // methods as well, and every message below would then report the methods only.
            if (HotReloadIntroducedTypeResponseSection.TryBuildMessage(
                    result.IntroducedTypes,
                    hasMethodFailure,
                    result.PatchedTotal,
                    CountAddedOutcomes(result),
                    CountOutcomesOfKind(result, HotReloadMethodOutcomeKind.Skipped),
                    out string typeMessage))
            {
                return AppendWarningCount(typeMessage, warningCount, appendCompileResolution);
            }

            // Why: when every method was left untouched, the empty Methods list is intentional —
            // report the unchanged count instead of the generic "no patchable bodies" message.
            if (!hasFailure && result.Methods.Count == 0 && result.UnchangedTotal > 0)
            {
                return AppendWarningCount(
                    "All " + result.UnchangedTotal
                    + " methods are unchanged since the last compile; nothing to patch."
                    + FormatStalePatchRevertNote(result.RevertedUnchangedTotal),
                    warningCount,
                    appendCompileResolution);
            }

            string message = BuildApplyOutcomeMessage(result, hasFailure, allRequestedSkipped, reappliedSiblingCount);
            message = AppendUnchangedAndLifecycleNotes(message, result);
            message = HotReloadIntroducedTypeResponseSection.AppendTypeSummary(
                message,
                result.IntroducedTypes);

            return AppendWarningCount(message, warningCount, appendCompileResolution);
        }

        // Whether the Message may say one compile clears every warning and none has to be cleared
        // first: only when the warnings allow it, and never when a declared type needs a compile
        // before it exists, which contradicts it.
        // Why an unapplied edit rules it out too: the sentence reads as "keep working", while a
        // Failed or Skipped method or a refused type is an edit that is not running yet and needs
        // a fix or a compile first.
        private static bool DecideAppendCompileResolution(
            HotReloadOrchestratorResult result,
            HotReloadResponseWarnings warnings)
        {
            if (!warnings.AllowsSingleCompileResolution)
            {
                return false;
            }

            return result.IntroducedTypeNoticeCount == 0
                && !HotReloadCompileFallbackDecider.HasUnappliedEdit(result);
        }

        private static bool ReadsInvocationCountFromLedger(HotReloadMethodOutcomeKind kind)
        {
            return kind == HotReloadMethodOutcomeKind.AlreadyActive
                || kind == HotReloadMethodOutcomeKind.Stale;
        }

        private static string BuildApplyOutcomeMessage(
            HotReloadOrchestratorResult result,
            bool hasFailure,
            bool allRequestedSkipped,
            int reappliedSiblingCount)
        {
            int addedCount = CountAddedOutcomes(result);
            if (hasFailure)
            {
                return AppendStaleSummary(BuildFailureMessage(result), result);
            }

            if (result.Methods.Count == 0)
            {
                return "Hot reload found no patchable method bodies in the given files; nothing was changed. "
                    + "Hot reload only replaces existing ordinary method bodies; use uloop compile for other edits.";
            }

            if (AreAllOutcomesAlreadyActive(result))
            {
                return string.Format(
                    HotReloadConstants.AlreadyActiveApplyMessageFormat,
                    result.Methods.Count);
            }

            // Why before the "no methods patched" message: a sibling re-apply raises PatchedTotal,
            // so that message would not be reached and the run would report an applied reload.
            if (allRequestedSkipped)
            {
                return AppendStaleSummary(BuildRequestedFilesAllSkippedMessage(addedCount), result);
            }

            if (result.PatchedTotal == 0 && addedCount == 0)
            {
                return AppendStaleSummary(
                    HotReloadConstants.NoMethodsPatchedSeeSkippedOrAlreadyActiveMessage,
                    result);
            }

            string message = "Hot reload applied. PatchedTotal=" + result.PatchedTotal
                + ", ActivePatchTotal=" + result.ActivePatchTotal + ".";
            if (addedCount > 0)
            {
                message += " Added: " + addedCount + ".";
            }

            // Why: a sibling re-apply adds its earlier rows to both counts, so a reader who edited
            // one method otherwise cannot tell why the counts are much larger than the edit.
            if (reappliedSiblingCount > 0)
            {
                message += " " + reappliedSiblingCount
                    + " of the patched and added rows re-applied changes from earlier reloads in sibling files.";
            }

            // Why counted here: the totals only count what was applied, so a run that skipped
            // some of the edits otherwise reads as if every one of them took effect.
            int skippedCount = CountOutcomesOfKind(result, HotReloadMethodOutcomeKind.Skipped);
            if (skippedCount > 0)
            {
                message += string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.SkippedCountApplyMessageSuffixFormat,
                    skippedCount);
            }

            return AppendStaleSummary(message, result);
        }

        // Why the reason is lifted into Message: a file-level refusal (an unimported .asmdef, a
        // membership guard) fails every row of the file, and the CS0103 rows that cascade from it
        // can sit above it in Methods, so a reader who only sees Message otherwise starts from a
        // symptom instead of the cause.
        private static string BuildFailureMessage(HotReloadOrchestratorResult result)
        {
            const string failureMessage = "Hot reload finished with one or more Failed method outcomes.";
            if (!HotReloadFileLevelFailureSummary.TryDescribe(result.Methods, out string fileLevelFailure))
            {
                return failureMessage + " See Methods.";
            }

            return failureMessage + " " + fileLevelFailure + " See Methods.";
        }

        // Why the sibling clause is separate: the Added rows of this run belong to a file the
        // caller did not ask about, so naming them keeps the counts readable without claiming
        // the requested edits were applied.
        private static string BuildRequestedFilesAllSkippedMessage(int addedCount)
        {
            if (addedCount == 0)
            {
                return HotReloadConstants.RequestedFilesAllSkippedMessage;
            }

            return HotReloadConstants.RequestedFilesAllSkippedMessage
                + " Also re-applied siblings: Added=" + addedCount + ".";
        }

        // Why in the summary: ActivePatchTotal counts stale patches, so without this the totals
        // look inconsistent with the listed outcomes.
        private static string AppendStaleSummary(string message, HotReloadOrchestratorResult result)
        {
            int staleCount = CountOutcomesOfKind(result, HotReloadMethodOutcomeKind.Stale);
            if (staleCount == 0)
            {
                return message;
            }

            return message + " Stale=" + staleCount + ".";
        }

        private static int CountOutcomesOfKind(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
        {
            int count = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                if (result.Methods[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static string AppendUnchangedAndLifecycleNotes(
            string message,
            HotReloadOrchestratorResult result)
        {
            if (result.UnchangedTotal > 0)
            {
                message += " " + result.UnchangedTotal + " unchanged methods were left untouched."
                    + FormatStalePatchRevertNote(result.RevertedUnchangedTotal);
            }

            int lifecycleNoteCount = CountPatchedLifecycleNotes(result);
            if (lifecycleNoteCount > 0)
            {
                // Why aggregate: per-method text already lives on Methods[].LifecycleNote;
                // dumping every note into Message repeats nearly identical paragraphs.
                message += " " + string.Format(
                    HotReloadConstants.LifecycleNotesAggregatedMessageFormat,
                    lifecycleNoteCount);
            }

            int forwardedMessageCount = CountForwardedUnityMessages(result);
            if (forwardedMessageCount > 0)
            {
                message += " " + string.Format(
                    HotReloadConstants.ForwardedUnityMessagesAggregatedMessageFormat,
                    forwardedMessageCount);
            }

            return message;
        }

        private static string FormatStalePatchRevertNote(int revertedUnchangedTotal)
        {
            if (revertedUnchangedTotal <= 0)
            {
                return string.Empty;
            }

            return " " + string.Format(
                HotReloadConstants.StalePatchesRevertedMessageFormat,
                revertedUnchangedTotal);
        }

        // Only patched rows: an added Unity message's note is about the proxy delivering it, not
        // about a one-shot method, so counting it here would overstate the one-shot rows.
        private static int CountPatchedLifecycleNotes(HotReloadOrchestratorResult result)
        {
            int lifecycleNoteCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = result.Methods[index];
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && !string.IsNullOrEmpty(outcome.LifecycleNote))
                {
                    lifecycleNoteCount++;
                }
            }

            return lifecycleNoteCount;
        }

        private static int CountForwardedUnityMessages(HotReloadOrchestratorResult result)
        {
            int forwardedCount = 0;
            for (int index = 0; index < result.Methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = result.Methods[index];
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added
                    && HotReloadUnityMessageNotes.IsForwarded(outcome.LifecycleNote))
                {
                    forwardedCount++;
                }
            }

            return forwardedCount;
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

        private static string AppendWarningCount(
            string message,
            int warningCount,
            bool appendCompileResolution)
        {
            if (warningCount <= 0)
            {
                return message;
            }

            string withCount = WarningsMessagePointer.Append(message, warningCount);
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
