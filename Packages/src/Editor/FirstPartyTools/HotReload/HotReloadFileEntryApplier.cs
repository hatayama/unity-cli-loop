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
    internal sealed class HotReloadFileEntryApplier
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadPatcher _patcher;

        internal HotReloadFileEntryApplier(HotReloadDomain domain, HotReloadPatcher patcher)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            _domain = domain;
            _patcher = patcher;
        }

        // Why the resolution is passed in: preflight for the whole group runs before any file
        // is mutated, so a match/bind/CheckPatchable failure cannot replace this file's shim or
        // added-member generation.
        internal HotReloadFileProcessResult ApplyResolvedFileAndBuildResult(
            HotReloadApplyContext context,
            HotReloadGroupFile file,
            HotReloadShimCompileResult compileResult,
            TransformWorkerEntryDto[] fileEntries,
            HotReloadEntryResolution.Result resolution)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(file != null, "file must not be null.");
            Debug.Assert(fileEntries.Length > 0, "An applied file must hold an entry.");
            Debug.Assert(resolution != null && resolution.AllResolved, "resolution must be resolved.");

            _domain.BeginGeneration(
                file.ProjectRelativePath,
                compileResult.AssemblyBytes,
                compileResult.PdbBytes,
                compileResult.Assembly);
            CommitAddedFieldsForFile(file.ProjectRelativePath, file.AddedFieldNames);
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
        /// The result of a file whose preflight resolution failed: its failure outcomes are
        /// reported and nothing of this file is applied.
        /// </summary>
        internal HotReloadFileProcessResult BuildResolutionFailedResult(
            HotReloadApplyContext context,
            HotReloadGroupFile file,
            HotReloadEntryResolution.Result resolution)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(file != null, "file must not be null.");
            Debug.Assert(resolution != null, "resolution must not be null.");

            file.Sinks.Outcomes.AddRange(resolution.FailureOutcomes);
            return FinishFileResult(context, file, patchedCount: 0, applied: false);
        }

        /// <summary>
        /// Drops the file's stale added members when the run left it with no entry to patch.
        /// </summary>
        /// <remarks>
        /// Why the added-member-only start: this file contributed no body to the shim assembly,
        /// so it has no shim generation to replace.
        /// </remarks>
        internal void ClearFileGeneration(HotReloadApplyContext context, HotReloadGroupFile file)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(file != null, "file must not be null.");

            IReadOnlyList<string> addedLabelsAtClear =
                _domain.ListActiveAddedMethodKeys(file.ProjectRelativePath);
            HotReloadOrchestratorLog.LogHotReloadEmptyEntriesClear(addedLabelsAtClear, context.CorrelationId);
            _domain.BeginAddedMemberOnlyGeneration(file.ProjectRelativePath);
            // Why AddedFieldNames first: a retry (gate or isolation) replaces this file's added
            // field names, and committing the first-pass names would resurrect a field the
            // retry no longer emits. The worker row is the first-pass fallback.
            string[] addedFieldNames = file.AddedFieldNames ?? file.FileOutput.addedFieldNames;
            CommitAddedFieldsForFile(file.ProjectRelativePath, addedFieldNames);
            // Why recorded: a file that only declares an added member has no entry of its own,
            // yet a sibling file's applied body uses that field, so the run must report it.
            file.ClearedAddedFieldNames = addedFieldNames;
            // Why after the clear: a still-declared added method can be worker-skipped
            // (virtual/generic), leaving entries empty while the registry drop is real.
            HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                _domain,
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
        // Why only here and the empty-entries deactivation: a failed worker or shim compile
        // returns empty AddedFieldNames while leaving existing patches, so writing the ledger
        // from the run response would wipe added fields that are still live.
        private void CommitAddedFieldsForFile(string projectRelativePath, string[] addedFieldNames)
        {
            _domain.FindGeneration(projectRelativePath)?.ReplaceAddedFields(
                addedFieldNames ?? Array.Empty<string>());
        }

        internal HotReloadFileProcessResult BuildUnappliedResult(HotReloadGroupFile file)
        {
            Debug.Assert(file != null, "file must not be null.");

            HotReloadFileSinks sinks = file.Sinks;
            return new HotReloadFileProcessResult(
                outcomes: sinks.Outcomes,
                warnings: sinks.Warnings,
                patchedCount: 0,
                suppressedPausePointIds: sinks.SuppressedPausePointIds,
                inlineRiskMethodLabels: new List<string>(),
                unchangedMethodCount: file.UnchangedMethodCount,
                retargetedPausePointIds: sinks.RetargetedPausePointIds,
                addedFieldNames: file.ClearedAddedFieldNames,
                sourceContentSha256: file.FileOutput != null ? file.FileOutput.sourceContentSha256 : null,
                revertedUnchangedCount: file.RevertedUnchangedCount,
                introducedTypes: sinks.IntroducedTypes);
        }

        /// <summary>
        /// The results of a group no file of which was applied, one unapplied result per file.
        /// </summary>
        internal List<HotReloadFileProcessResult> BuildUnappliedGroupResults(
            IReadOnlyList<HotReloadGroupFile> files)
        {
            List<HotReloadFileProcessResult> results =
                new List<HotReloadFileProcessResult>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                results.Add(BuildUnappliedResult(file));
            }

            return results;
        }
        private HotReloadFileProcessResult FinishFileResult(
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
                _domain,
                sinks.Warnings,
                file.SnapshotLabels,
                file.SnapshotAddedLabels,
                file.ProjectRelativePath,
                context.WorkerOutput,
                sinks.Outcomes);
            return new HotReloadFileProcessResult(
                outcomes: sinks.Outcomes,
                warnings: sinks.Warnings,
                patchedCount: patchedCount,
                suppressedPausePointIds: sinks.SuppressedPausePointIds,
                inlineRiskMethodLabels: inlineRiskMethodLabels ?? new List<string>(),
                unchangedMethodCount: file.UnchangedMethodCount,
                retargetedPausePointIds: sinks.RetargetedPausePointIds,
                addedFieldNames: applied ? file.AddedFieldNames : null,
                sourceContentSha256: file.FileOutput.sourceContentSha256,
                addedConstNames: applied ? file.AddedConstNames : null,
                revertedUnchangedCount: file.RevertedUnchangedCount,
                introducedTypes: sinks.IntroducedTypes);
        }

        private int ApplyResolvedEntries(
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
                    sinks.AppliedEntries.Add(resolvedEntries[index].Entry);
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

        private void AppendOneShotCallerNoteCandidate(
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
                    new HotReloadMetadataTypeName(resolved.Entry.typeMetadataName),
                    resolved.Entry.methodName,
                    resolved.Entry.parameterTypeFullNames ?? Array.Empty<string>(),
                    resolved.Entry.genericArity);
            candidates.Add(new HotReloadOneShotCallerNoteEnricher.Candidate(identity, outcome));
        }

        private HotReloadMethodOutcome ApplyResolvedEntry(
            HotReloadEntryResolution.ResolvedEntry resolved,
            string projectRelativePath,
            List<string> inlineRiskMethodLabels,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            HotReloadFileGeneration generation =
                _domain.FindGeneration(projectRelativePath);
            Debug.Assert(generation != null, "The file's generation must have started before its entries apply.");
            if (resolved.IsAddedMethod)
            {
                generation.RegisterAddedMethod(
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
            generation.RegisterShimMethod(
                resolved.OriginalMethod,
                new HotReloadShimMethodEntry(
                    resolved.ShimMethod,
                    resolved.PatchShape == HotReloadPatchShape.Delegation,
                    resolved.Entry.sourceStartLine,
                    resolved.Entry.sourceEndLine));
            HotReloadPatchResult patchResult = _patcher.Apply(
                resolved.OriginalMethod,
                resolved.ShimMethod,
                resolved.PatchShape,
                projectRelativePath);
            if (!patchResult.Success)
            {
                generation.RemoveShimMethod(resolved.OriginalMethod);
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
        private void AppendPausePointTransitionIds(
            MethodBase method,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            IPausePointHotReloadPort pausePointSide = HotReloadPausePointCoordination.PausePointSide;
            IReadOnlyList<string> armedIds = pausePointSide?.GetArmedMarkerIdsOnMethod(method);
            if (armedIds == null || armedIds.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> suppressedIds =
                pausePointSide.GetSuppressedMarkerIdsOnMethod(method) ?? Array.Empty<string>();

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
