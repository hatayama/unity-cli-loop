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
    internal sealed class HotReloadGroupProcessor
    {
        private readonly HotReloadGroupProcessorDependencies _dependencies;
        private readonly HotReloadGroupStageCollaborators _collaborators;
        private readonly HotReloadDomain _domain;
        private readonly HotReloadFileEntryApplier _fileEntryApplier;
        private readonly HotReloadEntryApplier _entryApplier;
        private readonly HotReloadGroupCommitStage _commitStage;

        internal HotReloadGroupProcessor(
            HotReloadGroupProcessorDependencies dependencies,
            HotReloadGroupStageCollaborators collaborators,
            HotReloadGroupCommitStage commitStage)
        {
            Debug.Assert(dependencies != null, "dependencies must not be null.");
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            Debug.Assert(commitStage != null, "commitStage must not be null.");
            _dependencies = dependencies;
            _collaborators = collaborators;
            _domain = collaborators.Domain;
            _fileEntryApplier = collaborators.FileEntryApplier;
            _entryApplier = collaborators.EntryApplier;
            _commitStage = commitStage;
        }

        internal async Task<IReadOnlyList<HotReloadFileProcessResult>> ProcessGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            string correlationId,
            CancellationToken ct)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");

            HotReloadGroupFile firstFile = files[0];
            // Application.dataPath and the ledgers require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (!_dependencies.ValidateNewSourceMembership(files))
            {
                return _fileEntryApplier.BuildUnappliedGroupResults(files);
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
                _domain.IntroducedTypes,
                workerInput.targetAssemblyName,
                workerInput.targetAssemblyMvid).ToArray();
            HotReloadIntroducedTypePreparationResult preparation = await _dependencies
                .PrepareIntroducedTypes(files, workerInput, ct)
                .ConfigureAwait(false);
            // Why before the failure branch and only here: one preparation covers every
            // declaration of the group, so a declaration bound from a retained artifact and a
            // non-fatal notice are true whether or not another declaration was refused. A
            // declaration bound from a retained artifact introduces nothing, so no artifact of
            // this run publishes it and a run whose only change is such a declaration never
            // reaches an activation at all.
            HotReloadIntroducedTypeOutcomeSink.Append(files, preparation.AlreadyActiveTypes);
            HotReloadIntroducedTypeOutcomeSink.AppendNotices(files, preparation.Notices);

            if (!preparation.Success)
            {
                // Why the two failures part ways here: a refused declaration is reported as the
                // type it refused, while a preparation that could not run at all refused no
                // declaration and stays a run-level failure of every file of the group.
                if (preparation.Failures.Count > 0)
                {
                    HotReloadIntroducedTypeOutcomeSink.Append(files, preparation.Failures);
                }
                else
                {
                    HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", preparation.ErrorMessage);
                }

                return _fileEntryApplier.BuildUnappliedGroupResults(files);
            }

            if (preparation.Prepared == null)
            {
                return await TransformAndApplyGroupAsync(files, workerInput, null, correlationId, ct)
                    .ConfigureAwait(false);
            }

            return await TransformAndApplyPreparedGroupAsync(
                files,
                workerInput,
                preparation.Prepared,
                correlationId,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the group against the artifact this run prepared, so the transform and the shim
        /// compilation bind the introduced types from the loaded assembly.
        /// </summary>
        private async Task<IReadOnlyList<HotReloadFileProcessResult>> TransformAndApplyPreparedGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto workerInput,
            HotReloadPreparedIntroducedTypes prepared,
            string correlationId,
            CancellationToken ct)
        {
            HotReloadIntroducedTypeArtifact artifact = prepared.Artifact;
            HotReloadIntroducedTypeRegistry registry = _domain.IntroducedTypes;
            List<TransformWorkerIntroducedTypeArtifactDto> records =
                new List<TransformWorkerIntroducedTypeArtifactDto>(workerInput.introducedTypeArtifacts);
            records.Add(HotReloadIntroducedTypeArtifactRecords.CreateRecord(artifact));
            workerInput.introducedTypeArtifacts = records.ToArray();

            // The scope answers binds for the prepared assembly while nothing has activated it, so
            // the shim compilation and the reflection it drives can already reach the new types.
            using (IDisposable preparedScope = _domain.IntroducedTypeResolver.RegisterPrepared(artifact))
            {
                try
                {
                    // Why inside the scope and first in the try: the finally below is what drops
                    // the membership again, so registering anywhere the finally does not cover
                    // would leave the run's membership behind when the scope itself throws.
                    registry.RegisterPrepared(artifact);
                    return await TransformAndApplyGroupAsync(files, workerInput, prepared, correlationId, ct)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // A run the commit boundary activated no longer holds a prepared membership,
                    // so this only drops what a run that never reached activation left behind.
                    registry.DiscardPrepared(artifact);
                }
            }
        }

        private async Task<IReadOnlyList<HotReloadFileProcessResult>> TransformAndApplyGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            TransformWorkerInputDto workerInput,
            HotReloadPreparedIntroducedTypes prepared,
            string correlationId,
            CancellationToken ct)
        {
            HotReloadGroupFile firstFile = files[0];
            TransformWorkerClientResult workerResult = await _dependencies
                .RunWorker(workerInput, ct)
                .ConfigureAwait(false);
            HotReloadOrchestratorLog.LogHotReloadWorkerResult(workerResult, correlationId);
            if (!workerResult.Success)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", workerResult.ErrorMessage);
                return _fileEntryApplier.BuildUnappliedGroupResults(files);
            }

            TransformWorkerOutputDto workerOutput = workerResult.Output;
            Debug.Assert(
                workerOutput.files.Length == files.Count,
                "A group worker run must return one per-file output per edited file.");
            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                workerOutput,
                CollectProjectRelativePaths(files));
            HotReloadGroupNotices.AppendPerFileWorkerNotices(files, rows);
            // Why once for the group: the worker scans the assembly's unedited siblings for const
            // drift as a whole, so flowing them per file would repeat the same texts.
            if (workerOutput.siblingConstDriftWarnings != null)
            {
                firstFile.Sinks.SiblingDerivedWarnings.AddRange(workerOutput.siblingConstDriftWarnings);
            }

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            // Why a run that commits types skips it: peeling a patch here would mutate the domain
            // before the commit boundary, which a failed recheck could then no longer undo, so
            // such a run reverts at the boundary instead.
            if (!_collaborators.CommitPolicy.CommitsIntroducedTypes(prepared, files[0].AssemblyName)
                && !await RevalidateBeforeRevertAsync(
                    files,
                    ct,
                    () => _entryApplier.RevertUnchangedPatchesPerFile(files, rows)).ConfigureAwait(false))
            {
                return _fileEntryApplier.BuildUnappliedGroupResults(files);
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
                prepared);
            HotReloadGroupGateAndCompileResult gateAndCompile = await _dependencies
                .GateAndCompile(context, ct)
                .ConfigureAwait(false);
            if (gateAndCompile.Outcome == HotReloadGroupGateAndCompileOutcome.Failed)
            {
                return _fileEntryApplier.BuildUnappliedGroupResults(files);
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
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context,
            CancellationToken ct)
        {
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            // The worker-bound continuation after the pre-revert check is not guaranteed to
            // resume on Unity's context, while the signature gate reads compilation state.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<HotReloadGroupFile> files = context.Files;
            HotReloadGroupFile gateWarningSink = files[0];
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = await HotReloadSignatureChangeGate.TryApplySignatureChangeGateAsync(
                collaborators,
                context,
                ct).ConfigureAwait(false);
            HotReloadWorkerNoticeAppender.AppendRetrySiblingConstDriftWarnings(
                gateWarningSink.Sinks.SiblingDerivedWarnings,
                gateResult.Isolation);
            HotReloadGroupNotices.AppendRemovedMemberNotices(collaborators.Patcher, context, gateResult);
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
                return HotReloadGroupGateAndCompileResult.Failed();
            }

            HotReloadGroupOutcomeRouter.AppendByFilePath(files, gateResult.SkippedOutcomes);
            // Why one file's warning list: gate warnings name compiled call sites across the
            // assembly, not one edited file, and the run merges every file's warnings anyway.
            gateWarningSink.Sinks.Warnings.AddRange(gateResult.Warnings);

            HotReloadGroupCompileResult compile = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                collaborators,
                context,
                gateResult,
                ct).ConfigureAwait(false);
            if (compile.Outcome == HotReloadGroupCompileOutcome.Failed)
            {
                return HotReloadGroupGateAndCompileResult.Failed();
            }

            // Why a run with no entry still goes on: a reload whose only change is a new type
            // declaration produces no method to patch, and ending here would leave the type it
            // prepared unactivated — the very case new-file support exists for.
            if (compile.Outcome == HotReloadGroupCompileOutcome.ReadyWithoutMethods)
            {
                return HotReloadGroupGateAndCompileResult.ReadyWithoutEntries(gateResult, compile);
            }

            return HotReloadGroupGateAndCompileResult.Ready(gateResult, compile);
        }

        /// <summary>
        /// Turns a gated and compiled group into applied patches: coverage, the last membership
        /// check, the group-wide preflight that resolves every file, and only then the mutation.
        /// </summary>
        internal async Task<IReadOnlyList<HotReloadFileProcessResult>> CompleteApplyAfterCoverageAsync(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile,
            CancellationToken ct)
        {
            // Why coverage only with entries: it checks that every gated replacement the scan
            // found has an entry to patch, and a run the compile left no entry for has no
            // replacement to cover — it reaches the boundary only to commit its types.
            if (compile.HasEntriesToApply
                && gateResult.DidScan
                && !HotReloadGroupNotices.AppendSignatureChangeCoverageNotices(context, gateResult, compile))
            {
                return _fileEntryApplier.BuildUnappliedGroupResults(context.Files);
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            if (!TryAppendNewSourceMembershipFailure(_collaborators, context.Files))
            {
                return _fileEntryApplier.BuildUnappliedGroupResults(context.Files);
            }

            ct.ThrowIfCancellationRequested();
            string staleReason =
                HotReloadGroupCommitBoundary.DescribeStaleReason(_collaborators, context);
            if (staleReason != null)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(context.Files, "(file)", staleReason);
                return _fileEntryApplier.BuildUnappliedGroupResults(context.Files);
            }

            // Why the whole group is resolved before any file is mutated: a file whose entries
            // cannot be resolved must not leave the files applied before it half patched. It is
            // also the last failure a run can take without having activated anything. A run with
            // no entry has nothing to bind or resolve, so it goes straight to the commit point.
            if (!compile.HasEntriesToApply)
            {
                return _commitStage.Commit(context, gateResult, compile, null);
            }

            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles = _dependencies.PrepareGroupEntries(
                context,
                compile.CompileResult,
                compile.EntriesToPatch);
            // Why before the commit point: the preflight decides per file, and a file it could
            // not resolve is the last failure a run can take without having activated anything.
            // Committing anyway would publish a type for a group that goes on to patch nothing.
            if (_commitStage.HoldsUnresolvedFile(preparedFiles))
            {
                return _commitStage.BuildResolutionFailedResults(context, preparedFiles);
            }

            return _commitStage.Commit(context, gateResult, compile, preparedFiles);
        }

        internal static bool TryAppendNewSourceMembershipFailure(
            HotReloadGroupStageCollaborators collaborators,
            IReadOnlyList<HotReloadGroupFile> files)
        {
            string failure =
                HotReloadNewSourceMembershipValidator.TryRevalidateFiles(collaborators, files);
            if (failure == null)
            {
                return true;
            }

            HotReloadGroupOutcomeRouter.AppendGroupFailure(files, "(file)", failure);
            return false;
        }

        internal async Task<bool> RevalidateBeforeRevertAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            CancellationToken ct,
            Action revertUnchangedPatches)
        {
            Debug.Assert(revertUnchangedPatches != null, "revertUnchangedPatches must not be null.");
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ct.ThrowIfCancellationRequested();
            if (!TryAppendNewSourceMembershipFailure(_collaborators, files))
            {
                return false;
            }

            ct.ThrowIfCancellationRequested();
            revertUnchangedPatches();
            return true;
        }

        private void SnapshotGroupState(IReadOnlyList<HotReloadGroupFile> files)
        {
            foreach (HotReloadGroupFile file in files)
            {
                // Why snapshot at the group's apply entry: runs process groups sequentially, and
                // RevertUnchangedPatches / BeginFileGeneration mutate ledgers after the worker.
                // The worker itself does not.
                file.SnapshotLabels = HotReloadAppliedSourceLifecycle.CollectActiveLabelsForFile(
                    _domain,
                    file.ProjectRelativePath);
                file.SnapshotAddedLabels = new HashSet<string>(
                    _domain.ListActiveAddedMethodKeys(file.ProjectRelativePath),
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

        private static List<string> CollectProjectRelativePaths(IReadOnlyList<HotReloadGroupFile> files)
        {
            List<string> projectRelativePaths = new List<string>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                projectRelativePaths.Add(file.ProjectRelativePath);
            }

            return projectRelativePaths;
        }

    }
}
