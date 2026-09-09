using System;
using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The commit point of a group run: the last refusal a run can still take without having
    /// changed the domain, and everything the run does once it is past that point.
    /// </summary>
    internal sealed class HotReloadGroupCommitStage
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadGroupProcessorDependencies _dependencies;
        private readonly HotReloadFileEntryApplier _fileEntryApplier;
        private readonly HotReloadEntryApplier _entryApplier;
        private readonly HotReloadGroupCommitPolicy _commitPolicy;

        internal HotReloadGroupCommitStage(
            HotReloadDomain domain,
            HotReloadGroupProcessorDependencies dependencies,
            HotReloadFileEntryApplier fileEntryApplier,
            HotReloadEntryApplier entryApplier,
            HotReloadGroupCommitPolicy commitPolicy)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(dependencies != null, "dependencies must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            Debug.Assert(entryApplier != null, "entryApplier must not be null.");
            Debug.Assert(commitPolicy != null, "commitPolicy must not be null.");
            _domain = domain;
            _dependencies = dependencies;
            _fileEntryApplier = fileEntryApplier;
            _entryApplier = entryApplier;
            _commitPolicy = commitPolicy;
        }

        internal bool HoldsUnresolvedFile(IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            foreach (HotReloadPreparedGroupFile prepared in preparedFiles)
            {
                if (prepared.Kind == HotReloadGroupFilePreparationKind.ResolutionFailed)
                {
                    return true;
                }
            }

            return false;
        }

        // The group result of a run the preflight refused: the file that failed reports the same
        // resolution failure the apply step would have reported for it, and every sibling is left
        // unapplied because the group is atomic on this side of the commit point.
        internal List<HotReloadFileProcessResult> BuildResolutionFailedResults(
            HotReloadApplyContext context,
            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            List<HotReloadFileProcessResult> results =
                new List<HotReloadFileProcessResult>(preparedFiles.Count);
            foreach (HotReloadPreparedGroupFile prepared in preparedFiles)
            {
                if (prepared.Kind == HotReloadGroupFilePreparationKind.ResolutionFailed)
                {
                    results.Add(_fileEntryApplier.BuildResolutionFailedResult(
                        context, prepared.File, prepared.Resolution));
                    continue;
                }

                results.Add(_fileEntryApplier.BuildUnappliedResult(prepared.File));
            }

            return results;
        }

        /// <summary>
        /// The commit point of a group run and everything that follows it: the introduced types
        /// become active, the patches this run supersedes are peeled, and the resolved entries are
        /// applied. A failure after the commit point leaves the types active and fails methods.
        /// </summary>
        internal IReadOnlyList<HotReloadFileProcessResult> Commit(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile,
            IReadOnlyList<HotReloadPreparedGroupFile> preparedFiles)
        {
            IReadOnlyList<HotReloadGroupFile> files = context.Files;
            HotReloadPreparedIntroducedTypes prepared = context.PreparedIntroducedTypes;
            if (prepared != null)
            {
                _domain.IntroducedTypes.Activate(prepared.Artifact);
                // Why after the activation and not at preparation: only a type the boundary
                // published is introduced, so a run that never reached here must report none.
                HotReloadIntroducedTypeOutcomeSink.Append(files, BuildIntroducedRows(prepared.Artifact));
            }

            bool commitsIntroducedTypes =
                _commitPolicy.CommitsIntroducedTypes(prepared, files[0].AssemblyName);
            if (commitsIntroducedTypes)
            {
                _entryApplier.RevertUnchangedPatchesPerFile(
                    files,
                    HotReloadWorkerRowsByFile.Build(context.WorkerOutput, context.ProjectRelativePaths));
            }

            if (preparedFiles == null)
            {
                // The empty-entries generation clear a run that commits types deferred past the
                // commit point, because clearing it before the boundary would mutate the domain
                // a failed recheck can no longer undo. A run without types already cleared.
                if (commitsIntroducedTypes)
                {
                    _commitPolicy.ClearEmptyFileGenerations(context);
                }

                return _fileEntryApplier.BuildUnappliedGroupResults(files);
            }

            IReadOnlyList<HotReloadFileProcessResult> results = _dependencies.ApplyPreparedEntries(
                context,
                compile.CompileResult,
                preparedFiles);
            RecordSupersededSignaturesAfterApply(context, gateResult.GatedReplacementMethodKeys);
            return results;
        }

        private List<HotReloadIntroducedTypeOutcome> BuildIntroducedRows(
            HotReloadIntroducedTypeArtifact artifact)
        {
            List<HotReloadIntroducedTypeOutcome> rows =
                new List<HotReloadIntroducedTypeOutcome>(artifact.Descriptors.Count);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                rows.Add(
                    HotReloadIntroducedTypeOutcome.Introduced(
                        descriptor.MetadataName.Value,
                        descriptor.OriginalAssemblyName,
                        descriptor.OwnerProjectRelativePath));
            }

            return rows;
        }

        // Why per file: a group applies file by file, so only the rows that actually reached
        // Harmony may claim their removed signatures were superseded. A partly applied file
        // patches some rows and leaves the rest failed or file-atomically skipped.
        private void RecordSupersededSignaturesAfterApply(
            HotReloadApplyContext context,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            for (int index = 0; index < context.Files.Count; index++)
            {
                HotReloadGroupFile file = context.Files[index];
                Debug.Assert(file.FileOutput != null, "Every file must carry its worker output row.");
                HotReloadSupersededSignatureRecorder.RecordFromAppliedEntries(
                    _domain,
                    file.ProjectRelativePath,
                    file.Sinks.AppliedEntries,
                    file.FileOutput.removedMethodSignatures
                        ?? Array.Empty<TransformWorkerRemovedMethodSignatureDto>(),
                    gatedReplacementMethodKeys);
            }
        }
    }
}
