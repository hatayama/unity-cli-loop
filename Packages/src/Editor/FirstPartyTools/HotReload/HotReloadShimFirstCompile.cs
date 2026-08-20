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
            HotReloadOrchestrator.HotReloadFileProcessResult EarlyResult,
            TransformWorkerEntryDto[] EntriesToPatch,
            HotReloadShimCompileResult CompileResult,
            string[] AddedFieldNames)> ResolveEntriesToPatchAsync(
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            string projectRelativePath,
            string correlationId,
            string[] addedFieldNames,
            HashSet<string> snapshotLabels,
            HashSet<string> snapshotAddedLabels,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds,
            int unchangedMethodCount,
            List<string> siblingDerivedWarnings,
            CancellationToken ct)
        {
            if (gateResult.UsedWorkerRetry)
            {
                addedFieldNames = gateResult.Isolation.AddedFieldNames;
                if (gateResult.Isolation.RetryEntries.Length == 0)
                {
                    return (
                        new HotReloadOrchestrator.HotReloadFileProcessResult(
                            outcomes,
                            warnings,
                            0,
                            suppressedPausePointIds,
                            new List<string>(),
                            unchangedMethodCount,
                            retargetedPausePointIds,
                            addedFieldNames: null,
                            sourceContentSha256: workerOutput.sourceContentSha256),
                        null,
                        null,
                        addedFieldNames);
                }

                return (null, gateResult.Isolation.RetryEntries, gateResult.Isolation.RetryCompileResult, addedFieldNames);
            }

            if (string.IsNullOrEmpty(workerOutput.shimSource)
                || workerOutput.entries == null
                || workerOutput.entries.Length == 0)
            {
                // Why only on this success path: deleting an added method and restoring callers
                // yields empty entries, so the post-shim-compile BeginFileGeneration never runs.
                // Worker failure and shim-compile failure return earlier or later without
                // clearing — same as leaving existing Harmony patches in place when apply does
                // not succeed.
                IReadOnlyList<string> addedLabelsAtClear =
                    HotReloadAddedMemberRegistry.ListActiveMethodKeys(projectRelativePath);
                HotReloadOrchestratorLog.LogHotReloadEmptyEntriesClear(addedLabelsAtClear, correlationId);
                HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
                HotReloadEntryApplier.CommitAddedFieldsForFile(projectRelativePath, workerOutput.addedFieldNames);
                // Why after the clear: a still-declared added method can be worker-skipped
                // (virtual/generic), leaving entries empty while the registry drop is real.
                HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                    warnings,
                    snapshotLabels,
                    snapshotAddedLabels,
                    projectRelativePath,
                    workerOutput,
                    outcomes);
                return (
                    new HotReloadOrchestrator.HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: workerOutput.sourceContentSha256),
                    null,
                    null,
                    addedFieldNames);
            }

            ShimFirstCompileResult firstCompile = await CompileShimFirstPassAsync(
                workerInput,
                workerOutput,
                compilationAssembly,
                targetDllPath,
                defines,
                assemblyResolvePath,
                correlationId,
                siblingDerivedWarnings,
                ct).ConfigureAwait(false);
            if (firstCompile.AddedFieldNames != null)
            {
                addedFieldNames = firstCompile.AddedFieldNames;
            }

            if (firstCompile.FileFailed)
            {
                outcomes.AddRange(firstCompile.Outcomes);
                return (
                    new HotReloadOrchestrator.HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: workerOutput.sourceContentSha256),
                    null,
                    null,
                    addedFieldNames);
            }

            outcomes.AddRange(firstCompile.Outcomes);
            if (firstCompile.EntriesToPatch.Length == 0)
            {
                return (
                    new HotReloadOrchestrator.HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        suppressedPausePointIds,
                        new List<string>(),
                        unchangedMethodCount,
                        retargetedPausePointIds,
                        addedFieldNames: null,
                        sourceContentSha256: workerOutput.sourceContentSha256),
                    null,
                    null,
                    addedFieldNames);
            }

            return (null, firstCompile.EntriesToPatch, firstCompile.CompileResult, addedFieldNames);
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
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        shimReferencePaths.ErrorMessage,
                        assemblyResolvePath));
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                workerInput.projectRelativePath,
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
                    failureMethodLabel = HotReloadPatcher.FormatMethodKeyParts(
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
                    HotReloadMethodOutcome.Failed(
                        failureMethodLabel,
                        HotReloadSkippedMemberCompileNote.AppendNotes(
                            compileResult.ErrorMessage,
                            fallbackErrorMessages,
                            workerOutput.skipped),
                        assemblyResolvePath));
            }

            siblingDerivedWarnings.AddRange(isolation.SiblingConstDriftWarnings);
            List<HotReloadMethodOutcome> isolationOutcomes = new List<HotReloadMethodOutcome>();
            isolationOutcomes.AddRange(isolation.FailedMethodOutcomes);
            isolationOutcomes.AddRange(isolation.SkippedCallerOutcomes);
            if (isolation.RetryEntries.Length == 0)
            {
                return ShimFirstCompileResult.SucceededEmpty(isolationOutcomes, isolation.AddedFieldNames);
            }

            return ShimFirstCompileResult.Succeeded(
                isolation.RetryEntries,
                isolation.RetryCompileResult,
                isolationOutcomes,
                isolation.AddedFieldNames);
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

            public static ShimFirstCompileResult Failed(HotReloadMethodOutcome outcome)
            {
                return new ShimFirstCompileResult(
                    true,
                    new List<HotReloadMethodOutcome> { outcome },
                    Array.Empty<TransformWorkerEntryDto>(),
                    null);
            }

            public static ShimFirstCompileResult SucceededEmpty(
                List<HotReloadMethodOutcome> outcomes,
                string[] addedFieldNames = null)
            {
                return new ShimFirstCompileResult(
                    false,
                    outcomes,
                    Array.Empty<TransformWorkerEntryDto>(),
                    null,
                    addedFieldNames);
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
