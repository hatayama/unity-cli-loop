using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Gates compiled-signature replacements whose leftover callers are not in this edit.
    /// </summary>
    internal static class HotReloadSignatureChangeGate
    {
        /// <summary>
        /// Scans compiled call sites after worker #1 and before the first shim compile. No
        /// trigger means the scanner is not called.
        /// </summary>
        internal static async Task<SignatureChangeGateResult> TryApplySignatureChangeGateAsync(
            HotReloadApplyContext context,
            CancellationToken ct)
        {
            Debug.Assert(context != null, "context must not be null.");
            TransformWorkerEntryDto[] entries = context.WorkerOutput.entries ?? Array.Empty<TransformWorkerEntryDto>();
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures = context.RemovedMethodSignatures;
            List<TransformWorkerEntryDto> replacementEntries = CollectReplacementEntries(entries);
            if (replacementEntries.Count == 0 && removedSignatures.Length == 0)
            {
                return SignatureChangeGateResult.NoWork();
            }

            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets = CollectScanTargets(
                context.AssemblyName,
                replacementEntries,
                removedSignatures);
            HashSet<HotReloadQualifiedMethodIdentity> deletedCallerExemptions =
                HotReloadDeletedCallerExemptions.Collect(
                    context.AssemblyName,
                    entries,
                    context.WorkerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>(),
                    context.WorkerOutput.skipped ?? Array.Empty<TransformWorkerSkippedDto>(),
                    removedSignatures);
            List<HotReloadCallSiteScanner.CallSiteHit> hits =
                HotReloadCallSiteScanner.FindCallSites(context.ProjectRoot, targets).Hits;
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget =
                CollectInitialUncoveredCallers(context.AssemblyName, entries, hits, deletedCallerExemptions);

            List<string> staleWarnings = HotReloadSignatureChangeCoverage.CollectStaleSignatureWarnings(
                removedSignatures,
                uncoveredCallersByTarget);
            List<TransformWorkerEntryDto> gatedReplacements = CollectGatedReplacementEntries(
                replacementEntries,
                uncoveredCallersByTarget);
            if (gatedReplacements.Count == 0)
            {
                return SignatureChangeGateResult.WarningsOnly(
                    staleWarnings,
                    hits,
                    deletedCallerExemptions);
            }

            HotReloadShimIsolation.IsolationExclusions exclusions = HotReloadShimIsolation.BuildIsolationExclusions(gatedReplacements, entries);
            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> editedFileMethodIdentitiesByFile =
                HotReloadSignatureChangeCoverage.CollectEditedFileMethodIdentitiesByFile(
                    context.AssemblyName,
                    entries,
                    context.WorkerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>());
            List<HotReloadMethodOutcome> skippedOutcomes = BuildGatedReplacementSkipOutcomes(
                gatedReplacements,
                uncoveredCallersByTarget,
                editedFileMethodIdentitiesByFile,
                context.GroupFilePaths);
            skippedOutcomes.AddRange(
                HotReloadShimIsolation.BuildSkippedCallerOutcomes(
                    exclusions.CallerEntries,
                    context.GroupFilePaths,
                    HotReloadConstants.SignatureChangedGatedCallerSkipReason));

            HotReloadShimIsolation.IsolationRetryRunResult retry = await HotReloadShimIsolation.RunIsolationRetryAsync(
                context.WorkerInput,
                exclusions,
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadMethodOutcome>(),
                context.CompilationAssembly,
                context.TargetDllPath,
                context.Defines,
                context.WorkerOutput.skipped,
                context.GroupFilePaths,
                HotReloadConstants.VibeLogIsolationTriggerSignatureChangeGate,
                context.CorrelationId,
                ct).ConfigureAwait(false);
            List<string> gatedReplacementMethodKeys =
                CollectGatedReplacementMethodKeys(gatedReplacements);
            if (retry.Isolation == null)
            {
                return SignatureChangeGateResult.Failed(
                    retry.FailureMessage,
                    gatedReplacementMethodKeys);
            }

            // Why merge here: gate consumption adds SkippedOutcomes only. Retry-only skips live
            // on Isolation.SkippedCallerOutcomes and would drop again without this join.
            skippedOutcomes.AddRange(retry.Isolation.SkippedCallerOutcomes);
            return SignatureChangeGateResult.Retried(
                retry.Isolation,
                skippedOutcomes,
                staleWarnings,
                hits,
                deletedCallerExemptions,
                gatedReplacementMethodKeys);
        }

        private static List<TransformWorkerEntryDto> CollectReplacementEntries(
            TransformWorkerEntryDto[] entries)
        {
            List<TransformWorkerEntryDto> replacements = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                if (entry.replacesCompiledMethod)
                {
                    replacements.Add(entry);
                }
            }

            return replacements;
        }

        /// <summary>
        /// Classifies scanned callers against the first worker output using the edited assembly.
        /// </summary>
        internal static Dictionary<string, List<HotReloadQualifiedMethodIdentity>> CollectInitialUncoveredCallers(
            string assemblyName,
            TransformWorkerEntryDto[] entries,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            IReadOnlyCollection<HotReloadQualifiedMethodIdentity> deletedCallerExemptions)
        {
            HashSet<HotReloadQualifiedMethodIdentity> coveredIdentities =
                HotReloadSignatureChangeCoverage.CollectCoveredMethodIdentities(
                    assemblyName,
                    entries);
            coveredIdentities.UnionWith(deletedCallerExemptions);
            return HotReloadSignatureChangeCoverage.CollectUncoveredCallersByTarget(
                hits,
                coveredIdentities);
        }

        private static HotReloadCallSiteScanner.CompiledMethodIdentity[] CollectScanTargets(
            string assemblyName,
            IReadOnlyList<TransformWorkerEntryDto> replacementEntries,
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures)
        {
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> targets =
                new List<HotReloadCallSiteScanner.CompiledMethodIdentity>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in replacementEntries)
            {
                TryAddScanTarget(
                    targets,
                    seenKeys,
                    assemblyName,
                    entry.typeMetadataName,
                    entry.methodName,
                    entry.parameterTypeFullNames,
                    entry.genericArity);
            }

            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedSignatures)
            {
                TryAddScanTarget(
                    targets,
                    seenKeys,
                    assemblyName,
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
            }

            return targets.ToArray();
        }

        private static void TryAddScanTarget(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> targets,
            HashSet<string> seenKeys,
            string assemblyName,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string methodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                typeMetadataName,
                methodName,
                parameterTypeFullNames,
                genericArity);
            if (!seenKeys.Add(methodKey))
            {
                return;
            }

            targets.Add(
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    typeMetadataName,
                    methodName,
                    parameterTypeFullNames ?? Array.Empty<string>(),
                    genericArity));
        }

        private static List<TransformWorkerEntryDto> CollectGatedReplacementEntries(
            IReadOnlyList<TransformWorkerEntryDto> replacementEntries,
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget)
        {
            List<TransformWorkerEntryDto> gated = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in replacementEntries)
            {
                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(
                        methodKey,
                        out List<HotReloadQualifiedMethodIdentity> callers)
                    && callers.Count > 0)
                {
                    gated.Add(entry);
                }
            }

            return gated;
        }

        private static List<string> CollectGatedReplacementMethodKeys(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements)
        {
            List<string> keys = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                if (!seen.Add(methodKey))
                {
                    continue;
                }

                keys.Add(methodKey);
            }

            return keys;
        }

        // Why FormatMethodLabelParts, not BuildMethodKey: registry MethodKey uses the display
        // label ('+' nested separators, '.' before the name). The wire key keeps '/' and '::'
        // and never matches Describe().
        internal static string FormatGatedReplacementRegistryKey(TransformWorkerEntryDto entry)
        {
            Debug.Assert(entry != null, "entry must not be null.");
            return HotReloadMethodKeys.FormatMethodLabelParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames ?? Array.Empty<string>(),
                entry.genericArity);
        }

        internal static List<HotReloadMethodOutcome> BuildGatedReplacementSkipOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements,
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget,
            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> editedFileMethodIdentitiesByFile,
            HotReloadGroupFilePaths groupFilePaths)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodLabel = FormatGatedReplacementRegistryKey(entry);
                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                string reason;
                // Why live registry, not a run-start snapshot: BeginFileGeneration runs after
                // this gate, so the previous apply's added members are still listed here.
                // Why the entry's own file: a group run gates the replacements of several files,
                // and a member is active per file.
                if (HotReloadAddedMemberRegistry.IsActiveMember(entry.sourceProjectRelativePath, methodLabel))
                {
                    reason = string.Format(
                        HotReloadConstants.SignatureChangedGateSkipReasonAlreadyActiveFormat,
                        methodLabel);
                }
                // Why this entry's own file: a caller edited in a sibling file of the group is
                // not a same-file caller, and telling the user otherwise points them at the
                // wrong file.
                else if (uncoveredCallersByTarget.TryGetValue(
                             methodKey,
                             out List<HotReloadQualifiedMethodIdentity> uncoveredCallers)
                    && editedFileMethodIdentitiesByFile.TryGetValue(
                        entry.sourceProjectRelativePath,
                        out HashSet<HotReloadQualifiedMethodIdentity> editedFileMethodIdentities)
                    && HotReloadSignatureChangeCoverage.AreAllUncoveredCallersInEditedFile(
                        uncoveredCallers,
                        editedFileMethodIdentities))
                {
                    reason = string.Format(
                        HotReloadConstants.SignatureChangedGateSkipReasonSameFileCallersFormat,
                        methodLabel,
                        HotReloadSignatureChangeCoverage.FormatUncoveredCallerShortNames(uncoveredCallers));
                }
                else
                {
                    reason = string.Format(
                        HotReloadConstants.SignatureChangedGateSkipReasonFormat,
                        methodLabel);
                }

                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        reason,
                        groupFilePaths.ResolveAssemblyResolvePath(entry.sourceProjectRelativePath)));
            }

            return outcomes;
        }

        internal sealed class SignatureChangeGateResult
        {
            public bool FileFailed { get; }
            public string FailureMessage { get; }
            public bool UsedWorkerRetry { get; }
            public bool DidScan { get; }
            public HotReloadShimIsolation.HotReloadShimIsolationResult Isolation { get; }
            public List<HotReloadMethodOutcome> SkippedOutcomes { get; }
            public List<string> Warnings { get; }
            public List<HotReloadCallSiteScanner.CallSiteHit> Hits { get; }
            public HashSet<HotReloadQualifiedMethodIdentity> DeletedCallerExemptions { get; }
            public List<string> GatedReplacementMethodKeys { get; }

            private SignatureChangeGateResult(
                bool fileFailed,
                string failureMessage,
                bool usedWorkerRetry,
                bool didScan,
                HotReloadShimIsolation.HotReloadShimIsolationResult isolation,
                List<HotReloadMethodOutcome> skippedOutcomes,
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                HashSet<HotReloadQualifiedMethodIdentity> deletedCallerExemptions,
                List<string> gatedReplacementMethodKeys)
            {
                FileFailed = fileFailed;
                FailureMessage = failureMessage;
                UsedWorkerRetry = usedWorkerRetry;
                DidScan = didScan;
                Isolation = isolation;
                SkippedOutcomes = skippedOutcomes ?? new List<HotReloadMethodOutcome>();
                Warnings = warnings ?? new List<string>();
                Hits = hits ?? new List<HotReloadCallSiteScanner.CallSiteHit>();
                DeletedCallerExemptions = deletedCallerExemptions
                    ?? new HashSet<HotReloadQualifiedMethodIdentity>();
                GatedReplacementMethodKeys = gatedReplacementMethodKeys ?? new List<string>();
            }

            public static SignatureChangeGateResult NoWork()
            {
                return new SignatureChangeGateResult(
                    false, null, false, false, null, null, null, null, null, null);
            }

            public static SignatureChangeGateResult WarningsOnly(
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                HashSet<HotReloadQualifiedMethodIdentity> deletedCallerExemptions)
            {
                return new SignatureChangeGateResult(
                    false, null, false, true, null, null, warnings, hits, deletedCallerExemptions, null);
            }

            public static SignatureChangeGateResult Failed(
                string failureMessage,
                List<string> gatedReplacementMethodKeys)
            {
                return new SignatureChangeGateResult(
                    true,
                    failureMessage,
                    false,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    gatedReplacementMethodKeys);
            }

            public static SignatureChangeGateResult Retried(
                HotReloadShimIsolation.HotReloadShimIsolationResult isolation,
                List<HotReloadMethodOutcome> skippedOutcomes,
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                HashSet<HotReloadQualifiedMethodIdentity> deletedCallerExemptions,
                List<string> gatedReplacementMethodKeys)
            {
                return new SignatureChangeGateResult(
                    false,
                    null,
                    true,
                    true,
                    isolation,
                    skippedOutcomes,
                    warnings,
                    hits,
                    deletedCallerExemptions,
                    gatedReplacementMethodKeys);
            }
        }
    }
}
