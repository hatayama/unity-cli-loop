using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The collaborators the stages of one group run are bound to, handed to each stage as an
    /// argument instead of read back from the installed services.
    /// </summary>
    /// <remarks>
    /// Why this is not <see cref="HotReloadServices"/>: this is the bundle that is finished before
    /// the stages are, and the services are only complete after them, so the stages cannot be
    /// handed the services they are part of. A stage that read the installed services at call time
    /// would run a replacement's group against whichever domain happened to be installed.
    /// </remarks>
    internal sealed class HotReloadGroupStageCollaborators
    {
        internal HotReloadGroupStageCollaborators(
            HotReloadDomain domain,
            HotReloadPatcher patcher,
            HotReloadFileEntryApplier fileEntryApplier,
            HotReloadEntryApplier entryApplier,
            TransformWorkerClient transformWorkerClient,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            HotReloadGroupCommitPolicy commitPolicy)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            Debug.Assert(entryApplier != null, "entryApplier must not be null.");
            Debug.Assert(transformWorkerClient != null, "transformWorkerClient must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(
                editorStateSnapshotCapture != null, "editorStateSnapshotCapture must not be null.");
            Debug.Assert(commitPolicy != null, "commitPolicy must not be null.");
            Domain = domain;
            Patcher = patcher;
            FileEntryApplier = fileEntryApplier;
            EntryApplier = entryApplier;
            TransformWorkerClient = transformWorkerClient;
            PackageRootCapture = packageRootCapture;
            EditorStateSnapshotCapture = editorStateSnapshotCapture;
            CommitPolicy = commitPolicy;
        }

        internal HotReloadDomain Domain { get; }

        internal HotReloadPatcher Patcher { get; }

        internal HotReloadFileEntryApplier FileEntryApplier { get; }

        internal HotReloadEntryApplier EntryApplier { get; }

        internal TransformWorkerClient TransformWorkerClient { get; }

        internal IHotReloadPackageRootCapture PackageRootCapture { get; }

        internal IHotReloadEditorStateSnapshotCapture EditorStateSnapshotCapture { get; }

        internal HotReloadGroupCommitPolicy CommitPolicy { get; }
    }
}
