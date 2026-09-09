using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Selects the entries a group applies: empty-entries clear, the one shim compile of the
    /// group, and file-atomic isolation of a compile failure.
    /// </summary>
    internal static class HotReloadShimFirstCompile
    {
        // Why a helper: the gate-retry / empty-entries / first-pass compile fork is one
        // entries-to-patch stage and kept the group pipeline over CA1502.
        internal static async Task<HotReloadGroupCompileResult> ResolveEntriesToPatchAsync(
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            CancellationToken ct)
        {
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(gateResult != null, "gateResult must not be null.");

            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(collaborators, context.Files))
            {
                return HotReloadGroupCompileResult.Failed();
            }

            if (gateResult.UsedWorkerRetry)
            {
                AdoptRetryAddedMemberNames(context.Files, gateResult.Isolation.RetryFiles);
                if (gateResult.Isolation.RetryEntries.Length == 0)
                {
                    return HotReloadGroupCompileResult.Failed();
                }

                return HotReloadGroupCompileResult.ReadyWithMethods(
                    gateResult.Isolation.RetryEntries,
                    gateResult.Isolation.RetryCompileResult);
            }

            if (string.IsNullOrEmpty(context.WorkerOutput.shimSource)
                || context.WorkerOutput.entries == null
                || context.WorkerOutput.entries.Length == 0)
            {
                // Why only on this success path: worker failure and shim-compile failure return
                // earlier or later without clearing — same as leaving existing Harmony patches in
                // place when apply does not succeed.
                // Why a run that commits types skips it: clearing a generation here would mutate
                // the domain before the commit boundary, which a failed recheck could then no
                // longer undo, so such a run clears at the boundary instead.
                if (!collaborators.CommitPolicy.CommitsIntroducedTypes(
                        context.PreparedIntroducedTypes,
                        context.AssemblyName))
                {
                    collaborators.CommitPolicy.ClearEmptyFileGenerations(context);
                }

                return HotReloadGroupCompileResult.ReadyWithoutMethods();
            }

            return await CompileShimForGroupAsync(collaborators, context, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Compiles the group's shim source once and, on failure, isolates the files whose
        /// methods the compiler errors belong to. Signature-change gate retries never call this —
        /// they already consumed the one worker retry.
        /// </summary>
        private static async Task<HotReloadGroupCompileResult> CompileShimForGroupAsync(
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context,
            CancellationToken ct)
        {
            // BuildShimReferencePaths reads Application.dataPath / platform; stay on main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            TransformWorkerOutputDto workerOutput = context.WorkerOutput;
            bool includeHarmonyReference = HotReloadShimReferenceBuilder.NeedsHarmonyReference(workerOutput);
            bool includeAddedFieldStoreReference =
                HotReloadShimReferenceBuilder.NeedsAddedFieldStoreReference(workerOutput);
            HotReloadShimReferenceBuilder.ShimReferencePathsResult shimReferencePaths = HotReloadShimReferenceBuilder.TryBuildShimReferencePaths(
                context.CompilationAssembly,
                context.TargetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference,
                context.WorkerInput.introducedTypeArtifacts);
            if (shimReferencePaths.ErrorMessage != null)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(
                    context.Files,
                    "(file)",
                    shimReferencePaths.ErrorMessage);
                return HotReloadGroupCompileResult.Failed();
            }

            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferencePaths.References,
                context.Defines,
                context.ProjectRelativePaths,
                ct).ConfigureAwait(false);
            if (compileResult.Success)
            {
                AdoptFirstPassAddedMemberNames(context.Files);
                return HotReloadGroupCompileResult.ReadyWithMethods(workerOutput.entries, compileResult);
            }

            HotReloadOrchestratorLog.LogHotReloadShimCompileFailed(
                compileResult,
                HotReloadConstants.VibeLogShimCompileStageFirstPass,
                context.CorrelationId);

            // Why isolate only here: a signature-change gate retry already used
            // RunIsolationRetryAsync (worker run #2). Calling isolation after that would be a
            // third worker run. Gate retry compile failures return Failed from the gate and
            // never reach this first-compile path.
            HotReloadShimIsolation.HotReloadShimIsolationResult isolation = await HotReloadShimIsolation.TryIsolateShimCompileFailureAsync(
                collaborators.TransformWorkerClient,
                context.WorkerInput,
                workerOutput,
                compileResult,
                context.CompilationAssembly,
                context.TargetDllPath,
                context.Defines,
                context.GroupFilePaths,
                context.CorrelationId,
                ct).ConfigureAwait(false);
            if (isolation == null)
            {
                AppendUnattributableCompileFailure(context, compileResult);
                return HotReloadGroupCompileResult.Failed();
            }

            context.Files[0].Sinks.SiblingDerivedWarnings.AddRange(isolation.SiblingConstDriftWarnings);
            HotReloadGroupOutcomeRouter.AppendByFilePath(context.Files, isolation.FailedMethodOutcomes);
            HotReloadGroupOutcomeRouter.AppendByFilePath(context.Files, isolation.SkippedCallerOutcomes);
            return ResolveIsolatedEntriesToPatch(context, isolation);
        }

        // Why one failure per file: the errors could not be tied to any edited method, so the
        // group as a whole is what failed. A single-entry group still names its method, because
        // "(shim-compile)" would hide the only body the agent edited.
        private static void AppendUnattributableCompileFailure(
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult)
        {
            TransformWorkerEntryDto[] entries = context.WorkerOutput.entries;
            string failureMethodLabel = "(shim-compile)";
            if (entries.Length == 1)
            {
                TransformWorkerEntryDto soleEntry = entries[0];
                failureMethodLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                    new HotReloadMetadataTypeName(soleEntry.typeMetadataName),
                    soleEntry.methodName,
                    soleEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                    soleEntry.genericArity);
            }

            // Why: CompileAndLoadAsync appends "(line N)" only for diagnostics whose
            // #line-mapped file is one of the group's sources — scaffold-only errors stay bare.
            List<string> fallbackErrorMessages = new List<string>(compileResult.Errors.Count);
            for (int errorIndex = 0; errorIndex < compileResult.Errors.Count; errorIndex++)
            {
                fallbackErrorMessages.Add(compileResult.Errors[errorIndex].Message);
            }

            HotReloadGroupOutcomeRouter.AppendGroupFailure(
                context.Files,
                failureMethodLabel,
                HotReloadSkippedMemberCompileNote.AppendNotes(
                    compileResult.ErrorMessage,
                    fallbackErrorMessages,
                    context.WorkerOutput.skipped));
        }

        private static HotReloadGroupCompileResult ResolveIsolatedEntriesToPatch(
            HotReloadApplyContext context,
            HotReloadShimIsolation.HotReloadShimIsolationResult isolation)
        {
            if (isolation.Plan.AllFilesFailed)
            {
                // Why survivors stay Skipped: applying them after a sibling Failed would leave
                // the file half-applied. Isolation still attributes the Failed methods, and the
                // retry ran only to collect the skips its exclusions caused.
                HotReloadGroupOutcomeRouter.AppendByFilePath(
                    context.Files,
                    BuildAtomicFileSkipOutcomes(isolation.RetryEntries, context.GroupFilePaths));
                return HotReloadGroupCompileResult.Failed();
            }

            HotReloadGroupOutcomeRouter.AppendByFilePath(
                context.Files,
                HotReloadFileAtomicIsolationPlan.CollectOutcomes(
                    isolation.Plan.AtomicSkipOutcomesByFile,
                    context.ProjectRelativePaths));
            AccumulateAtomicSkipApply(context.Files, isolation.Plan);
            AdoptRetryAddedMemberNames(context.Files, isolation.RetryFiles);
            if (isolation.RetryEntries.Length == 0)
            {
                return HotReloadGroupCompileResult.Failed();
            }

            return HotReloadGroupCompileResult.ReadyWithMethods(isolation.RetryEntries, isolation.RetryCompileResult);
        }

        private static List<HotReloadMethodOutcome> BuildAtomicFileSkipOutcomes(
            TransformWorkerEntryDto[] retryEntries,
            HotReloadGroupFilePaths groupFilePaths)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>(retryEntries.Length);
            foreach (TransformWorkerEntryDto entry in retryEntries)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        HotReloadMethodKeys.FormatMethodLabelParts(
                            new HotReloadMetadataTypeName(entry.typeMetadataName),
                            entry.methodName,
                            entry.parameterTypeFullNames ?? Array.Empty<string>(),
                            entry.genericArity),
                        HotReloadConstants.AtomicFileSkipReason,
                        groupFilePaths.ResolveAssemblyResolvePath(entry.sourceProjectRelativePath)));
            }

            return outcomes;
        }

        private static void AdoptFirstPassAddedMemberNames(IReadOnlyList<HotReloadGroupFile> files)
        {
            foreach (HotReloadGroupFile file in files)
            {
                file.AddedFieldNames = file.FileOutput.addedFieldNames;
                file.AddedConstNames = file.FileOutput.addedConstNames;
            }
        }

        // Why not apply: a file the isolation retry failed on must keep the previous run's
        // patches, so the apply loop skips it entirely instead of clearing it.
        // Why OR: a file already skipped for parse errors must stay skipped, and the isolation
        // plan only knows about the files its own retry failed on.
        // internal so a test can pin that accumulation without running a shim compile.
        internal static void AccumulateAtomicSkipApply(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadFileAtomicIsolationPlan plan)
        {
            foreach (HotReloadGroupFile file in files)
            {
                file.SkipApply = file.SkipApply || plan.IsFailedFile(file.ProjectRelativePath);
            }
        }

        // Why the retry rows win: the retry re-classified every declaration with the exclusions
        // in place, so its per-file added fields and consts are the ones the apply commits.
        // internal so a test can pin the overwrite without running a shim compile.
        internal static void AdoptRetryAddedMemberNames(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerFileOutputDto[] retryFiles)
        {
            Dictionary<string, TransformWorkerFileOutputDto> retryFilesByPath =
                new Dictionary<string, TransformWorkerFileOutputDto>(StringComparer.Ordinal);
            foreach (TransformWorkerFileOutputDto retryFile in retryFiles)
            {
                retryFilesByPath[retryFile.projectRelativePath] = retryFile;
            }

            foreach (HotReloadGroupFile file in files)
            {
                Debug.Assert(
                    retryFilesByPath.ContainsKey(file.ProjectRelativePath),
                    "A retry worker run must return one per-file output per edited file.");
                TransformWorkerFileOutputDto retryFile = retryFilesByPath[file.ProjectRelativePath];
                file.AddedFieldNames = retryFile.addedFieldNames;
                file.AddedConstNames = retryFile.addedConstNames;
            }
        }
    }
}
