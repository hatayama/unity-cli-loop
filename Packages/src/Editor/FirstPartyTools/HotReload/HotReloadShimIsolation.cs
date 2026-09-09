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
            TransformWorkerClient transformWorkerClient,
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

            HotReloadIsolationOutcomeBuilder outcomeBuilder = new HotReloadIsolationOutcomeBuilder();
            List<string> groupPaths = outcomeBuilder.CollectSourceProjectRelativePaths(workerInput);
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
            List<HotReloadMethodOutcome> skippedCallerOutcomes = outcomeBuilder.BuildSkippedCallerOutcomes(
                plan.CallerEntries,
                groupFilePaths,
                HotReloadConstants.IsolatedAddedMethodCallerSkipReason);

            HotReloadIsolationRetryContext retryContext = new HotReloadIsolationRetryContext(
                workerInput,
                compilationAssembly,
                targetDllPath,
                defines,
                workerOutput.skipped,
                groupFilePaths,
                correlationId);
            IsolationRetryRunResult retry = await RunIsolationRetryAsync(
                transformWorkerClient,
                retryContext,
                exclusions,
                failedMethodOutcomes,
                skippedCallerOutcomes,
                new HotReloadShimCompileFailureIsolationTrigger(),
                ct).ConfigureAwait(false);
            retry.Isolation?.AttachPlan(plan);
            return retry.Isolation;
        }

        internal static async Task<IsolationRetryRunResult> RunIsolationRetryAsync(
            TransformWorkerClient transformWorkerClient,
            HotReloadIsolationRetryContext context,
            IsolationExclusions exclusions,
            List<HotReloadMethodOutcome> failedMethodOutcomes,
            List<HotReloadMethodOutcome> skippedCallerOutcomes,
            IHotReloadIsolationTrigger trigger,
            CancellationToken ct)
        {
            TransformWorkerInputDto workerInput = context.WorkerInput;
            TransformWorkerInputDto retryInput = new TransformWorkerInputDto
            {
                // Why share the array: the worker only reads it, and each source carries the
                // snapshotSource the retry needs — omitting it would make the retry patch
                // unedited methods again and diverge the retry entries from the first pass.
                sources = workerInput.sources,
                defines = workerInput.defines,
                referencePaths = workerInput.referencePaths,
                targetTypesAssemblyPath = workerInput.targetTypesAssemblyPath,
                // Why copy the identity and the records, and why never the operation: the retry is
                // still a transform, and it recomputes each retained declaration's fingerprint
                // through the artifact mapping. Without them the retry binds the retained types
                // back to their source and emits types a loaded assembly already holds.
                targetAssemblyName = workerInput.targetAssemblyName,
                targetAssemblyMvid = workerInput.targetAssemblyMvid,
                introducedTypeArtifacts = workerInput.introducedTypeArtifacts,
                excludedMethodKeys = exclusions.ExcludedMethodKeys,
                excludedAddedMethodKeys = exclusions.ExcludedAddedMethodKeys,
                assemblySourcePaths = workerInput.assemblySourcePaths,
                // Why copy: retry must still scan the same snapshot-mismatched siblings so
                // siblingConstDriftWarnings stay populated on the retry worker output.
                changedSiblingSourcePaths = workerInput.changedSiblingSourcePaths
            };

            TransformWorkerClientResult retryWorkerResult =
                await transformWorkerClient.RunAsync(retryInput, ct).ConfigureAwait(false);
            if (!retryWorkerResult.Success)
            {
                HotReloadOrchestratorLog.LogHotReloadIsolationRetry(
                    exclusions.ExcludedMethodKeys.Length,
                    exclusions.ExcludedAddedMethodKeys.Length,
                    0,
                    0,
                    0,
                    false,
                    trigger.LogName,
                    context.CorrelationId);
                return IsolationRetryRunResult.Failed(
                    "Retry worker failed: " + retryWorkerResult.ErrorMessage);
            }

            HotReloadIsolationOutcomeBuilder outcomeBuilder = new HotReloadIsolationOutcomeBuilder();
            TransformWorkerOutputDto retryOutput = retryWorkerResult.Output;
            Debug.Assert(
                retryOutput.files.Length == workerInput.sources.Length,
                "A retry worker run must return one per-file output per source.");
            // Why drop first-pass (Method, Reason) pairs: consuming them again would duplicate
            // every per-file skip. Retry-only pairs are new — typically transitive callers of
            // excluded added methods — and must surface or the edit is applied nowhere.
            List<HotReloadMethodOutcome> retryOnlySkipped = outcomeBuilder.CollectRetryOnlySkippedOutcomes(
                context.FirstPassSkipped,
                retryOutput.skipped,
                context.GroupFilePaths,
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
                trigger.LogName,
                context.CorrelationId);
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
                context.CompilationAssembly,
                context.TargetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference,
                workerInput.introducedTypeArtifacts);
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
                context.Defines,
                outcomeBuilder.CollectSourceProjectRelativePaths(workerInput),
                ct).ConfigureAwait(false);
            if (!retryCompileResult.Success)
            {
                HotReloadOrchestratorLog.LogHotReloadShimCompileFailed(
                    retryCompileResult,
                    HotReloadConstants.VibeLogShimCompileStageRetry,
                    context.CorrelationId);
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
