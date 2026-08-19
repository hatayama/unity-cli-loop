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
        // Why before ApplyEntry: OnHotReloadPatchStateChanged(true) runs inside Apply and
        // pause-point retarget reads the shim registration — it must already see this
        // generation's bytes/methods.
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
            HotReloadShimRegistry.BeginFileGeneration(
                projectRelativePath,
                compileResult.AssemblyBytes,
                compileResult.PdbBytes,
                compileResult.Assembly);
            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
            CommitAddedFieldsForFile(projectRelativePath, addedFieldNames);
            Dictionary<string, string> bindFailureReasonByShimTypeName =
                BindShimAccessors(compileResult.Assembly);
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedCount = 0;
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                HotReloadMethodOutcome outcome = ApplyEntry(
                    entry,
                    assemblyName,
                    compileResult.Assembly,
                    bindFailureReasonByShimTypeName,
                    assemblyResolvePath,
                    projectRelativePath,
                    inlineRiskMethodLabels,
                    suppressedPausePointIds,
                    retargetedPausePointIds);
                outcomes.Add(outcome);
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched)
                {
                    patchedCount++;
                }
            }

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
                inlineRiskMethodLabels,
                unchangedMethodCount,
                retargetedPausePointIds,
                addedFieldNames,
                workerOutput.sourceContentSha256);
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

        internal static HotReloadMethodOutcome ApplyEntry(
            TransformWorkerEntryDto entry,
            string assemblyName,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailureReasonByShimTypeName,
            string filePath,
            string projectRelativePath,
            List<string> inlineRiskMethodLabels,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();
            // Pre-Resolve label: same shape as --status (params + nested '+' normalization).
            // After Resolve, prefer FormatMethodKey(MethodBase) so reflection ToString() wins.
            string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                entry.genericArity);

            if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
            {
                return ApplyAddedMethodEntry(
                    entry,
                    methodLabel,
                    shimAssembly,
                    bindFailureReasonByShimTypeName,
                    filePath,
                    projectRelativePath);
            }

            // Only "delegation" selects the forwarding patch; null/empty/anything else is transplant.
            HotReloadPatchShape patchShape = entry.patchKind == HotReloadConstants.PatchKindDelegation
                ? HotReloadPatchShape.Delegation
                : HotReloadPatchShape.Transplant;

            // Transplant entries never read accessor delegates, so a sibling bind failure in the
            // same shim type must not take them down; only delegation entries depend on the bind.
            if (patchShape == HotReloadPatchShape.Delegation
                && bindFailureReasonByShimTypeName.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath);
            }

            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                assemblyName,
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                entry.genericArity);
            if (!matchResult.Success)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, matchResult.ErrorMessage, filePath);
            }

            methodLabel = HotReloadPatcher.FormatMethodKey(matchResult.Method);

            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
            if (shimType == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim type not found in compiled shim assembly: " + entry.shimTypeName,
                    filePath);
            }

            (MethodInfo shimMethod, string shimLookupError) = FindShimMethod(shimType, entry.shimMethodName);
            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, shimLookupError, filePath);
            }

            // Why before Apply: Apply notifies OnHotReloadPatchStateChanged(true) after the
            // ledger write; registration must already expose this method's shim for retarget.
            HotReloadShimRegistry.RegisterMethod(
                projectRelativePath,
                matchResult.Method,
                new HotReloadShimRegistry.MethodEntry(
                    shimMethod,
                    patchShape == HotReloadPatchShape.Delegation,
                    entry.sourceStartLine,
                    entry.sourceEndLine));
            HotReloadPatchResult patchResult = HotReloadPatcher.Apply(
                matchResult.Method,
                shimMethod,
                patchShape,
                projectRelativePath);
            if (!patchResult.Success)
            {
                HotReloadShimRegistry.RemoveMethod(matchResult.Method);
                return HotReloadMethodOutcome.Failed(methodLabel, patchResult.ErrorMessage, filePath);
            }

            AppendPausePointTransitionIds(
                matchResult.Method,
                suppressedPausePointIds,
                retargetedPausePointIds);

            // Inline risk is flagged per method but reported as one aggregated warning so
            // Warnings stay readable when many tiny methods are patched together.
            if (patchResult.InlineRiskDetected)
            {
                inlineRiskMethodLabels.Add(methodLabel);
            }

            return HotReloadMethodOutcome.Patched(methodLabel, filePath, entry.lifecycleNote);
        }

        internal static HotReloadMethodOutcome ApplyAddedMethodEntry(
            TransformWorkerEntryDto entry,
            string methodLabel,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailureReasonByShimTypeName,
            string filePath,
            string projectRelativePath)
        {
            // Added methods with accessors share the shim type's __BindAccessors; a bind
            // failure leaves those delegates unbound, so the entry must not be registered.
            if (bindFailureReasonByShimTypeName.TryGetValue(
                    entry.shimTypeName ?? string.Empty,
                    out string bindFailureReason))
            {
                return HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath);
            }

            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
            if (shimType == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim type not found in compiled shim assembly: " + entry.shimTypeName,
                    filePath);
            }

            (MethodInfo shimMethod, string shimLookupError) = FindShimMethod(shimType, entry.shimMethodName);
            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, shimLookupError, filePath);
            }

            HotReloadAddedMemberRegistry.Register(
                projectRelativePath,
                methodLabel,
                shimMethod,
                filePath);
            return HotReloadMethodOutcome.Added(methodLabel, filePath, entry.lifecycleNote);
        }

        internal static (MethodInfo ShimMethod, string ErrorMessage) FindShimMethod(
            Type shimType,
            string shimMethodName)
        {
            MethodInfo shimMethod = shimType.GetMethod(
                shimMethodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (shimMethod == null)
            {
                // Fall back to a broader lookup — DeclaredOnly can miss when the compiler emits
                // unexpected metadata flags, but still prefer public static.
                shimMethod = shimType.GetMethod(
                    shimMethodName,
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (shimMethod == null)
            {
                return (null, "Shim method not found: " + shimType.Name + "." + shimMethodName);
            }

            return (shimMethod, null);
        }

        // What: after Apply (+ retarget handler), splits armed markers into retargeted vs suppressed.
        // Expired skips are recorded as a pending-drain event inside SourcePausePointPatcher and
        // surfaced from HotReloadTools.BuildApplyResponse (same pattern as line-drift warnings).
        internal static void AppendPausePointTransitionIds(
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

        internal static Type FindShimType(Assembly shimAssembly, string shimTypeName)
        {
            if (string.IsNullOrEmpty(shimTypeName))
            {
                return null;
            }

            // Prefer the short-name lookup used when shims are in the global namespace; fall back
            // to scanning because production emits shims into the original type's namespace.
            Type direct = shimAssembly.GetType(shimTypeName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Type candidate in shimAssembly.GetTypes())
            {
                if (candidate.Name == shimTypeName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
