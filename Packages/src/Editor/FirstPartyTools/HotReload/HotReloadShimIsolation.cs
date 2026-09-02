using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Retries shim compile once by excluding attributed failures and their added-method callers.
    /// </summary>
    internal static class HotReloadShimIsolation
    {
        /// <summary>
        /// Retries the failed shim compile once, excluding the method(s) whose compiler errors can
        /// be attributed to them, so the rest of the file's methods can still patch. Returns null
        /// when isolation is not possible (unattributable errors, all/none of the entries failing,
        /// the retry worker run failing, or the retry compile failing) — the caller then falls back
        /// to a single Failed outcome (method-attributed when only one entry remains).
        /// </summary>
        internal static async Task<HotReloadShimIsolationResult> TryIsolateShimCompileFailureAsync(
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            HotReloadShimCompileResult compileResult,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            string correlationId,
            CancellationToken ct)
        {
            if (compileResult.Errors.Count == 0)
            {
                return null;
            }

            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution =
                HotReloadShimErrorAttribution.AttributeErrorsToEntries(
                workerOutput.entries,
                compileResult.Errors,
                workerInput.projectRelativePath);
            if (attribution == null
                || attribution.FailedEntries.Count == 0
                || attribution.FailedEntries.Count == workerOutput.entries.Length)
            {
                // Unattributable errors (header/binder/using-level / scaffold path), or isolating
                // everyone / no one would not narrow the failure at all.
                return null;
            }

            List<HotReloadMethodOutcome> failedMethodOutcomes =
                BuildFailedMethodOutcomes(attribution, assemblyResolvePath, workerOutput.skipped);
            IsolationExclusions exclusions = BuildIsolationExclusions(
                attribution.FailedEntries,
                workerOutput.entries);
            List<HotReloadMethodOutcome> skippedCallerOutcomes = BuildSkippedCallerOutcomes(
                exclusions.CallerEntries,
                assemblyResolvePath,
                HotReloadConstants.IsolatedAddedMethodCallerSkipReason);

            IsolationRetryRunResult retry = await RunIsolationRetryAsync(
                workerInput,
                exclusions,
                failedMethodOutcomes,
                skippedCallerOutcomes,
                compilationAssembly,
                targetDllPath,
                defines,
                workerOutput.skipped,
                assemblyResolvePath,
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                correlationId,
                ct).ConfigureAwait(false);
            return retry.Isolation;
        }

        internal static async Task<IsolationRetryRunResult> RunIsolationRetryAsync(
            TransformWorkerInputDto workerInput,
            IsolationExclusions exclusions,
            List<HotReloadMethodOutcome> failedMethodOutcomes,
            List<HotReloadMethodOutcome> skippedCallerOutcomes,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            TransformWorkerSkippedDto[] firstPassSkipped,
            string assemblyResolvePath,
            string trigger,
            string correlationId,
            CancellationToken ct)
        {
            TransformWorkerInputDto retryInput = new TransformWorkerInputDto
            {
                sourcePath = workerInput.sourcePath,
                defines = workerInput.defines,
                referencePaths = workerInput.referencePaths,
                targetTypesAssemblyPath = workerInput.targetTypesAssemblyPath,
                excludedMethodKeys = exclusions.ExcludedMethodKeys,
                excludedAddedMethodKeys = exclusions.ExcludedAddedMethodKeys,
                // Why copy: omitting snapshotSource would make the retry patch unedited methods
                // again and diverge the retry entries set from the first-pass isolation baseline.
                snapshotSource = workerInput.snapshotSource,
                projectRelativePath = workerInput.projectRelativePath,
                assemblySourcePaths = workerInput.assemblySourcePaths,
                // Why copy: retry must still scan the same snapshot-mismatched siblings so
                // siblingConstDriftWarnings stay populated on the retry worker output.
                changedSiblingSourcePaths = workerInput.changedSiblingSourcePaths
            };

            TransformWorkerClientResult retryWorkerResult =
                await TransformWorkerClient.RunAsync(retryInput, ct).ConfigureAwait(false);
            if (!retryWorkerResult.Success)
            {
                HotReloadOrchestratorLog.LogHotReloadIsolationRetry(
                    exclusions.ExcludedMethodKeys.Length,
                    exclusions.ExcludedAddedMethodKeys.Length,
                    0,
                    0,
                    0,
                    false,
                    trigger,
                    correlationId);
                return IsolationRetryRunResult.Failed(
                    "Retry worker failed: " + retryWorkerResult.ErrorMessage);
            }

            TransformWorkerOutputDto retryOutput = retryWorkerResult.Output;
            // Why drop first-pass (Method, Reason) pairs: consuming them again would duplicate
            // every per-file skip. Retry-only pairs are new — typically transitive callers of
            // excluded added methods — and must surface or the edit is applied nowhere.
            List<HotReloadMethodOutcome> retryOnlySkipped = CollectRetryOnlySkippedOutcomes(
                firstPassSkipped,
                retryOutput.skipped,
                assemblyResolvePath,
                trigger,
                exclusions.ExcludedAddedMethodKeys);
            skippedCallerOutcomes.AddRange(retryOnlySkipped);
            HotReloadOrchestratorLog.LogHotReloadIsolationRetry(
                exclusions.ExcludedMethodKeys.Length,
                exclusions.ExcludedAddedMethodKeys.Length,
                retryOutput.entries?.Length ?? 0,
                retryOutput.skipped?.Length ?? 0,
                retryOnlySkipped.Count,
                true,
                trigger,
                correlationId);
            if (string.IsNullOrEmpty(retryOutput.shimSource) || retryOutput.entries.Length == 0)
            {
                return IsolationRetryRunResult.Succeeded(
                    new HotReloadShimIsolationResult(
                        failedMethodOutcomes,
                        skippedCallerOutcomes,
                        Array.Empty<TransformWorkerEntryDto>(),
                        null,
                        retryOutput.addedFieldNames,
                        retryOutput.siblingConstDriftWarnings,
                        retryOutput.addedConstNames));
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = HotReloadShimReferenceBuilder.NeedsHarmonyReference(retryOutput);
            bool includeAddedFieldStoreReference = HotReloadShimReferenceBuilder.NeedsAddedFieldStoreReference(retryOutput);
            HotReloadShimReferenceBuilder.ShimReferencePathsResult shimReferencePaths = HotReloadShimReferenceBuilder.TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                // First-pass publicize already succeeded, so a miss here is rare; abandon
                // isolation the same way as a retry compile failure.
                return IsolationRetryRunResult.Failed(
                    "Retry could not build shim references: " + shimReferencePaths.ErrorMessage);
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult retryCompileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                retryOutput.shimSource,
                shimReferences,
                defines,
                workerInput.projectRelativePath,
                ct).ConfigureAwait(false);
            if (!retryCompileResult.Success)
            {
                HotReloadOrchestratorLog.LogHotReloadShimCompileFailed(
                    retryCompileResult,
                    HotReloadConstants.VibeLogShimCompileStageRetry,
                    correlationId);
                return IsolationRetryRunResult.Failed(
                    "Retry shim compile failed: " + retryCompileResult.ErrorMessage);
            }

            return IsolationRetryRunResult.Succeeded(
                new HotReloadShimIsolationResult(
                    failedMethodOutcomes,
                    skippedCallerOutcomes,
                    retryOutput.entries,
                    retryCompileResult,
                    retryOutput.addedFieldNames,
                    retryOutput.siblingConstDriftWarnings,
                    retryOutput.addedConstNames));
        }

        /// <summary>
        /// Converts retry-worker skips that are not already in the first-pass skipped list into
        /// outcomes. Match is (Method, Reason) Ordinal equality so a method skipped for a new
        /// reason on retry still surfaces. Why rewrite only on shim-compile-failure isolation:
        /// signature-change-gate retry must keep UnavailableAddedCall for indirect callers.
        /// </summary>
        internal static List<HotReloadMethodOutcome> CollectRetryOnlySkippedOutcomes(
            TransformWorkerSkippedDto[] firstPassSkipped,
            TransformWorkerSkippedDto[] retrySkipped,
            string assemblyResolvePath,
            string trigger,
            IReadOnlyCollection<string> excludedAddedMethodKeys)
        {
            List<HotReloadMethodOutcome> retryOnly = new List<HotReloadMethodOutcome>();
            if (retrySkipped == null)
            {
                return retryOnly;
            }

            TransformWorkerSkippedDto[] baseline =
                firstPassSkipped ?? Array.Empty<TransformWorkerSkippedDto>();
            List<TransformWorkerSkippedDto> retryOnlyRows = new List<TransformWorkerSkippedDto>();
            foreach (TransformWorkerSkippedDto retryRow in retrySkipped)
            {
                if (FirstPassContainsSkippedPair(baseline, retryRow))
                {
                    continue;
                }

                retryOnlyRows.Add(retryRow);
            }

            if (string.Equals(
                trigger,
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                StringComparison.Ordinal))
            {
                RewriteShimCompileFailureIndirectCallerReasons(retryOnlyRows, excludedAddedMethodKeys);
            }

            foreach (TransformWorkerSkippedDto retryRow in retryOnlyRows)
            {
                retryOnly.Add(
                    HotReloadMethodOutcome.Skipped(
                        retryRow.method ?? "(unknown)",
                        retryRow.reason ?? string.Empty,
                        assemblyResolvePath));
            }

            return retryOnly;
        }

        private static void RewriteShimCompileFailureIndirectCallerReasons(
            List<TransformWorkerSkippedDto> retryOnlyRows,
            IReadOnlyCollection<string> excludedAddedMethodKeys)
        {
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            if (excludedAddedMethodKeys != null)
            {
                foreach (string key in excludedAddedMethodKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        reachable.Add(key);
                    }
                }
            }

            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (TransformWorkerSkippedDto row in retryOnlyRows)
                {
                    if (!string.Equals(
                        row.reason,
                        HotReloadConstants.UnavailableAddedCallSkipReason,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(row.calledAddedMethodKey)
                        || !reachable.Contains(row.calledAddedMethodKey))
                    {
                        continue;
                    }

                    row.reason = HotReloadConstants.IsolatedAddedMethodCallerSkipReason;
                    progressed = true;
                    if (!string.IsNullOrEmpty(row.methodKey))
                    {
                        reachable.Add(row.methodKey);
                    }
                }
            }
        }

        private static bool FirstPassContainsSkippedPair(
            TransformWorkerSkippedDto[] firstPassSkipped,
            TransformWorkerSkippedDto retryRow)
        {
            string retryMethod = retryRow.method ?? string.Empty;
            string retryReason = retryRow.reason ?? string.Empty;
            foreach (TransformWorkerSkippedDto firstPassRow in firstPassSkipped)
            {
                string firstMethod = firstPassRow.method ?? string.Empty;
                string firstReason = firstPassRow.reason ?? string.Empty;
                if (string.Equals(firstMethod, retryMethod, StringComparison.Ordinal)
                    && string.Equals(firstReason, retryReason, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<HotReloadMethodOutcome> BuildFailedMethodOutcomes(
            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution,
            string assemblyResolvePath,
            TransformWorkerSkippedDto[] skipped)
        {
            List<HotReloadMethodOutcome> failedMethodOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto failedEntry in attribution.FailedEntries)
            {
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    failedEntry.typeMetadataName,
                    failedEntry.methodName,
                    failedEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                    failedEntry.genericArity);
                List<string> entryErrorMessages = attribution.ErrorMessagesByEntry[failedEntry];
                string composedMessage = HotReloadShimCompiler.ComposeShimCompileFailureMessage(entryErrorMessages);
                failedMethodOutcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        methodLabel,
                        HotReloadSkippedMemberCompileNote.AppendNotes(
                            composedMessage,
                            entryErrorMessages,
                            skipped),
                        assemblyResolvePath));
            }

            return failedMethodOutcomes;
        }

        internal static IsolationExclusions BuildIsolationExclusions(
            IReadOnlyList<TransformWorkerEntryDto> failedEntries,
            TransformWorkerEntryDto[] allEntries)
        {
            HashSet<string> excludedKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> failedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            List<TransformWorkerEntryDto> excludedCallerEntries = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(failedEntry);
                if (failedEntry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    // Why a separate set: dropping a healthy added shim via excludedMethodKeys
                    // leaves remaining callers with CS0103 (G1). A broken added body must still
                    // be excluded together with its callers so retry does not re-emit it.
                    failedAddedMethodKeys.Add(methodKey);
                    excludedAddedMethodKeys.Add(methodKey);
                    continue;
                }

                excludedKeys.Add(methodKey);
            }

            HashSet<string> failedEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                failedEntryKeys.Add(HotReloadWireMethodKeys.BuildMethodKey(failedEntry));
            }

            List<TransformWorkerEntryDto> callers = CollectCallerEntriesOfAddedMethods(
                failedAddedMethodKeys,
                failedEntryKeys,
                allEntries);
            foreach (TransformWorkerEntryDto entry in callers)
            {
                excludedCallerEntries.Add(entry);
                string callerKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    excludedAddedMethodKeys.Add(callerKey);
                }
                else
                {
                    excludedKeys.Add(callerKey);
                }
            }

            string[] excludedMethodKeys = new string[excludedKeys.Count];
            excludedKeys.CopyTo(excludedMethodKeys);
            string[] excludedAddedKeys = new string[excludedAddedMethodKeys.Count];
            excludedAddedMethodKeys.CopyTo(excludedAddedKeys);
            return new IsolationExclusions(
                excludedMethodKeys,
                excludedAddedKeys,
                excludedCallerEntries);
        }

        private static List<TransformWorkerEntryDto> CollectCallerEntriesOfAddedMethods(
            HashSet<string> addedMethodKeys,
            HashSet<string> alreadyExcludedEntryKeys,
            TransformWorkerEntryDto[] allEntries)
        {
            List<TransformWorkerEntryDto> callerEntries = new List<TransformWorkerEntryDto>();
            if (addedMethodKeys.Count == 0 || allEntries == null)
            {
                return callerEntries;
            }

            foreach (TransformWorkerEntryDto entry in allEntries)
            {
                if (entry.calledAddedMethodKeys == null)
                {
                    continue;
                }

                string callerKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (alreadyExcludedEntryKeys.Contains(callerKey))
                {
                    continue;
                }

                bool callsAdded = false;
                foreach (string calledKey in entry.calledAddedMethodKeys)
                {
                    if (addedMethodKeys.Contains(calledKey))
                    {
                        callsAdded = true;
                        break;
                    }
                }

                if (!callsAdded)
                {
                    continue;
                }

                callerEntries.Add(entry);
            }

            return callerEntries;
        }

        internal static List<HotReloadMethodOutcome> BuildSkippedCallerOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> callerEntries,
            string assemblyResolvePath,
            string skipReason)
        {
            List<HotReloadMethodOutcome> skippedCallerOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto caller in callerEntries)
            {
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    caller.typeMetadataName,
                    caller.methodName,
                    caller.parameterTypeFullNames ?? Array.Empty<string>(),
                    caller.genericArity);
                skippedCallerOutcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        skipReason,
                        assemblyResolvePath));
            }

            return skippedCallerOutcomes;
        }

        internal sealed class IsolationExclusions
        {
            public string[] ExcludedMethodKeys { get; }
            public string[] ExcludedAddedMethodKeys { get; }
            public IReadOnlyList<TransformWorkerEntryDto> CallerEntries { get; }

            public IsolationExclusions(
                string[] excludedMethodKeys,
                string[] excludedAddedMethodKeys,
                IReadOnlyList<TransformWorkerEntryDto> callerEntries)
            {
                ExcludedMethodKeys = excludedMethodKeys;
                ExcludedAddedMethodKeys = excludedAddedMethodKeys;
                CallerEntries = callerEntries;
            }
        }

        internal sealed class IsolationRetryRunResult
        {
            public HotReloadShimIsolationResult Isolation { get; }
            public string FailureMessage { get; }

            private IsolationRetryRunResult(HotReloadShimIsolationResult isolation, string failureMessage)
            {
                Isolation = isolation;
                FailureMessage = failureMessage;
            }

            public static IsolationRetryRunResult Succeeded(HotReloadShimIsolationResult isolation)
            {
                return new IsolationRetryRunResult(isolation, null);
            }

            public static IsolationRetryRunResult Failed(string failureMessage)
            {
                return new IsolationRetryRunResult(null, failureMessage);
            }
        }

        /// <summary>
        /// Outcome of <see cref="TryIsolateShimCompileFailureAsync"/>. <see cref="RetryEntries"/>
        /// empty means the retry worker run produced nothing to patch (still a valid, non-null
        /// isolation — only <see cref="FailedMethodOutcomes"/> apply).
        /// </summary>
        internal sealed class HotReloadShimIsolationResult
        {
            public List<HotReloadMethodOutcome> FailedMethodOutcomes { get; }
            public List<HotReloadMethodOutcome> SkippedCallerOutcomes { get; }
            public TransformWorkerEntryDto[] RetryEntries { get; }
            public HotReloadShimCompileResult RetryCompileResult { get; }
            public string[] AddedFieldNames { get; }
            public string[] AddedConstNames { get; }
            public string[] SiblingConstDriftWarnings { get; }

            public HotReloadShimIsolationResult(
                List<HotReloadMethodOutcome> failedMethodOutcomes,
                List<HotReloadMethodOutcome> skippedCallerOutcomes,
                TransformWorkerEntryDto[] retryEntries,
                HotReloadShimCompileResult retryCompileResult,
                string[] addedFieldNames = null,
                string[] siblingConstDriftWarnings = null,
                string[] addedConstNames = null)
            {
                Debug.Assert(skippedCallerOutcomes != null, "skippedCallerOutcomes must not be null.");
                FailedMethodOutcomes = failedMethodOutcomes;
                SkippedCallerOutcomes = skippedCallerOutcomes;
                RetryEntries = retryEntries;
                RetryCompileResult = retryCompileResult;
                AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
                SiblingConstDriftWarnings = siblingConstDriftWarnings ?? Array.Empty<string>();
                AddedConstNames = addedConstNames ?? Array.Empty<string>();
            }
        }
    }
}
