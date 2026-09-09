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
            HotReloadGroupCommitStage groupCommitStage,
            HotReloadGroupProcessor groupProcessor,
            HotReloadOrchestrator orchestrator,
            HotReloadStatusExecutor statusExecutor,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(harmony != null, "harmony must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            Debug.Assert(entryApplier != null, "entryApplier must not be null.");
            Debug.Assert(transformWorkerClient != null, "transformWorkerClient must not be null.");
            Debug.Assert(groupCommitStage != null, "groupCommitStage must not be null.");
            Debug.Assert(groupProcessor != null, "groupProcessor must not be null.");
            Debug.Assert(orchestrator != null, "orchestrator must not be null.");
            Debug.Assert(statusExecutor != null, "statusExecutor must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(
                editorStateSnapshotCapture != null, "editorStateSnapshotCapture must not be null.");
            Domain = domain;
            Harmony = harmony;
            Patcher = patcher;
            FileEntryApplier = fileEntryApplier;
            EntryApplier = entryApplier;
            TransformWorkerClient = transformWorkerClient;
            GroupCommitStage = groupCommitStage;
            GroupProcessor = groupProcessor;
            Orchestrator = orchestrator;
            StatusExecutor = statusExecutor;
            PackageRootCapture = packageRootCapture;
            EditorStateSnapshotCapture = editorStateSnapshotCapture;
        }

        internal HotReloadDomain Domain { get; }

        internal IHotReloadHarmony Harmony { get; }

        internal HotReloadPatcher Patcher { get; }

        internal HotReloadFileEntryApplier FileEntryApplier { get; }

        internal HotReloadEntryApplier EntryApplier { get; }

        internal TransformWorkerClient TransformWorkerClient { get; }

        internal HotReloadGroupCommitStage GroupCommitStage { get; }

        internal HotReloadGroupProcessor GroupProcessor { get; }

        internal HotReloadOrchestrator Orchestrator { get; }

        internal HotReloadStatusExecutor StatusExecutor { get; }

        internal IHotReloadPackageRootCapture PackageRootCapture { get; }

        internal IHotReloadEditorStateSnapshotCapture EditorStateSnapshotCapture { get; }
    }
}
