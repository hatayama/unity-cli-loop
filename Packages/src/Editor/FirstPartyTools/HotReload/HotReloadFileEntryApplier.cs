using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies one file of a group: preflight resolution, generation start, added-field commit,
    /// Harmony patch/register, and that file's result.
    /// </summary>
    internal static class HotReloadFileEntryApplier
    {
        // Why preflight before BeginFileGeneration: a match/bind/CheckPatchable failure
        // must not replace this file's shim or added-member generation.
        internal static HotReloadFileProcessResult ApplyFileAndBuildResult(
            HotReloadApplyContext context,
            HotReloadGroupFile file,
            HotReloadShimCompileResult compileResult,
            TransformWorkerEntryDto[] fileEntries,
            Dictionary<string, string> bindFailures)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(file != null, "file must not be null.");
            Debug.Assert(fileEntries.Length > 0, "An applied file must hold an entry.");

            HotReloadFileSinks sinks = file.Sinks;
            HotReloadEntryResolution.Result resolution = HotReloadEntryResolution.ResolveEntries(
                context.AssemblyName,
                file.AssemblyResolvePath,
                compileResult.Assembly,
                fileEntries,
                bindFailures);
            if (!resolution.AllResolved)
            {
                sinks.Outcomes.AddRange(resolution.FailureOutcomes);
                return FinishFileResult(context, file, patchedCount: 0, applied: false);
            }

            HotReloadFileGenerations.BeginFileGeneration(
                file.ProjectRelativePath,
                compileResult.AssemblyBytes,
                compileResult.PdbBytes,
                compileResult.Assembly);
            HotReloadEntryApplier.CommitAddedFieldsForFile(file.ProjectRelativePath, file.AddedFieldNames);
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedCount = ApplyResolvedEntries(
                resolution.ResolvedEntries,
                fileEntries,
                file,
                context.AssemblyName,
                inlineRiskMethodLabels);

            return FinishFileResult(context, file, patchedCount, applied: true, inlineRiskMethodLabels);
        }

        /// <summary>
        /// Drops the file's stale added members when the run left it with no entry to patch.
        /// </summary>
        /// <remarks>
        /// Why not HotReloadFileGenerations.BeginFileGeneration: this file contributed no body to
        /// the shim assembly, so there are no bytes to register a shim generation with. Only the
        /// added-member side has stale rows to drop.
        /// </remarks>
        internal static void ClearFileGeneration(HotReloadApplyContext context, HotReloadGroupFile file)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(file != null, "file must not be null.");

            IReadOnlyList<string> addedLabelsAtClear =
                HotReloadFileGenerations.ListActiveAddedMethodKeys(file.ProjectRelativePath);
            HotReloadOrchestratorLog.LogHotReloadEmptyEntriesClear(addedLabelsAtClear, context.CorrelationId);
            HotReloadAddedMemberRegistry.BeginFileGeneration(file.ProjectRelativePath);
            HotReloadEntryApplier.CommitAddedFieldsForFile(
                file.ProjectRelativePath,
                file.FileOutput.addedFieldNames);
            // Why recorded: a file that only declares an added member has no entry of its own,
            // yet a sibling file's applied body uses that field, so the run must report it.
            file.ClearedAddedFieldNames = file.FileOutput.addedFieldNames;
            // Why after the clear: a still-declared added method can be worker-skipped
            // (virtual/generic), leaving entries empty while the registry drop is real.
            HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                file.Sinks.Warnings,
                file.SnapshotLabels,
                file.SnapshotAddedLabels,
                file.ProjectRelativePath,
                context.WorkerOutput,
                file.Sinks.Outcomes);
        }

        /// <summary>
        /// The result of a file the group never applied: a group-level failure, a preflight
        /// failure of another stage, or a file left with no entry to patch. It reports added
        /// field names only when the clear path committed them.
        /// </summary>
        internal static HotReloadFileProcessResult BuildUnappliedResult(HotReloadGroupFile file)
        {
            Debug.Assert(file != null, "file must not be null.");

            HotReloadFileSinks sinks = file.Sinks;
            return new HotReloadFileProcessResult(
                sinks.Outcomes,
                sinks.Warnings,
                0,
                sinks.SuppressedPausePointIds,
                new List<string>(),
                file.UnchangedMethodCount,
                sinks.RetargetedPausePointIds,
                addedFieldNames: file.ClearedAddedFieldNames,
                sourceContentSha256: file.FileOutput != null ? file.FileOutput.sourceContentSha256 : null,
                revertedUnchangedCount: file.RevertedUnchangedCount);
        }

        private static HotReloadFileProcessResult FinishFileResult(
            HotReloadApplyContext context,
            HotReloadGroupFile file,
            int patchedCount,
            bool applied,
            List<string> inlineRiskMethodLabels = null)
        {
            HotReloadFileSinks sinks = file.Sinks;
            // Why here as well as the empty-entries return: apply can drop a still-declared
            // added member by not re-Registering it after BeginFileGeneration.
            HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                sinks.Warnings,
                file.SnapshotLabels,
                file.SnapshotAddedLabels,
                file.ProjectRelativePath,
                context.WorkerOutput,
                sinks.Outcomes);
            return new HotReloadFileProcessResult(
                sinks.Outcomes,
                sinks.Warnings,
                patchedCount,
                sinks.SuppressedPausePointIds,
                inlineRiskMethodLabels ?? new List<string>(),
                file.UnchangedMethodCount,
                sinks.RetargetedPausePointIds,
                applied ? file.AddedFieldNames : null,
                file.FileOutput.sourceContentSha256,
                applied ? file.AddedConstNames : null,
                file.RevertedUnchangedCount);
        }

        private static int ApplyResolvedEntries(
            IReadOnlyList<HotReloadEntryResolution.ResolvedEntry> resolvedEntries,
            TransformWorkerEntryDto[] entriesToPatch,
            HotReloadGroupFile file,
            string assemblyName,
            List<string> inlineRiskMethodLabels)
        {
            HotReloadFileSinks sinks = file.Sinks;
            List<HotReloadMethodOutcome> outcomes = sinks.Outcomes;
            List<string> warnings = sinks.Warnings;
            int patchedCount = 0;
            int appliedThisRun = 0;
            for (int index = 0; index < resolvedEntries.Count; index++)
            {
                HotReloadMethodOutcome outcome = ApplyResolvedEntry(
                    resolvedEntries[index],
                    file.ProjectRelativePath,
                    inlineRiskMethodLabels,
                    sinks.SuppressedPausePointIds,
                    sinks.RetargetedPausePointIds);
                outcomes.Add(outcome);
                AppendOneShotCallerNoteCandidate(
                    resolvedEntries[index],
                    outcome,
                    assemblyName,
                    sinks.OneShotCallerNoteCandidates);
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    || outcome.Kind == HotReloadMethodOutcomeKind.Added)
                {
                    appliedThisRun++;
                    if (outcome.Kind == HotReloadMethodOutcomeKind.Patched)
                    {
                        patchedCount++;
                    }

                    continue;
                }

                if (outcome.Kind != HotReloadMethodOutcomeKind.Failed)
                {
                    continue;
                }

                HotReloadEntryResolution.AppendAtomicSkipOutcomes(
                    outcomes,
                    entriesToPatch,
                    index + 1,
                    file.AssemblyResolvePath);
                if (appliedThisRun >= 1)
                {
                    warnings.Add(
                        string.Format(
                            HotReloadConstants.PartialApplyAfterPatchEngineFailureWarningFormat,
                            appliedThisRun));
                }

                break;
            }

            return patchedCount;
        }

        private static void AppendOneShotCallerNoteCandidate(
            HotReloadEntryResolution.ResolvedEntry resolved,
            HotReloadMethodOutcome outcome,
            string assemblyName,
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            if ((outcome.Kind != HotReloadMethodOutcomeKind.Patched
                    && outcome.Kind != HotReloadMethodOutcomeKind.Added)
                || !string.IsNullOrEmpty(outcome.LifecycleNote))
            {
                return;
            }

            HotReloadCallSiteScanner.CompiledMethodIdentity identity =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    resolved.Entry.typeMetadataName,
                    resolved.Entry.methodName,
                    resolved.Entry.parameterTypeFullNames ?? Array.Empty<string>(),
                    resolved.Entry.genericArity);
            candidates.Add(new HotReloadOneShotCallerNoteEnricher.Candidate(identity, outcome));
        }

        private static HotReloadMethodOutcome ApplyResolvedEntry(
            HotReloadEntryResolution.ResolvedEntry resolved,
            string projectRelativePath,
            List<string> inlineRiskMethodLabels,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            if (resolved.IsAddedMethod)
            {
                HotReloadAddedMemberRegistry.Register(
                    projectRelativePath,
                    resolved.MethodLabel,
                    resolved.ShimMethod,
                    resolved.FilePath);
                return HotReloadMethodOutcome.Added(
                    resolved.MethodLabel,
                    resolved.FilePath,
                    resolved.Entry.lifecycleNote);
            }

            // Why before Apply: Apply notifies OnHotReloadPatchStateChanged(true) after the
            // ledger write; registration must already expose this method's shim for retarget.
            HotReloadShimRegistry.RegisterMethod(
                projectRelativePath,
                resolved.OriginalMethod,
                new HotReloadShimRegistry.MethodEntry(
                    resolved.ShimMethod,
                    resolved.PatchShape == HotReloadPatchShape.Delegation,
                    resolved.Entry.sourceStartLine,
                    resolved.Entry.sourceEndLine));
            HotReloadPatchResult patchResult = HotReloadPatcher.Apply(
                resolved.OriginalMethod,
                resolved.ShimMethod,
                resolved.PatchShape,
                projectRelativePath);
            if (!patchResult.Success)
            {
                HotReloadShimRegistry.RemoveMethod(resolved.OriginalMethod);
                return HotReloadMethodOutcome.Failed(
                    resolved.MethodLabel,
                    patchResult.ErrorMessage,
                    resolved.FilePath);
            }

            AppendPausePointTransitionIds(
                resolved.OriginalMethod,
                suppressedPausePointIds,
                retargetedPausePointIds);

            // Inline risk is flagged per method but reported as one aggregated warning so
            // Warnings stay readable when many tiny methods are patched together.
            if (patchResult.InlineRiskDetected)
            {
                inlineRiskMethodLabels.Add(resolved.MethodLabel);
            }

            return HotReloadMethodOutcome.Patched(
                resolved.MethodLabel,
                resolved.FilePath,
                resolved.Entry.lifecycleNote);
        }

        // What: after Apply (+ retarget handler), splits armed markers into retargeted vs suppressed.
        // Expired skips are recorded as a pending-drain event inside SourcePausePointPatcher and
        // surfaced from HotReloadTools.BuildApplyResponse (same pattern as line-drift warnings).
        private static void AppendPausePointTransitionIds(
            MethodBase method,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            IReadOnlyList<string> armedIds =
                HotReloadPausePointCoordination.GetArmedMarkerIdsOnMethod?.Invoke(method);
            if (armedIds == null || armedIds.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> suppressedIds =
                HotReloadPausePointCoordination.GetSuppressedMarkerIdsOnMethod?.Invoke(method)
                ?? Array.Empty<string>();

            // The same method can be patched twice in one run (duplicate file inputs,
            // re-applied edits); the aggregated warning must list each marker id once.
            foreach (string armedId in armedIds)
            {
                bool suppressed = false;
                for (int index = 0; index < suppressedIds.Count; index++)
                {
                    if (suppressedIds[index] == armedId)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                {
                    if (!suppressedPausePointIds.Contains(armedId))
                    {
                        suppressedPausePointIds.Add(armedId);
                    }
                }
                else if (!retargetedPausePointIds.Contains(armedId))
                {
                    retargetedPausePointIds.Add(armedId);
                }
            }
        }

    }
}
