using System;
using System.Collections.Generic;
using System.Globalization;
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
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            List<string> suppressedPausePointIds = new List<string>();
            List<string> retargetedPausePointIds = new List<string>();
            List<string> inlineRiskMethodLabels = new List<string>();
            List<string> addedFields = new List<string>();
            int patchedTotal = 0;
            int unchangedTotal = 0;
            // Why after the file loop (not inside ProcessFileAsync): duplicate paths in one
            // run must still apply twice; recording mid-run would short-circuit the second copy.
            Dictionary<string, (string Hash, bool IsFullyApplied)> appliedSourceHashByPath =
                new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);

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
                    ct).ConfigureAwait(false);

                outcomes.AddRange(fileResult.Outcomes);
                warnings.AddRange(fileResult.Warnings);
                HotReloadOutcomeAggregation.AppendDistinct(suppressedPausePointIds, fileResult.SuppressedPausePointIds);
                HotReloadOutcomeAggregation.AppendDistinct(retargetedPausePointIds, fileResult.RetargetedPausePointIds);
                HotReloadOutcomeAggregation.AppendDistinct(inlineRiskMethodLabels, fileResult.InlineRiskMethodLabels);
                patchedTotal += fileResult.PatchedCount;
                unchangedTotal += fileResult.UnchangedMethodCount;
                addedFields.AddRange(fileResult.AddedFieldNames);
                HotReloadAppliedSourceLifecycle.StageAppliedSourceHash(
                    appliedSourceHashByPath,
                    HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(filePath),
                    fileResult.SourceContentSha256,
                    fileResult.Outcomes);
            }

            foreach (KeyValuePair<string, (string Hash, bool IsFullyApplied)> pair in appliedSourceHashByPath)
            {
                HotReloadAppliedSourceLedger.Record(pair.Key, pair.Value.Hash, pair.Value.IsFullyApplied);
            }

            if (inlineRiskMethodLabels.Count > 0)
            {
                warnings.Add(
                    HotReloadOutcomeAggregation.FormatInlineRiskAggregatedWarning(
                        inlineRiskMethodLabels.Count,
                        patchedTotal,
                        inlineRiskMethodLabels));
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            addedFields.Sort(StringComparer.Ordinal);
            if (addedFields.Count > 0)
            {
                // Why from this list: AddedFields and the lifetime warning must name the same
                // applied fields. Worker-side classified sets include unused and unavailable
                // declarations, and retry overwrites names without replacing first-pass warnings.
                warnings.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        HotReloadConstants.AddedFieldsLifetimeWarningFormat,
                        string.Join(", ", addedFields)));
            }

            (int patchedCount, int failedCount, int skippedCount, int alreadyActiveCount, int addedCount) =
                HotReloadOutcomeAggregation.CountMethodOutcomeKinds(outcomes);
            HotReloadOrchestratorLog.LogHotReloadApplySummary(
                patchedCount,
                failedCount,
                skippedCount,
                alreadyActiveCount,
                addedCount,
                failedCount == 0,
                correlationId);
            return new HotReloadOrchestratorResult(
                outcomes,
                warnings,
                patchedTotal,
                HotReloadPatcher.ActiveChangeCount,
                suppressedPausePointIds,
                unchangedTotal,
                retargetedPausePointIds,
                addedFields.ToArray());
        }

        private static async Task<HotReloadFileProcessResult> ProcessFileAsync(
            string assemblyResolvePath,
            string workerSourcePath,
            string correlationId,
            CancellationToken ct)
        {
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

            TransformWorkerInputDto workerInput = new TransformWorkerInputDto
            {
                sourcePath = Path.GetFullPath(workerSourcePath),
                defines = defines,
                referencePaths = referencePaths,
                targetTypesAssemblyPath = Path.GetFullPath(targetDllPath),
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths(projectRoot, compilationAssembly.sourceFiles)
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
            AppendWorkerNotices(
                workerOutput,
                snapshotSource,
                projectRelativePath,
                assemblyName,
                assemblyResolvePath,
                outcomes,
                warnings);

            TransformWorkerUnchangedMethodDto[] unchangedMethods =
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>();
            int unchangedMethodCount = unchangedMethods.Length;

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            HotReloadEntryApplier.RevertUnchangedPatches(assemblyName, unchangedMethods);

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
                    sourceContentSha256: workerOutput.sourceContentSha256);
            }

            outcomes.AddRange(gateResult.SkippedOutcomes);
            warnings.AddRange(gateResult.Warnings);

            (HotReloadFileProcessResult earlyEntries,
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                string[] resolvedAddedFieldNames) = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
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
                        sourceContentSha256: workerOutput.sourceContentSha256);
                }

                HotReloadSignatureChangeCoverage.AppendSignatureChangeCallersRepatchedWarnings(
                    warnings,
                    entriesToPatch,
                    gateResult.Hits,
                    snapshotLabels);
            }

            // Harmony Patch/Unpatch and method resolution against loaded modules require main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            return HotReloadEntryApplier.ApplyEntriesAndBuildResult(
                assemblyName,
                assemblyResolvePath,
                projectRelativePath,
                compileResult,
                entriesToPatch,
                addedFieldNames,
                workerOutput,
                snapshotLabels,
                snapshotAddedLabels,
                outcomes,
                warnings,
                suppressedPausePointIds,
                retargetedPausePointIds,
                unchangedMethodCount);
        }

        // Why after the worker: const-only / empty files have no patch candidates, so the
        // missing-baseline warning was pure noise (FB E). Emit only when the worker saw at
        // least one method or accessor row.
        private static void AppendWorkerNotices(
            TransformWorkerOutputDto workerOutput,
            string snapshotSource,
            string projectRelativePath,
            string assemblyName,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings)
        {
            if (snapshotSource == null
                && CountPatchCandidateRows(workerOutput) >= 1)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.NoVerifiedSourceSnapshotWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (workerOutput.baselineDisabledByDuplicateKeys)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.BaselineDisabledByDuplicateKeysWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (workerOutput.parseErrors != null)
            {
                foreach (string parseError in workerOutput.parseErrors)
                {
                    warnings.Add(parseError);
                }
            }

            if (workerOutput.skipped != null)
            {
                foreach (TransformWorkerSkippedDto skipped in workerOutput.skipped)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Skipped(
                            skipped.method ?? "(unknown)",
                            skipped.reason ?? string.Empty,
                            assemblyResolvePath));
                }
            }

            if (workerOutput.declarationDriftWarnings != null)
            {
                // Surfaced before the empty-entries early return so const drift still reaches
                // the response when every method in the file is skipped or unchanged.
                foreach (string driftWarning in workerOutput.declarationDriftWarnings)
                {
                    warnings.Add(driftWarning);
                }
            }
        }

        private static int CountPatchCandidateRows(TransformWorkerOutputDto workerOutput)
        {
            int entryCount = workerOutput.entries != null ? workerOutput.entries.Length : 0;
            int skippedCount = workerOutput.skipped != null ? workerOutput.skipped.Length : 0;
            int unchangedCount =
                workerOutput.unchangedMethods != null ? workerOutput.unchangedMethods.Length : 0;
            return entryCount + skippedCount + unchangedCount;
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

        internal sealed class HotReloadFileProcessResult
        {
            public List<HotReloadMethodOutcome> Outcomes { get; }
            public List<string> Warnings { get; }
            public int PatchedCount { get; }
            public List<string> SuppressedPausePointIds { get; }
            public List<string> RetargetedPausePointIds { get; }
            public List<string> InlineRiskMethodLabels { get; }
            public int UnchangedMethodCount { get; }
            public string[] AddedFieldNames { get; }
            public string SourceContentSha256 { get; }

            public HotReloadFileProcessResult(
                List<HotReloadMethodOutcome> outcomes,
                List<string> warnings,
                int patchedCount,
                List<string> suppressedPausePointIds = null,
                List<string> inlineRiskMethodLabels = null,
                int unchangedMethodCount = 0,
                List<string> retargetedPausePointIds = null,
                string[] addedFieldNames = null,
                string sourceContentSha256 = null)
            {
                Outcomes = outcomes;
                Warnings = warnings;
                PatchedCount = patchedCount;
                SuppressedPausePointIds = suppressedPausePointIds ?? new List<string>();
                InlineRiskMethodLabels = inlineRiskMethodLabels ?? new List<string>();
                UnchangedMethodCount = unchangedMethodCount;
                RetargetedPausePointIds = retargetedPausePointIds ?? new List<string>();
                AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
                SourceContentSha256 = sourceContentSha256;
            }
        }
    }
}
