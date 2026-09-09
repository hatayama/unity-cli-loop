using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything the hot-reload tool needs wired together for one Unity domain. The composition
    /// root builds it; callers take what they need from it rather than reaching for a static.
    /// </summary>
    internal sealed class HotReloadServices
    {
        internal HotReloadServices(
            HotReloadDomain domain,
            IHotReloadHarmony harmony,
            HotReloadPatcher patcher,
            HotReloadFileEntryApplier fileEntryApplier,
            HotReloadEntryApplier entryApplier,
            TransformWorkerClient transformWorkerClient,
            HotReloadGroupStageCollaborators groupStageCollaborators,
            HotReloadGroupCommitStage groupCommitStage,
            HotReloadGroupProcessor groupProcessor,
            IHotReloadOrchestrator orchestrator,
            HotReloadStatusExecutor statusExecutor,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            IHotReloadChangeDetector changeDetector)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(harmony != null, "harmony must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            Debug.Assert(entryApplier != null, "entryApplier must not be null.");
            Debug.Assert(transformWorkerClient != null, "transformWorkerClient must not be null.");
            Debug.Assert(
                groupStageCollaborators != null, "groupStageCollaborators must not be null.");
            Debug.Assert(groupCommitStage != null, "groupCommitStage must not be null.");
            Debug.Assert(groupProcessor != null, "groupProcessor must not be null.");
            Debug.Assert(orchestrator != null, "orchestrator must not be null.");
            Debug.Assert(statusExecutor != null, "statusExecutor must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(
                editorStateSnapshotCapture != null, "editorStateSnapshotCapture must not be null.");
            Debug.Assert(changeDetector != null, "changeDetector must not be null.");
            Domain = domain;
            Harmony = harmony;
            Patcher = patcher;
            FileEntryApplier = fileEntryApplier;
            EntryApplier = entryApplier;
            TransformWorkerClient = transformWorkerClient;
            GroupStageCollaborators = groupStageCollaborators;
            GroupCommitStage = groupCommitStage;
            GroupProcessor = groupProcessor;
            Orchestrator = orchestrator;
            StatusExecutor = statusExecutor;
            PackageRootCapture = packageRootCapture;
            EditorStateSnapshotCapture = editorStateSnapshotCapture;
            ChangeDetector = changeDetector;
        }

        internal HotReloadDomain Domain { get; }

        internal IHotReloadHarmony Harmony { get; }

        internal HotReloadPatcher Patcher { get; }

        internal HotReloadFileEntryApplier FileEntryApplier { get; }

        internal HotReloadEntryApplier EntryApplier { get; }

        internal TransformWorkerClient TransformWorkerClient { get; }

        /// <summary>
        /// The collaborators one group run's stages were bound to. Kept here so a test that calls
        /// a stage directly passes the same bundle the installed run uses.
        /// </summary>
        internal HotReloadGroupStageCollaborators GroupStageCollaborators { get; }

        internal HotReloadGroupCommitStage GroupCommitStage { get; }

        internal HotReloadGroupProcessor GroupProcessor { get; }

        internal IHotReloadOrchestrator Orchestrator { get; }

        internal HotReloadStatusExecutor StatusExecutor { get; }

        internal IHotReloadPackageRootCapture PackageRootCapture { get; }

        internal IHotReloadEditorStateSnapshotCapture EditorStateSnapshotCapture { get; }

        internal IHotReloadChangeDetector ChangeDetector { get; }

        /// <summary>
        /// A copy that runs <paramref name="orchestrator"/> instead of this one, sharing every
        /// other collaborator — including the domain, so installing the copy neither takes the
        /// resolver over nor disposes anything when it is put back.
        /// </summary>
        internal HotReloadServices WithOrchestrator(IHotReloadOrchestrator orchestrator)
        {
            return new HotReloadServices(
                Domain,
                Harmony,
                Patcher,
                FileEntryApplier,
                EntryApplier,
                TransformWorkerClient,
                GroupStageCollaborators,
                GroupCommitStage,
                GroupProcessor,
                orchestrator,
                StatusExecutor,
                PackageRootCapture,
                EditorStateSnapshotCapture,
                ChangeDetector);
        }

        /// <summary>
        /// A copy that selects omitted files through <paramref name="changeDetector"/>, sharing
        /// every other collaborator.
        /// </summary>
        internal HotReloadServices WithChangeDetector(IHotReloadChangeDetector changeDetector)
        {
            return new HotReloadServices(
                Domain,
                Harmony,
                Patcher,
                FileEntryApplier,
                EntryApplier,
                TransformWorkerClient,
                GroupStageCollaborators,
                GroupCommitStage,
                GroupProcessor,
                Orchestrator,
                StatusExecutor,
                PackageRootCapture,
                EditorStateSnapshotCapture,
                changeDetector);
        }
    }
}
