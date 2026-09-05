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
    /// Retries shim compile once by excluding every entry of the files a compile error was
    /// attributed to, so the remaining files of the group can still be applied.
    /// </summary>
    internal static class HotReloadShimIsolation
    {
        /// <summary>
        /// Retries the failed shim compile once, excluding every entry of the files whose compiler
        /// errors could be attributed to them, so the other files of the group can still patch.
        /// Returns null when isolation is not possible (unattributable errors, every entry failing,
        /// the retry worker run failing, or the retry compile failing) — the caller then falls back to one group-level
        /// Failed outcome per file (method-attributed when the group holds a single entry).
        /// </summary>
        internal static async Task<HotReloadShimIsolationResult> TryIsolateShimCompileFailureAsync(
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            HotReloadShimCompileResult compileResult,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            HotReloadGroupFilePaths groupFilePaths,
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
                compileResult.Errors);
            if (attribution == null
                || attribution.FailedEntries.Count == 0
                || attribution.FailedEntries.Count == workerOutput.entries.Length)
            {
                // Unattributable errors (header/binder/using-level or scaffold path): naming a
                // file would be a guess, so the caller falls back to one group-level failure.
                // Every entry failing is the same situation from the other side: excluding all
                // of them would narrow nothing, and there is no file left to save.
                return null;
            }

            List<string> groupPaths = CollectSourceProjectRelativePaths(workerInput);
            HotReloadFileAtomicIsolationPlan plan = HotReloadFileAtomicIsolationPlan.Build(
                workerOutput.entries,
                attribution,
                workerOutput.skipped,
                groupFilePaths,
                groupPaths);
            List<HotReloadMethodOutcome> failedMethodOutcomes =
                HotReloadFileAtomicIsolationPlan.CollectOutcomes(plan.FailedOutcomesByFile, groupPaths);
            IsolationExclusions exclusions = new IsolationExclusions(
                plan.ExcludedMethodKeys,
                plan.ExcludedAddedMethodKeys,
                plan.CallerEntries);
            List<HotReloadMethodOutcome> skippedCallerOutcomes = BuildSkippedCallerOutcomes(
                plan.CallerEntries,
                groupFilePaths,
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
                groupFilePaths,
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                correlationId,
                ct).ConfigureAwait(false);
            retry.Isolation?.AttachPlan(plan);
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
            HotReloadGroupFilePaths groupFilePaths,
            string trigger,
            string correlationId,
            CancellationToken ct)
        {
            TransformWorkerInputDto retryInput = new TransformWorkerInputDto
            {
                // Why share the array: the worker only reads it, and each source carries the
                // snapshotSource the retry needs — omitting it would make the retry patch
                // unedited methods again and diverge the retry entries from the first pass.
                sources = workerInput.sources,
                defines = workerInput.defines,
                referencePaths = workerInput.referencePaths,
                targetTypesAssemblyPath = workerInput.targetTypesAssemblyPath,
                excludedMethodKeys = exclusions.ExcludedMethodKeys,
                excludedAddedMethodKeys = exclusions.ExcludedAddedMethodKeys,
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
            Debug.Assert(
                retryOutput.files.Length == workerInput.sources.Length,
                "A retry worker run must return one per-file output per source.");
            // Why drop first-pass (Method, Reason) pairs: consuming them again would duplicate
            // every per-file skip. Retry-only pairs are new — typically transitive callers of
            // excluded added methods — and must surface or the edit is applied nowhere.
            List<HotReloadMethodOutcome> retryOnlySkipped = CollectRetryOnlySkippedOutcomes(
                firstPassSkipped,
                retryOutput.skipped,
                groupFilePaths,
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
                        retryOutput.files,
                        retryOutput.siblingConstDriftWarnings));
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
                CollectSourceProjectRelativePaths(workerInput),
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
                    retryOutput.files,
                    retryOutput.siblingConstDriftWarnings));
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
            HotReloadGroupFilePaths groupFilePaths,
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
                        groupFilePaths.ResolveAssemblyResolvePath(retryRow.sourceProjectRelativePath)));
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

        // The project-relative paths of every source the run transformed, for shim-diagnostic
        // line mapping.
        internal static List<string> CollectSourceProjectRelativePaths(TransformWorkerInputDto workerInput)
        {
            List<string> projectRelativePaths = new List<string>(workerInput.sources.Length);
            foreach (TransformWorkerSourceDto source in workerInput.sources)
            {
                projectRelativePaths.Add(source.projectRelativePath);
            }

            return projectRelativePaths;
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
                string methodKey = HotReloadMethodKeys.BuildMethodKey(failedEntry);
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
                failedEntryKeys.Add(HotReloadMethodKeys.BuildMethodKey(failedEntry));
            }

            List<TransformWorkerEntryDto> callers = CollectCallerEntriesOfAddedMethods(
                failedAddedMethodKeys,
                failedEntryKeys,
                allEntries);
            foreach (TransformWorkerEntryDto entry in callers)
            {
                excludedCallerEntries.Add(entry);
                string callerKey = HotReloadMethodKeys.BuildMethodKey(entry);
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

                string callerKey = HotReloadMethodKeys.BuildMethodKey(entry);
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
            HotReloadGroupFilePaths groupFilePaths,
            string skipReason)
        {
            List<HotReloadMethodOutcome> skippedCallerOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto caller in callerEntries)
            {
                string methodLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                    caller.typeMetadataName,
                    caller.methodName,
                    caller.parameterTypeFullNames ?? Array.Empty<string>(),
                    caller.genericArity);
                skippedCallerOutcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        skipReason,
                        groupFilePaths.ResolveAssemblyResolvePath(caller.sourceProjectRelativePath)));
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

            // Per-file rows of the retry worker run. Why the rows (not one added-name array): the
            // retry covers the whole group, and added fields and consts are per-file results.
            public TransformWorkerFileOutputDto[] RetryFiles { get; }

            public string[] SiblingConstDriftWarnings { get; }

            // How the failed shim compile was split across the group's files. Null when the
            // signature-change gate drove the retry: that retry isolates gated replacements,
            // not compile failures, so no file was taken down by an error.
            public HotReloadFileAtomicIsolationPlan Plan { get; private set; }

            internal void AttachPlan(HotReloadFileAtomicIsolationPlan plan)
            {
                Debug.Assert(plan != null, "plan must not be null.");
                Plan = plan;
            }

            public HotReloadShimIsolationResult(
                List<HotReloadMethodOutcome> failedMethodOutcomes,
                List<HotReloadMethodOutcome> skippedCallerOutcomes,
                TransformWorkerEntryDto[] retryEntries,
                HotReloadShimCompileResult retryCompileResult,
                TransformWorkerFileOutputDto[] retryFiles = null,
                string[] siblingConstDriftWarnings = null)
            {
                Debug.Assert(skippedCallerOutcomes != null, "skippedCallerOutcomes must not be null.");
                FailedMethodOutcomes = failedMethodOutcomes;
                SkippedCallerOutcomes = skippedCallerOutcomes;
                RetryEntries = retryEntries;
                RetryCompileResult = retryCompileResult;
                RetryFiles = retryFiles ?? Array.Empty<TransformWorkerFileOutputDto>();
                SiblingConstDriftWarnings = siblingConstDriftWarnings ?? Array.Empty<string>();
            }
        }
    }
}
