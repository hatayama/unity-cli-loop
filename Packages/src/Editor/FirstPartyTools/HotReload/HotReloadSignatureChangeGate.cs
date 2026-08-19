using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor.Compilation;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

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
            string projectRoot,
            string assemblyName,
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            string projectRelativePath,
            string correlationId,
            CancellationToken ct)
        {
            TransformWorkerEntryDto[] entries = workerOutput.entries ?? Array.Empty<TransformWorkerEntryDto>();
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures =
                workerOutput.removedMethodSignatures
                ?? Array.Empty<TransformWorkerRemovedMethodSignatureDto>();
            List<TransformWorkerEntryDto> replacementEntries = CollectReplacementEntries(entries);
            if (replacementEntries.Count == 0 && removedSignatures.Length == 0)
            {
                return SignatureChangeGateResult.NoWork();
            }

            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets = CollectScanTargets(
                assemblyName,
                replacementEntries,
                removedSignatures);
            List<HotReloadCallSiteScanner.CallSiteHit> hits =
                HotReloadCallSiteScanner.FindCallSites(projectRoot, targets);
            HashSet<string> coveredKeys = HotReloadSignatureChangeCoverage.CollectCoveredMethodKeys(entries, targets);
            Dictionary<string, List<string>> uncoveredCallersByTarget =
                HotReloadSignatureChangeCoverage.CollectUncoveredCallersByTarget(hits, coveredKeys);

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
                    HotReloadSignatureChangeCoverage.CollectScanTargetKeys(targets));
            }

            HotReloadShimIsolation.IsolationExclusions exclusions = HotReloadShimIsolation.BuildIsolationExclusions(gatedReplacements, entries);
            HashSet<string> editedFileMethodKeys = HotReloadSignatureChangeCoverage.CollectEditedFileMethodKeys(
                entries,
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>());
            List<HotReloadMethodOutcome> skippedOutcomes = BuildGatedReplacementSkipOutcomes(
                gatedReplacements,
                uncoveredCallersByTarget,
                editedFileMethodKeys,
                assemblyResolvePath,
                projectRelativePath);
            skippedOutcomes.AddRange(
                HotReloadShimIsolation.BuildSkippedCallerOutcomes(
                    exclusions.CallerEntries,
                    assemblyResolvePath,
                    HotReloadConstants.SignatureChangedGatedCallerSkipReason));

            HotReloadShimIsolation.IsolationRetryRunResult retry = await HotReloadShimIsolation.RunIsolationRetryAsync(
                workerInput,
                exclusions,
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadMethodOutcome>(),
                compilationAssembly,
                targetDllPath,
                defines,
                workerOutput.skipped,
                assemblyResolvePath,
                HotReloadConstants.VibeLogIsolationTriggerSignatureChangeGate,
                correlationId,
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
                HotReloadSignatureChangeCoverage.CollectScanTargetKeys(targets),
                gatedReplacementMethodKeys);
        }

        internal static List<TransformWorkerEntryDto> CollectReplacementEntries(
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

        internal static HotReloadCallSiteScanner.CompiledMethodIdentity[] CollectScanTargets(
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

        internal static void TryAddScanTarget(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> targets,
            HashSet<string> seenKeys,
            string assemblyName,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string methodKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
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

        internal static List<TransformWorkerEntryDto> CollectGatedReplacementEntries(
            IReadOnlyList<TransformWorkerEntryDto> replacementEntries,
            Dictionary<string, List<string>> uncoveredCallersByTarget)
        {
            List<TransformWorkerEntryDto> gated = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in replacementEntries)
            {
                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    && callers.Count > 0)
                {
                    gated.Add(entry);
                }
            }

            return gated;
        }

        internal static List<string> CollectGatedReplacementMethodKeys(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements)
        {
            List<string> keys = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (!seen.Add(methodKey))
                {
                    continue;
                }

                keys.Add(methodKey);
            }

            return keys;
        }

        // Why FormatMethodKeyParts, not BuildMethodKey: registry MethodKey uses the display
        // label ('+' nested separators, '.' before the name). The wire key keeps '/' and '::'
        // and never matches Describe().
        internal static string FormatGatedReplacementRegistryKey(TransformWorkerEntryDto entry)
        {
            Debug.Assert(entry != null, "entry must not be null.");
            return HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames ?? Array.Empty<string>(),
                entry.genericArity);
        }

        internal static List<HotReloadMethodOutcome> BuildGatedReplacementSkipOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements,
            Dictionary<string, List<string>> uncoveredCallersByTarget,
            HashSet<string> editedFileMethodKeys,
            string assemblyResolvePath,
            string projectRelativePath)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodLabel = FormatGatedReplacementRegistryKey(entry);
                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                string reason;
                // Why live registry, not a run-start snapshot: BeginFileGeneration runs after
                // this gate, so the previous apply's added members are still listed here.
                if (HotReloadAddedMemberRegistry.IsActiveMember(projectRelativePath, methodLabel))
                {
                    reason = string.Format(
                        HotReloadConstants.SignatureChangedGateSkipReasonAlreadyActiveFormat,
                        methodLabel);
                }
                else if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> uncoveredCallers)
                    && HotReloadSignatureChangeCoverage.AreAllUncoveredCallersInEditedFile(uncoveredCallers, editedFileMethodKeys))
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
                        assemblyResolvePath));
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
            public List<string> ScanTargetKeys { get; }
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
                List<string> scanTargetKeys,
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
                ScanTargetKeys = scanTargetKeys ?? new List<string>();
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
                List<string> scanTargetKeys)
            {
                return new SignatureChangeGateResult(
                    false, null, false, true, null, null, warnings, hits, scanTargetKeys, null);
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
                List<string> scanTargetKeys,
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
                    scanTargetKeys,
                    gatedReplacementMethodKeys);
            }
        }
    }
}
