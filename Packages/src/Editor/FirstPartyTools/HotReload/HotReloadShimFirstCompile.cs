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
    /// Selects entries to patch: empty-entries clear, first shim compile, and compile-failure isolation.
    /// </summary>
    internal static class HotReloadShimFirstCompile
    {
        // Why a helper: the gate-retry / empty-entries / first-pass compile fork is one
        // entries-to-patch stage and kept ProcessFileAsync over CA1502.
        internal static async Task<(
            HotReloadFileProcessResult EarlyResult,
            TransformWorkerEntryDto[] EntriesToPatch,
            HotReloadShimCompileResult CompileResult,
            string[] AddedFieldNames,
            string[] AddedConstNames)> ResolveEntriesToPatchAsync(
            HotReloadApplyContext context,
            HotReloadFileSinks sinks,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            string[] addedFieldNames,
            int unchangedMethodCount,
            int revertedUnchangedCount,
            CancellationToken ct)
        {
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(sinks != null, "sinks must not be null.");
            string[] addedConstNames = context.FileOutput.addedConstNames;
            if (gateResult.UsedWorkerRetry)
            {
                addedFieldNames = gateResult.Isolation.AddedFieldNames;
                addedConstNames = gateResult.Isolation.AddedConstNames;
                if (gateResult.Isolation.RetryEntries.Length == 0)
                {
                    return (
                        new HotReloadFileProcessResult(
                            sinks.Outcomes,
                            sinks.Warnings,
                            0,
                            sinks.SuppressedPausePointIds,
                            new List<string>(),
                            unchangedMethodCount,
                            sinks.RetargetedPausePointIds,
                            addedFieldNames: null,
                            sourceContentSha256: context.FileOutput.sourceContentSha256,
                            revertedUnchangedCount: revertedUnchangedCount),
                        null,
                        null,
                        addedFieldNames,
                        addedConstNames);
                }

                return (
                    null,
                    gateResult.Isolation.RetryEntries,
                    gateResult.Isolation.RetryCompileResult,
                    addedFieldNames,
                    addedConstNames);
            }

            if (string.IsNullOrEmpty(context.WorkerOutput.shimSource)
                || context.WorkerOutput.entries == null
                || context.WorkerOutput.entries.Length == 0)
            {
                // Why only on this success path: deleting an added method and restoring callers
                // yields empty entries, so the post-shim-compile BeginFileGeneration never runs.
                // Worker failure and shim-compile failure return earlier or later without
                // clearing — same as leaving existing Harmony patches in place when apply does
                // not succeed.
                IReadOnlyList<string> addedLabelsAtClear =
                    HotReloadFileGenerations.ListActiveAddedMethodKeys(context.ProjectRelativePath);
                HotReloadOrchestratorLog.LogHotReloadEmptyEntriesClear(addedLabelsAtClear, context.CorrelationId);
                // Why not HotReloadFileGenerations.BeginFileGeneration: there is no shim assembly
                // on this path — entries are empty, so nothing was compiled. Only the added-member
                // side has stale rows to drop; starting a shim generation would need bytes that
                // do not exist.
                HotReloadAddedMemberRegistry.BeginFileGeneration(context.ProjectRelativePath);
                HotReloadEntryApplier.CommitAddedFieldsForFile(
                    context.ProjectRelativePath,
                    context.FileOutput.addedFieldNames);
                // Why after the clear: a still-declared added method can be worker-skipped
                // (virtual/generic), leaving entries empty while the registry drop is real.
                HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                    sinks.Warnings,
                    context.SnapshotLabels,
                    context.SnapshotAddedLabels,
                    context.ProjectRelativePath,
                    context.WorkerOutput,
                    sinks.Outcomes);
                return (
                    new HotReloadFileProcessResult(
                        sinks.Outcomes,
                        sinks.Warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: context.FileOutput.sourceContentSha256,
                        revertedUnchangedCount: revertedUnchangedCount),
                    null,
                    null,
                    addedFieldNames,
                    addedConstNames);
            }

            ShimFirstCompileResult firstCompile = await CompileShimFirstPassAsync(
                context.WorkerInput,
                context.WorkerOutput,
                context.CompilationAssembly,
                context.TargetDllPath,
                context.Defines,
                context.AssemblyResolvePath,
                context.CorrelationId,
                sinks.SiblingDerivedWarnings,
                ct).ConfigureAwait(false);
            if (firstCompile.AddedFieldNames != null)
            {
                addedFieldNames = firstCompile.AddedFieldNames;
            }

            if (firstCompile.FileFailed)
            {
                sinks.Outcomes.AddRange(firstCompile.Outcomes);
                return (
                    new HotReloadFileProcessResult(
                        sinks.Outcomes,
                        sinks.Warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: context.FileOutput.sourceContentSha256,
                        revertedUnchangedCount: revertedUnchangedCount),
                    null,
                    null,
                    addedFieldNames,
                    addedConstNames);
            }

            sinks.Outcomes.AddRange(firstCompile.Outcomes);
            if (firstCompile.EntriesToPatch.Length == 0)
            {
                return (
                    new HotReloadFileProcessResult(
                        sinks.Outcomes,
                        sinks.Warnings,
                        0,
                        sinks.SuppressedPausePointIds,
                        new List<string>(),
                        unchangedMethodCount,
                        sinks.RetargetedPausePointIds,
                        addedFieldNames: null,
                        sourceContentSha256: context.FileOutput.sourceContentSha256,
                        revertedUnchangedCount: revertedUnchangedCount),
                    null,
                    null,
                    addedFieldNames,
                    addedConstNames);
            }

            return (null, firstCompile.EntriesToPatch, firstCompile.CompileResult, addedFieldNames, addedConstNames);
        }

        /// <summary>
        /// First shim compile plus optional compile-failure isolation. Signature-change gate
        /// retries never call this — they already consumed the one worker retry.
        /// </summary>
        private static async Task<ShimFirstCompileResult> CompileShimFirstPassAsync(
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            string correlationId,
            List<string> siblingDerivedWarnings,
            CancellationToken ct)
        {
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");
            // BuildShimReferencePaths reads Application.dataPath / platform; stay on main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = HotReloadShimReferenceBuilder.NeedsHarmonyReference(workerOutput);
            bool includeAddedFieldStoreReference = HotReloadShimReferenceBuilder.NeedsAddedFieldStoreReference(workerOutput);
            HotReloadShimReferenceBuilder.ShimReferencePathsResult shimReferencePaths = HotReloadShimReferenceBuilder.TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                return ShimFirstCompileResult.Failed(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Failed(
                            "(file)",
                            shimReferencePaths.ErrorMessage,
                            assemblyResolvePath)
                    });
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                workerInput.sources[0].projectRelativePath,
                ct).ConfigureAwait(false);

            TransformWorkerEntryDto[] entriesToPatch = workerOutput.entries;
            if (compileResult.Success)
            {
                return ShimFirstCompileResult.Succeeded(entriesToPatch, compileResult);
            }

            HotReloadOrchestratorLog.LogHotReloadShimCompileFailed(
                compileResult,
                HotReloadConstants.VibeLogShimCompileStageFirstPass,
                correlationId);

            // Why isolate only here: a signature-change gate retry already used
            // RunIsolationRetryAsync (worker run #2). Calling isolation after that would be a
            // third worker run. Gate retry compile failures return Failed from the gate and
            // never reach this first-compile path.
            HotReloadShimIsolation.HotReloadShimIsolationResult isolation = await HotReloadShimIsolation.TryIsolateShimCompileFailureAsync(
                workerInput,
                workerOutput,
                compileResult,
                compilationAssembly,
                targetDllPath,
                defines,
                assemblyResolvePath,
                correlationId,
                ct).ConfigureAwait(false);
            if (isolation == null)
            {
                // Why: CompileAndLoadAsync appends "(line N)" only for diagnostics whose
                // #line-mapped file matches projectRelativePath — scaffold-only errors stay
                // bare. Single-entry failures skip isolation and always take this path.
                // Why attribute single-entry failures: "(shim-compile)" hides which method
                // body failed when the agent edited only one method.
                string failureMethodLabel = "(shim-compile)";
                if (entriesToPatch.Length == 1)
                {
                    TransformWorkerEntryDto soleEntry = entriesToPatch[0];
                    failureMethodLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                        soleEntry.typeMetadataName,
                        soleEntry.methodName,
                        soleEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                        soleEntry.genericArity);
                }

                List<string> fallbackErrorMessages = new List<string>(compileResult.Errors.Count);
                for (int errorIndex = 0; errorIndex < compileResult.Errors.Count; errorIndex++)
                {
                    fallbackErrorMessages.Add(compileResult.Errors[errorIndex].Message);
                }

                return ShimFirstCompileResult.Failed(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Failed(
                            failureMethodLabel,
                            HotReloadSkippedMemberCompileNote.AppendNotes(
                                compileResult.ErrorMessage,
                                fallbackErrorMessages,
                                workerOutput.skipped),
                            assemblyResolvePath)
                    });
            }

            siblingDerivedWarnings.AddRange(isolation.SiblingConstDriftWarnings);
            List<HotReloadMethodOutcome> isolationOutcomes = new List<HotReloadMethodOutcome>();
            isolationOutcomes.AddRange(isolation.FailedMethodOutcomes);
            isolationOutcomes.AddRange(isolation.SkippedCallerOutcomes);
            AppendAtomicFileSkipOutcomes(
                isolationOutcomes,
                isolation.RetryEntries,
                assemblyResolvePath);
            return ShimFirstCompileResult.Failed(isolationOutcomes);
        }

        // Why survivors stay Skipped: applying them after a sibling Failed would leave
        // the file half-applied. Isolation still attributes the Failed methods.
        private static void AppendAtomicFileSkipOutcomes(
            List<HotReloadMethodOutcome> outcomes,
            TransformWorkerEntryDto[] retryEntries,
            string filePath)
        {
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(retryEntries != null, "retryEntries must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");

            foreach (TransformWorkerEntryDto entry in retryEntries)
            {
                string methodLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                    entry.typeMetadataName,
                    entry.methodName,
                    entry.parameterTypeFullNames ?? Array.Empty<string>(),
                    entry.genericArity);
                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        HotReloadConstants.AtomicFileSkipReason,
                        filePath));
            }
        }

        private sealed class ShimFirstCompileResult
        {
            public bool FileFailed { get; }
            public List<HotReloadMethodOutcome> Outcomes { get; }
            public TransformWorkerEntryDto[] EntriesToPatch { get; }
            public HotReloadShimCompileResult CompileResult { get; }
            public string[] AddedFieldNames { get; }

            private ShimFirstCompileResult(
                bool fileFailed,
                List<HotReloadMethodOutcome> outcomes,
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                string[] addedFieldNames = null)
            {
                FileFailed = fileFailed;
                Outcomes = outcomes ?? new List<HotReloadMethodOutcome>();
                EntriesToPatch = entriesToPatch ?? Array.Empty<TransformWorkerEntryDto>();
                CompileResult = compileResult;
                AddedFieldNames = addedFieldNames;
            }

            public static ShimFirstCompileResult Failed(List<HotReloadMethodOutcome> outcomes)
            {
                return new ShimFirstCompileResult(
                    true,
                    outcomes,
                    Array.Empty<TransformWorkerEntryDto>(),
                    null);
            }

            public static ShimFirstCompileResult Succeeded(
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                List<HotReloadMethodOutcome> outcomes = null,
                string[] addedFieldNames = null)
            {
                return new ShimFirstCompileResult(
                    false,
                    outcomes,
                    entriesToPatch,
                    compileResult,
                    addedFieldNames);
            }
        }
    }
}
