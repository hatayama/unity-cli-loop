using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies worker entries: bind accessors, Harmony patch/revert, added-method register.
    /// </summary>
    internal static class HotReloadEntryApplier
    {
        // Why preflight before BeginFileGeneration: a match/bind/CheckPatchable failure
        // must not replace the file's shim or added-member generation.
        internal static HotReloadOrchestrator.HotReloadFileProcessResult ApplyEntriesAndBuildResult(
            string assemblyName,
            string assemblyResolvePath,
            string projectRelativePath,
            HotReloadShimCompileResult compileResult,
            TransformWorkerEntryDto[] entriesToPatch,
            string[] addedFieldNames,
            TransformWorkerOutputDto workerOutput,
            HashSet<string> snapshotLabels,
            HashSet<string> snapshotAddedLabels,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds,
            int unchangedMethodCount)
        {
            HotReloadEntryResolution.Result resolution = HotReloadEntryResolution.ResolveEntries(
                assemblyName,
                assemblyResolvePath,
                compileResult.Assembly,
                entriesToPatch);
            if (!resolution.AllResolved)
            {
                outcomes.AddRange(resolution.FailureOutcomes);
                return FinishFileResult(
                    outcomes,
                    warnings,
                    snapshotLabels,
                    snapshotAddedLabels,
                    projectRelativePath,
                    workerOutput,
                    suppressedPausePointIds,
                    retargetedPausePointIds,
                    unchangedMethodCount,
                    patchedCount: 0,
                    addedFieldNames: null);
            }

            HotReloadShimRegistry.BeginFileGeneration(
                projectRelativePath,
                compileResult.AssemblyBytes,
                compileResult.PdbBytes,
                compileResult.Assembly);
            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
            CommitAddedFieldsForFile(projectRelativePath, addedFieldNames);
            // Why bind again: phase 1 already bound to detect failures; phase 2 keeps the
            // historical apply order so a successful preflight still runs Bind before Patch.
            BindShimAccessors(compileResult.Assembly);
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedCount = ApplyResolvedEntries(
                resolution.ResolvedEntries,
                entriesToPatch,
                assemblyResolvePath,
                projectRelativePath,
                outcomes,
                warnings,
                inlineRiskMethodLabels,
                suppressedPausePointIds,
                retargetedPausePointIds);

            return FinishFileResult(
                outcomes,
                warnings,
                snapshotLabels,
                snapshotAddedLabels,
                projectRelativePath,
                workerOutput,
                suppressedPausePointIds,
                retargetedPausePointIds,
                unchangedMethodCount,
                patchedCount,
                addedFieldNames,
                inlineRiskMethodLabels);
        }

        private static HotReloadOrchestrator.HotReloadFileProcessResult FinishFileResult(
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            HashSet<string> snapshotLabels,
            HashSet<string> snapshotAddedLabels,
            string projectRelativePath,
            TransformWorkerOutputDto workerOutput,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds,
            int unchangedMethodCount,
            int patchedCount,
            string[] addedFieldNames,
            List<string> inlineRiskMethodLabels = null)
        {
            // Why here as well as the empty-entries return: apply can drop a still-declared
            // added member by not re-Registering it after BeginFileGeneration.
            HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                warnings,
                snapshotLabels,
                snapshotAddedLabels,
                projectRelativePath,
                workerOutput,
                outcomes);
            return new HotReloadOrchestrator.HotReloadFileProcessResult(
                outcomes,
                warnings,
                patchedCount,
                suppressedPausePointIds,
                inlineRiskMethodLabels ?? new List<string>(),
                unchangedMethodCount,
                retargetedPausePointIds,
                addedFieldNames,
                workerOutput.sourceContentSha256);
        }

        private static int ApplyResolvedEntries(
            IReadOnlyList<HotReloadEntryResolution.ResolvedEntry> resolvedEntries,
            TransformWorkerEntryDto[] entriesToPatch,
            string assemblyResolvePath,
            string projectRelativePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            List<string> inlineRiskMethodLabels,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            int patchedCount = 0;
            int appliedThisRun = 0;
            for (int index = 0; index < resolvedEntries.Count; index++)
            {
                HotReloadMethodOutcome outcome = ApplyResolvedEntry(
                    resolvedEntries[index],
                    projectRelativePath,
                    inlineRiskMethodLabels,
                    suppressedPausePointIds,
                    retargetedPausePointIds);
                outcomes.Add(outcome);
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
                    assemblyResolvePath);
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

        // Why only here and the empty-entries deactivation: a failed worker or shim compile
        // returns empty AddedFieldNames while leaving existing patches, so writing the ledger
        // from the run response would wipe added fields that are still live.
        internal static void CommitAddedFieldsForFile(string projectRelativePath, string[] addedFieldNames)
        {
            HotReloadAddedFieldRegistry.ReplaceForFile(
                projectRelativePath,
                addedFieldNames ?? Array.Empty<string>());
        }

        // Peels leftover Harmony patches when the source again matches the verified baseline.
        // Resolve failures are silent: unchanged identities already matched compile-time IL.
        internal static void RevertUnchangedPatches(
            string assemblyName,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            for (int index = 0; index < unchangedMethods.Length; index++)
            {
                TransformWorkerUnchangedMethodDto unchanged = unchangedMethods[index];
                if (unchanged == null
                    || string.IsNullOrEmpty(unchanged.typeMetadataName)
                    || string.IsNullOrEmpty(unchanged.methodName)
                    || unchanged.parameterTypeFullNames == null)
                {
                    continue;
                }

                // Why pass unchanged.genericArity: Caller(int) and Caller<T>(int) share name
                // and parameters. Arity 0 would resolve the generic unchanged row to the
                // non-generic sibling and peel its live patch.
                HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                    assemblyName,
                    unchanged.typeMetadataName,
                    unchanged.methodName,
                    unchanged.parameterTypeFullNames,
                    unchanged.genericArity);
                if (!matchResult.Success)
                {
                    continue;
                }

                HotReloadPatcher.Revert(matchResult.Method);
            }
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

        /// <summary>
        /// Invokes each shim type's binder (emitted when the type carries at least one accessor
        /// delegate) once, before any patch is applied, so no delegation shim or added-method
        /// accessor rewrite can run with unbound accessor delegates. Returns bind failures keyed
        /// by shim type name; every delegation entry and added-method entry in a failed type
        /// becomes Failed instead of being patched or registered.
        /// Internal so tests can pin the failure contract directly — an end-to-end bind failure
        /// cannot be fabricated once shim compilation has succeeded against the same assembly.
        /// </summary>
        internal static Dictionary<string, string> BindShimAccessors(Assembly shimAssembly)
        {
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");

            Dictionary<string, string> failureReasonByShimTypeName = new Dictionary<string, string>();
            foreach (Type shimType in shimAssembly.GetTypes())
            {
                MethodInfo bindMethod = shimType.GetMethod(
                    HotReloadConstants.ShimBindAccessorsMethodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (bindMethod == null)
                {
                    continue;
                }

                try
                {
                    bindMethod.Invoke(null, null);
                }
                catch (TargetInvocationException invocationException)
                {
                    // Approved deviation from the no-try-catch rule: a bind failure (the source
                    // references a member the compiled assembly does not have yet) is an expected
                    // per-type outcome that must fail that type's methods with a remediation hint,
                    // not crash the whole hot-reload run. Nothing is swallowed — the cause becomes
                    // the Failed reason for every affected method.
                    Exception cause = invocationException.InnerException ?? invocationException;
                    failureReasonByShimTypeName[shimType.Name] =
                        "Accessor binding failed for shim type '" + shimType.Name + "': "
                        + cause.Message + " Run 'uloop compile' and retry.";
                }
            }

            return failureReasonByShimTypeName;
        }
    }
}
