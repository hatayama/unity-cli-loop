using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// End-to-end hot-reload pipeline: resolve assembly → worker → shim compile → match → patch.
    /// </summary>
    internal static class HotReloadOrchestrator
    {
        /// <summary>
        /// Runs hot reload for each path in <paramref name="files"/>.
        /// <paramref name="contentPathOverride"/> is test-only: when set, the worker reads that
        /// path while assembly resolution still uses <paramref name="files"/> (so edited copies
        /// can live under <c>Library/UloopHotReload/TestSources/</c> without provoking AssetDatabase).
        /// <paramref name="contentPathOverrides"/> is the per-file form of that hook.
        /// </summary>
        public static async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyList<string> contentPathOverrides = null)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            string correlationId = VibeLogger.GenerateCorrelationId();
            HotReloadRunAccumulator run = new HotReloadRunAccumulator();

            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                string filePath = files[index];
                string workerSourcePath = ResolveWorkerSourcePath(
                    filePath,
                    contentPathOverride,
                    contentPathOverrides,
                    index);

                // Why ConfigureAwait(false): UnityCliLoopTool forbids capturing Unity's
                // SynchronizationContext across awaits — while Play Mode is paused that context
                // does not run continuations, so a true resume would hang the tool forever.
                // ProcessFileAsync switches back via MainThreadSwitcher (EditorApplication.update
                // queue) before any main-thread-only editor API or Harmony patch.
                HotReloadFileProcessResult fileResult = await ProcessFileAsync(
                    filePath,
                    workerSourcePath,
                    correlationId,
                    run.SiblingDerivedWarnings,
                    run.OneShotCallerNoteCandidates,
                    ct).ConfigureAwait(false);

                run.Add(HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(filePath), fileResult);
            }

            run.RecordAppliedSourceHashes();

            await MainThreadSwitcher.SwitchToMainThread(ct);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            run.ApplyOneShotCallerNotes(projectRoot);

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return run.BuildResult(correlationId);
        }

        private static async Task<HotReloadFileProcessResult> ProcessFileAsync(
            string assemblyResolvePath,
            string workerSourcePath,
            string correlationId,
            List<string> siblingDerivedWarnings,
            List<HotReloadOneShotCallerNoteEnricher.Candidate> oneShotCallerNoteCandidates,
            CancellationToken ct)
        {
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            List<string> suppressedPausePointIds = new List<string>();
            List<string> retargetedPausePointIds = new List<string>();

            // CompilationPipeline / Application.dataPath require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);

            (HotReloadFileProcessResult earlyResolve,
                string projectRelativePath,
                string assemblyName,
                UnityCompilationAssembly compilationAssembly,
                string targetDllPath,
                string projectRoot) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                assemblyResolvePath,
                workerSourcePath,
                outcomes,
                warnings,
                correlationId);
            if (earlyResolve != null)
            {
                return earlyResolve;
            }

            // Why snapshot at this file's apply entry: multi-file runs process files
            // sequentially, and RevertUnchangedPatches / BeginFileGeneration mutate ledgers
            // after the worker. The worker itself does not.
            HashSet<string> snapshotLabels = HotReloadAppliedSourceLifecycle.CollectActiveLabelsForFile(projectRelativePath);
            HashSet<string> snapshotAddedLabels = new HashSet<string>(
                HotReloadAddedMemberRegistry.ListActiveMethodKeys(projectRelativePath),
                StringComparer.Ordinal);

            string[] defines = compilationAssembly.defines ?? Array.Empty<string>();
            string[] referencePaths = HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(compilationAssembly, targetDllPath);

            // Why projectRelativePath (not workerSourcePath): contentPathOverride E2E copies live
            // under Library/UloopHotReload/TestSources/ and are absent from the PDB document list.
            // Assembly resolution already computed the on-disk Assets/Packages path above.
            string snapshotSource = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                projectRelativePath,
                targetDllPath);

            HotReloadChangedSiblingScanResult siblingScan = HotReloadChangedSiblingSourceDetector.Detect(
                projectRoot,
                assemblyName,
                targetDllPath,
                compilationAssembly.sourceFiles,
                projectRelativePath);
            if (!string.IsNullOrEmpty(siblingScan.ScanLimitWarning))
            {
                siblingDerivedWarnings.Add(siblingScan.ScanLimitWarning);
            }

            TransformWorkerInputDto workerInput = new TransformWorkerInputDto
            {
                sourcePath = Path.GetFullPath(workerSourcePath),
                defines = defines,
                referencePaths = referencePaths,
                targetTypesAssemblyPath = Path.GetFullPath(targetDllPath),
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths(projectRoot, compilationAssembly.sourceFiles),
                changedSiblingSourcePaths = siblingScan.ChangedSiblingAbsolutePaths
            };

            TransformWorkerClientResult workerResult =
                await TransformWorkerClient.RunAsync(workerInput, ct).ConfigureAwait(false);
            HotReloadOrchestratorLog.LogHotReloadWorkerResult(workerResult, correlationId);
            if (!workerResult.Success)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed("(file)", workerResult.ErrorMessage, assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            TransformWorkerOutputDto workerOutput = workerResult.Output;
            string[] addedFieldNames = workerOutput.addedFieldNames;
            HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                workerOutput,
                snapshotSource,
                projectRelativePath,
                assemblyName,
                assemblyResolvePath,
                outcomes,
                warnings,
                siblingDerivedWarnings);

            TransformWorkerUnchangedMethodDto[] unchangedMethods =
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>();
            int unchangedMethodCount = unchangedMethods.Length;

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            int revertedUnchangedCount = HotReloadEntryApplier.RevertUnchangedPatches(
                assemblyName,
                unchangedMethods);

            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = await HotReloadSignatureChangeGate.TryApplySignatureChangeGateAsync(
                projectRoot,
                assemblyName,
                workerInput,
                workerOutput,
                compilationAssembly,
                targetDllPath,
                defines,
                assemblyResolvePath,
                projectRelativePath,
                correlationId,
                ct).ConfigureAwait(false);
            HotReloadWorkerNoticeAppender.AppendRetrySiblingConstDriftWarnings(siblingDerivedWarnings, gateResult.Isolation);
            // Why after the gate: a gated replacement is not applied, so listing it under
            // "Removed members stay present... edited bodies no longer call them" is false.
            string removedMembersWarning = HotReloadRemovedMembersWarning.FormatRemovedMembersWarning(
                workerOutput.removedMembers,
                workerOutput.removedMethodSignatures,
                gateResult.GatedReplacementMethodKeys);
            if (removedMembersWarning != null)
            {
                warnings.Add(removedMembersWarning);
            }

            HotReloadStalePatchOutcomes.Append(
                outcomes,
                workerOutput,
                gateResult.GatedReplacementMethodKeys,
                projectRelativePath,
                assemblyResolvePath);

            if (gateResult.FileFailed)
            {
                // Why not apply first-pass entries: a gate retry null means the replacement was
                // not isolated. Falling through would apply the unguarded return-type change.
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(signature-change-gate)",
                        gateResult.FailureMessage,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(
                    outcomes,
                    warnings,
                    0,
                    unchangedMethodCount: unchangedMethodCount,
                    sourceContentSha256: workerOutput.sourceContentSha256,
                    revertedUnchangedCount: revertedUnchangedCount);
            }

            outcomes.AddRange(gateResult.SkippedOutcomes);
            warnings.AddRange(gateResult.Warnings);

            (HotReloadFileProcessResult earlyEntries,
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                string[] resolvedAddedFieldNames,
                string[] resolvedAddedConstNames) = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                gateResult,
                workerInput,
                workerOutput,
                compilationAssembly,
                targetDllPath,
                defines,
                assemblyResolvePath,
                projectRelativePath,
                correlationId,
                addedFieldNames,
                snapshotLabels,
                snapshotAddedLabels,
                outcomes,
                warnings,
                suppressedPausePointIds,
                retargetedPausePointIds,
                unchangedMethodCount,
                siblingDerivedWarnings,
                revertedUnchangedCount,
                ct).ConfigureAwait(false);
            if (earlyEntries != null)
            {
                return earlyEntries;
            }

            addedFieldNames = resolvedAddedFieldNames;

            if (gateResult.DidScan)
            {
                // Why after entriesToPatch is final and before Harmony: isolation or a gate
                // retry can drop a covering caller without dropping the replacement. A third
                // worker run is not allowed (max two); fail the file instead of applying.
                List<string> lostReplacementKeys = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                    entriesToPatch,
                    gateResult.Hits,
                    gateResult.ScanTargetKeys);
                if (lostReplacementKeys.Count > 0)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Failed(
                            "(signature-change-gate)",
                            string.Format(
                                HotReloadConstants.SignatureChangeCoverageLostFailureFormat,
                                string.Join(", ", lostReplacementKeys)),
                            assemblyResolvePath));
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: workerOutput.sourceContentSha256,
                        revertedUnchangedCount: revertedUnchangedCount);
                }

                HotReloadSignatureChangeCoverage.AppendSignatureChangeCallersRepatchedWarnings(
                    warnings,
                    entriesToPatch,
                    gateResult.Hits,
                    snapshotLabels);
            }

            // Harmony Patch/Unpatch and method resolution against loaded modules require main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            HotReloadFileProcessResult applied = HotReloadEntryApplier.ApplyEntriesAndBuildResult(
                assemblyName,
                assemblyResolvePath,
                projectRelativePath,
                compileResult,
                entriesToPatch,
                addedFieldNames,
                resolvedAddedConstNames,
                workerOutput,
                snapshotLabels,
                snapshotAddedLabels,
                outcomes,
                warnings,
                suppressedPausePointIds,
                retargetedPausePointIds,
                unchangedMethodCount,
                oneShotCallerNoteCandidates,
                revertedUnchangedCount);
            // Why after apply: earlier returns (gate fail, shim compile, coverage loss)
            // never applied the replacement, so leftover Active rows must not claim they
            // were superseded.
            RecordSupersededSignaturesAfterApply(
                applied,
                workerOutput,
                gateResult.GatedReplacementMethodKeys);
            return applied;
        }

        private static void RecordSupersededSignaturesAfterApply(
            HotReloadFileProcessResult applied,
            TransformWorkerOutputDto workerOutput,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (!HasAppliedChange(applied.Outcomes))
            {
                return;
            }

            HotReloadSupersededSignatureRecorder.RecordFromWorkerOutput(
                workerOutput,
                gatedReplacementMethodKeys);
        }

        private static bool HasAppliedChange(IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            for (int index = 0; index < outcomes.Count; index++)
            {
                HotReloadMethodOutcomeKind kind = outcomes[index].Kind;
                if (kind == HotReloadMethodOutcomeKind.Patched
                    || kind == HotReloadMethodOutcomeKind.Added)
                {
                    return true;
                }
            }

            return false;
        }

        // Why a list hook: one contentPathOverride cannot feed two edited copies, and
        // AddRange+Sort across files is otherwise untestable.
        private static string ResolveWorkerSourcePath(
            string filePath,
            string contentPathOverride,
            IReadOnlyList<string> contentPathOverrides,
            int index)
        {
            if (contentPathOverrides != null
                && index < contentPathOverrides.Count
                && !string.IsNullOrEmpty(contentPathOverrides[index]))
            {
                return contentPathOverrides[index];
            }

            if (string.IsNullOrEmpty(contentPathOverride))
            {
                return filePath;
            }

            return contentPathOverride;
        }
    }
}
