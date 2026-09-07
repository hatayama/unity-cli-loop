using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs one assembly group through the pipeline: one worker run, one shim compile, and one
    /// apply per edited file of the group.
    /// </summary>
    /// <remarks>
    /// Why a group is the unit: the edited files of an assembly are transformed into a single
    /// shim assembly, so a body in one file can call a method or field another file of the same
    /// edit added. The reported and applied unit stays the single file.
    /// </remarks>
    internal static class HotReloadGroupProcessor
    {
        internal static async Task<IReadOnlyList<HotReloadFileProcessResult>> ProcessGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            string correlationId,
            CancellationToken ct)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");

            HotReloadGroupFile firstFile = files[0];
            // Application.dataPath and the ledgers require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!HotReloadGroupProcessorDependencies.Current.ValidateNewSourceMembership(files))
            {
                return BuildUnappliedResults(files);
            }

            SnapshotGroupState(files);

            HotReloadChangedSiblingScanResult siblingScan = HotReloadChangedSiblingSourceDetector.Detect(
                firstFile.ProjectRoot,
                firstFile.AssemblyName,
                firstFile.TargetDllPath,
                firstFile.CompilationAssembly.sourceFiles,
                CollectProjectRelativePaths(files));
            if (!string.IsNullOrEmpty(siblingScan.ScanLimitWarning))
            {
                firstFile.Sinks.SiblingDerivedWarnings.Add(siblingScan.ScanLimitWarning);
            }

            TransformWorkerInputDto workerInput = BuildWorkerInput(files, siblingScan);
            workerInput.introducedTypeArtifacts = HotReloadIntroducedTypeArtifactRecords.CollectActive(
                HotReloadIntroducedTypeHolder.Registry,
                workerInput.targetAssemblyName,
                workerInput.targetAssemblyMvid).ToArray();
            HotReloadIntroducedTypePreparationResult preparation = await HotReloadGroupProcessorDependencies
                .Current
                .PrepareIntroducedTypes(files, workerInput, ct)
                .ConfigureAwait(false);
            if (!preparation.Success)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", preparation.ErrorMessage);
                return BuildUnappliedResults(files);
            }

            if (preparation.Artifact == null)
            {
                return await TransformAndApplyGroupAsync(files, workerInput, null, correlationId, ct)
                    .ConfigureAwait(false);
            }

            return await TransformAndApplyPreparedGroupAsync(
                files,
                workerInput,
                preparation.Artifact,
                correlationId,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the group against the artifact this run prepared, so the transform and the shim
        /// compilation bind the introduced types from the loaded assembly.
        /// </summary>
        private static async Task<IReadOnlyList<HotReloadFileProcessResult>> TransformAndApplyPreparedGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto workerInput,
            HotReloadIntroducedTypeArtifact artifact,
            string correlationId,
            CancellationToken ct)
        {
            HotReloadIntroducedTypeRegistry registry = HotReloadIntroducedTypeHolder.Registry;
            registry.RegisterPrepared(artifact);
            List<TransformWorkerIntroducedTypeArtifactDto> records =
                new List<TransformWorkerIntroducedTypeArtifactDto>(workerInput.introducedTypeArtifacts);
            records.Add(HotReloadIntroducedTypeArtifactRecords.CreateRecord(artifact));
            workerInput.introducedTypeArtifacts = records.ToArray();

            // The scope answers binds for the prepared assembly while nothing has activated it, so
            // the shim compilation and the reflection it drives can already reach the new types.
            using (IDisposable preparedScope = HotReloadIntroducedTypeHolder.Resolver.RegisterPrepared(artifact))
            {
                try
                {
                    return await TransformAndApplyGroupAsync(files, workerInput, artifact, correlationId, ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // The commit boundary that activates a successful run is a later stage; until
                    // it exists the prepared membership is dropped here as well. The discard of a
                    // failed run stays in place once activation replaces the successful path.
                    registry.DiscardPrepared(artifact);
                }
            }
        }

        private static async Task<IReadOnlyList<HotReloadFileProcessResult>> TransformAndApplyGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto workerInput,
            HotReloadIntroducedTypeArtifact preparedArtifact,
            string correlationId,
            CancellationToken ct)
        {
            HotReloadGroupFile firstFile = files[0];
            TransformWorkerClientResult workerResult = await HotReloadGroupProcessorDependencies
                .Current
                .RunWorker(workerInput, ct)
                .ConfigureAwait(false);
            HotReloadOrchestratorLog.LogHotReloadWorkerResult(workerResult, correlationId);
            if (!workerResult.Success)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", workerResult.ErrorMessage);
                return BuildUnappliedResults(files);
            }

            TransformWorkerOutputDto workerOutput = workerResult.Output;
            Debug.Assert(
                workerOutput.files.Length == files.Count,
                "A group worker run must return one per-file output per edited file.");
            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                workerOutput,
                CollectProjectRelativePaths(files));
            AppendPerFileWorkerNotices(files, rows);
            // Why once for the group: the worker scans the assembly's unedited siblings for const
            // drift as a whole, so flowing them per file would repeat the same texts.
            if (workerOutput.siblingConstDriftWarnings != null)
            {
                firstFile.Sinks.SiblingDerivedWarnings.AddRange(workerOutput.siblingConstDriftWarnings);
            }

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            if (!await RevalidateBeforeRevertAsync(
                    files,
                    ct,
                    () => RevertUnchangedPatchesPerFile(files, rows)).ConfigureAwait(false))
            {
                return BuildUnappliedResults(files);
            }

            HotReloadApplyContext context = new HotReloadApplyContext(
                firstFile.ProjectRoot,
                firstFile.AssemblyName,
                correlationId,
                firstFile.CompilationAssembly,
                firstFile.TargetDllPath,
                firstFile.CompilationAssembly.defines ?? Array.Empty<string>(),
                workerInput,
                workerOutput,
                files,
                preparedArtifact);
            HotReloadGroupGateAndCompileResult gateAndCompile = await HotReloadGroupProcessorDependencies
                .Current
                .GateAndCompile(context, ct)
                .ConfigureAwait(false);
            if (!gateAndCompile.HasEntriesToApply)
            {
                return BuildUnappliedResults(files);
            }

            return await CompleteApplyAfterCoverageAsync(
                context,
                gateAndCompile.Gate,
                gateAndCompile.Compile,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the signature-change gate and the first shim compile of the group, which together
        /// decide whether there is anything left to apply.
        /// </summary>
        internal static async Task<HotReloadGroupGateAndCompileResult> GateAndCompileAsync(
            HotReloadApplyContext context,
            CancellationToken ct)
        {
            // The worker-bound continuation after the pre-revert check is not guaranteed to
            // resume on Unity's context, while the signature gate reads compilation state.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<HotReloadGroupFile> files = context.Files;
            HotReloadGroupFile gateWarningSink = files[0];
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = await HotReloadSignatureChangeGate.TryApplySignatureChangeGateAsync(
                context,
                ct).ConfigureAwait(false);
            HotReloadWorkerNoticeAppender.AppendRetrySiblingConstDriftWarnings(
                gateWarningSink.Sinks.SiblingDerivedWarnings,
                gateResult.Isolation);
            AppendRemovedMemberNotices(context, gateResult);
            if (gateResult.FileFailed)
            {
                // Why not apply first-pass entries: a gate retry null means the replacement was
                // not isolated. Falling through would apply the unguarded return-type change.
                // Why every file: the gate consumed the run's one worker retry for the group, so
                // no file of it can be retried on its own.
                HotReloadGroupOutcomeRouter.AppendGroupFailure(
                    files,
                    "(signature-change-gate)",
                    gateResult.FailureMessage);
                return HotReloadGroupGateAndCompileResult.NothingToApply();
            }

            HotReloadGroupOutcomeRouter.AppendByFilePath(files, gateResult.SkippedOutcomes);
            // Why one file's warning list: gate warnings name compiled call sites across the
            // assembly, not one edited file, and the run merges every file's warnings anyway.
            gateWarningSink.Sinks.Warnings.AddRange(gateResult.Warnings);

            HotReloadGroupCompileResult compile = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                context,
                gateResult,
                ct).ConfigureAwait(false);
            if (!compile.HasEntriesToApply)
            {
                return HotReloadGroupGateAndCompileResult.NothingToApply();
            }

            return HotReloadGroupGateAndCompileResult.Ready(gateResult, compile);
        }

        /// <summary>
        /// Turns a gated and compiled group into applied patches: coverage, the last membership
        /// check, the group-wide preflight that resolves every file, and only then the mutation.
        /// </summary>
        internal static async Task<IReadOnlyList<HotReloadFileProcessResult>> CompleteApplyAfterCoverageAsync(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile,
            CancellationToken ct)
        {
            if (gateResult.DidScan && !AppendSignatureChangeCoverageNotices(context, gateResult, compile))
            {
                return BuildUnappliedResults(context.Files);
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            if (!TryAppendNewSourceMembershipFailure(context.Files))
            {
                return BuildUnappliedResults(context.Files);
            }

            ct.ThrowIfCancellationRequested();
            HotReloadGroupProcessorDependencies dependencies = HotReloadGroupProcessorDependencies.Current;
            // Why the whole group is resolved before any file is mutated: a file whose entries
            // cannot be resolved must not leave the files applied before it half patched.
            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles = dependencies.PrepareGroupEntries(
                context,
                compile.CompileResult,
                compile.EntriesToPatch);
            IReadOnlyList<HotReloadFileProcessResult> results = dependencies.ApplyPreparedEntries(
                context,
                compile.CompileResult,
                preparedFiles);
            RecordSupersededSignaturesAfterApply(context, gateResult.GatedReplacementMethodKeys);
            return results;
        }

        internal static bool TryAppendNewSourceMembershipFailure(IReadOnlyList<HotReloadGroupFile> files)
        {
            string failure = HotReloadNewSourceMembershipValidator.TryRevalidateFiles(files);
            if (failure == null)
            {
                return true;
            }

            HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", failure);
            return false;
        }

        internal static async Task<bool> RevalidateBeforeRevertAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            CancellationToken ct,
            Action revertUnchangedPatches)
        {
            Debug.Assert(revertUnchangedPatches != null, "revertUnchangedPatches must not be null.");
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            if (!TryAppendNewSourceMembershipFailure(files))
            {
                return false;
            }

            ct.ThrowIfCancellationRequested();
            revertUnchangedPatches();
            return true;
        }

        // Returns false when a replacement lost its covering caller and the group must fail.
        private static bool AppendSignatureChangeCoverageNotices(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile)
        {
            // Why after entriesToPatch is final and before Harmony: isolation or a gate
            // retry can drop a covering caller without dropping the replacement. A third
            // worker run is not allowed (max two); fail the group instead of applying.
            List<string> lostReplacementKeys = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                context.AssemblyName,
                compile.EntriesToPatch,
                gateResult.Hits,
                gateResult.DeletedCallerExemptions);
            if (lostReplacementKeys.Count > 0)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(
                    context.Files,
                    "(signature-change-gate)",
                    string.Format(
                        HotReloadConstants.SignatureChangeCoverageLostFailureFormat,
                        string.Join(", ", lostReplacementKeys)));
                return false;
            }

            foreach (HotReloadGroupFile file in context.Files)
            {
                // Why the group's entries with this file's labels: a caller in one file can cover
                // a replacement in another, and the snapshot labels decide which file's response
                // the warning belongs to.
                HotReloadSignatureChangeCoverage.AppendSignatureChangeCallersRepatchedWarnings(
                    file.Sinks.Warnings,
                    context.AssemblyName,
                    compile.EntriesToPatch,
                    gateResult.Hits,
                    file.SnapshotLabels);
            }

            return true;
        }

        private static void AppendRemovedMemberNotices(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult)
        {
            foreach (HotReloadGroupFile file in context.Files)
            {
                // Why after the gate: a gated replacement is not applied, so listing it under
                // "Removed members stay present... edited bodies no longer call them" is false.
                string removedMembersWarning = HotReloadRemovedMembersWarning.FormatRemovedMembersWarning(
                    file.FileOutput.removedMembers,
                    file.FileOutput.removedMethodSignatures,
                    gateResult.GatedReplacementMethodKeys);
                if (removedMembersWarning != null)
                {
                    file.Sinks.Warnings.Add(removedMembersWarning);
                }

                HotReloadStalePatchOutcomes.Append(
                    file.Sinks.Outcomes,
                    context.WorkerOutput,
                    file.FileOutput.removedMethodSignatures,
                    gateResult.GatedReplacementMethodKeys,
                    file.ProjectRelativePath,
                    file.AssemblyResolvePath);
            }
        }

        private static void SnapshotGroupState(IReadOnlyList<HotReloadGroupFile> files)
        {
            foreach (HotReloadGroupFile file in files)
            {
                // Why snapshot at the group's apply entry: runs process groups sequentially, and
                // RevertUnchangedPatches / BeginFileGeneration mutate ledgers after the worker.
                // The worker itself does not.
                file.SnapshotLabels =
                    HotReloadAppliedSourceLifecycle.CollectActiveLabelsForFile(file.ProjectRelativePath);
                file.SnapshotAddedLabels = new HashSet<string>(
                    HotReloadFileGenerations.ListActiveAddedMethodKeys(file.ProjectRelativePath),
                    StringComparer.Ordinal);
                // Why projectRelativePath (not workerSourcePath): contentPathOverride E2E copies
                // live under Library/UloopHotReload/TestSources/ and are absent from the PDB
                // document list. Assembly resolution already computed the on-disk path.
                file.SnapshotSource = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                    file.ProjectRelativePath,
                    file.TargetDllPath);
            }
        }

        private static TransformWorkerInputDto BuildWorkerInput(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadChangedSiblingScanResult siblingScan)
        {
            HotReloadGroupFile firstFile = files[0];
            TransformWorkerSourceDto[] sources = new TransformWorkerSourceDto[files.Count];
            for (int index = 0; index < files.Count; index++)
            {
                HotReloadGroupFile file = files[index];
                sources[index] = new TransformWorkerSourceDto
                {
                    sourcePath = Path.GetFullPath(file.WorkerSourcePath),
                    projectRelativePath = file.ProjectRelativePath,
                    snapshotSource = file.SnapshotSource
                };
            }

            return new TransformWorkerInputDto
            {
                sources = sources,
                defines = firstFile.CompilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(
                    firstFile.CompilationAssembly,
                    firstFile.TargetDllPath),
                targetTypesAssemblyPath = Path.GetFullPath(firstFile.TargetDllPath),
                // The retained records normalize an introduced type back to the generation of the
                // assembly that owns its source, so every run has to name that generation even
                // before it carries a record of its own.
                targetAssemblyName = firstFile.AssemblyName,
                targetAssemblyMvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(firstFile.TargetDllPath),
                assemblySourcePaths = HotReloadPatchTargetSupport.BuildAssemblySourcePaths(
                    firstFile.ProjectRoot,
                    firstFile.CompilationAssembly.sourceFiles),
                changedSiblingSourcePaths = siblingScan.ChangedSiblingAbsolutePaths
            };
        }

        private static void AppendPerFileWorkerNotices(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadWorkerRowsByFile rows)
        {
            foreach (HotReloadGroupFile file in files)
            {
                IReadOnlyList<TransformWorkerSkippedDto> fileSkipped = rows.SkippedFor(file.ProjectRelativePath);
                int patchCandidateRowCount = rows.EntriesFor(file.ProjectRelativePath).Count
                    + fileSkipped.Count
                    + rows.UnchangedFor(file.ProjectRelativePath).Count;
                file.FileOutput = rows.FileOutputFor(file.ProjectRelativePath);
                file.UnchangedMethodCount = rows.UnchangedFor(file.ProjectRelativePath).Count;
                HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                    file.FileOutput,
                    fileSkipped,
                    patchCandidateRowCount,
                    file.SnapshotSource,
                    file.ProjectRelativePath,
                    file.AssemblyName,
                    file.AssemblyResolvePath,
                    file.Sinks.Outcomes,
                    file.Sinks.Warnings);
            }
        }

        private static void RevertUnchangedPatchesPerFile(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadWorkerRowsByFile rows)
        {
            foreach (HotReloadGroupFile file in files)
            {
                IReadOnlyList<TransformWorkerUnchangedMethodDto> fileUnchanged =
                    rows.UnchangedFor(file.ProjectRelativePath);
                TransformWorkerUnchangedMethodDto[] unchangedMethods =
                    new TransformWorkerUnchangedMethodDto[fileUnchanged.Count];
                for (int index = 0; index < fileUnchanged.Count; index++)
                {
                    unchangedMethods[index] = fileUnchanged[index];
                }

                file.RevertedUnchangedCount = HotReloadEntryApplier.RevertUnchangedPatches(
                    file.AssemblyName,
                    unchangedMethods,
                    file.Sinks.Outcomes,
                    file.AssemblyResolvePath);
            }
        }

        // Why per file: a group applies file by file, so only the rows that actually reached
        // Harmony may claim their removed signatures were superseded. A partly applied file
        // patches some rows and leaves the rest failed or file-atomically skipped.
        private static void RecordSupersededSignaturesAfterApply(
            HotReloadApplyContext context,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            for (int index = 0; index < context.Files.Count; index++)
            {
                HotReloadGroupFile file = context.Files[index];
                Debug.Assert(file.FileOutput != null, "Every file must carry its worker output row.");
                HotReloadSupersededSignatureRecorder.RecordFromAppliedEntries(
                    file.Sinks.AppliedEntries,
                    file.FileOutput.removedMethodSignatures
                        ?? Array.Empty<TransformWorkerRemovedMethodSignatureDto>(),
                    gatedReplacementMethodKeys);
            }
        }

        private static List<string> CollectProjectRelativePaths(IReadOnlyList<HotReloadGroupFile> files)
        {
            List<string> projectRelativePaths = new List<string>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                projectRelativePaths.Add(file.ProjectRelativePath);
            }

            return projectRelativePaths;
        }

        private static List<HotReloadFileProcessResult> BuildUnappliedResults(
            IReadOnlyList<HotReloadGroupFile> files)
        {
            List<HotReloadFileProcessResult> results =
                new List<HotReloadFileProcessResult>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                results.Add(HotReloadFileEntryApplier.BuildUnappliedResult(file));
            }

            return results;
        }
    }
}
