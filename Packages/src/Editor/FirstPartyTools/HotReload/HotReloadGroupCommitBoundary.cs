using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The rechecks a group run has to pass in the last instant before it mutates anything, and
    /// the reason it must not.
    /// </summary>
    /// <remarks>
    /// Why they sit together and after the last await: everything they confirm can change while
    /// the run awaits the worker, the gate and the shim compile, and the run activates an
    /// artifact assembly whose types cannot be taken back once a caller has bound them. Each
    /// check reports an expected external change, so none of them is a <c>Debug.Assert</c>.
    /// </remarks>
    internal static class HotReloadGroupCommitBoundary
    {
        /// <summary>
        /// The reason this group must not be committed, or null when every recheck passed.
        /// </summary>
        internal static string DescribeStaleReason(
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context)
        {
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            // The Editor may have started compiling or importing while the run awaited its worker.
            string notReadyReason =
                collaborators.EditorStateSnapshotCapture.CaptureCurrent()
                    .GetNotReadyReason();
            if (notReadyReason != null)
            {
                return "The Editor became busy before the reload could be applied: " + notReadyReason;
            }

            string targetAssemblyDrift = DescribeTargetAssemblyDrift(collaborators, context);
            if (targetAssemblyDrift != null)
            {
                return targetAssemblyDrift;
            }

            string preparationDrift = DescribePreparationDrift(context);
            if (preparationDrift != null)
            {
                return preparationDrift;
            }

            return DescribeRequestSourceDrift(context.Files);
        }

        /// <summary>
        /// The target assembly that was rebuilt after this run read it, which every introduced
        /// type of the run was normalized against.
        /// </summary>
        /// <remarks>
        /// Why only for a run that commits types: an ordinary patch of a rebuilt assembly is
        /// already refused by the guard the patcher applies per method, while an artifact
        /// assembly carries the module version id of the generation it was compiled for and
        /// stays active for the rest of the domain's life.
        /// </remarks>
        private static string DescribeTargetAssemblyDrift(
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context)
        {
            if (!collaborators.CommitPolicy.CommitsIntroducedTypes(
                    context.PreparedIntroducedTypes,
                    context.AssemblyName))
            {
                return null;
            }

            string currentMvid = TryReadTargetAssemblyMvid(context.TargetDllPath);
            if (currentMvid == null)
            {
                return "The target assembly could not be read again before the reload was applied.";
            }

            if (string.Equals(
                currentMvid,
                context.WorkerInput.targetAssemblyMvid,
                StringComparison.Ordinal))
            {
                return null;
            }

            return "The target assembly was rebuilt after this run read it, so the types it"
                + " prepared belong to a generation the domain no longer has.";
        }

        // Why a failed read is drift rather than a throw: the same rebuild this check exists for
        // replaces the file, so the read can land while it is half written or locked.
        private static string TryReadTargetAssemblyMvid(string targetDllPath)
        {
            try
            {
                return HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// The owner whose source changed between the preparation run and the transform run, so
        /// the artifact assembly no longer describes what the transform saw.
        /// </summary>
        private static string DescribePreparationDrift(HotReloadApplyContext context)
        {
            HotReloadPreparedIntroducedTypes prepared = context.PreparedIntroducedTypes;
            if (prepared == null)
            {
                return null;
            }

            Dictionary<string, string> transformHashesByPath = CollectTransformHashes(context.WorkerOutput);
            foreach (KeyValuePair<string, string> ownerHash in prepared.OwnerSourceHashes)
            {
                // Why fail-closed: a comparison this side cannot make is not a window it has
                // shown to be closed, and the run is about to publish an assembly compiled from
                // the very source whose staleness is in question.
                if (!transformHashesByPath.TryGetValue(ownerHash.Key, out string transformHash)
                    || string.IsNullOrEmpty(transformHash)
                    || string.IsNullOrEmpty(ownerHash.Value))
                {
                    return "The introduced type owner '" + ownerHash.Key
                        + "' has no transform hash, so its staleness cannot be verified.";
                }

                if (string.Equals(ownerHash.Value, transformHash, StringComparison.Ordinal))
                {
                    continue;
                }

                return "The introduced type owner '" + ownerHash.Key
                    + "' changed between preparation and transform, so the prepared assembly no"
                    + " longer describes the transformed source.";
            }

            return null;
        }

        /// <summary>
        /// The request source that changed on disk after the transform run read it.
        /// </summary>
        private static string DescribeRequestSourceDrift(IReadOnlyList<HotReloadGroupFile> files)
        {
            foreach (HotReloadGroupFile file in files)
            {
                if (file.FileOutput == null || string.IsNullOrEmpty(file.FileOutput.sourceContentSha256))
                {
                    return "The request source '" + file.ProjectRelativePath
                        + "' has no transform hash, so its staleness cannot be verified.";
                }

                string currentHash = TryComputeCurrentHash(file.WorkerSourcePath);
                if (currentHash == null)
                {
                    return "The request source '" + file.ProjectRelativePath
                        + "' could not be read again before the reload was applied.";
                }

                if (string.Equals(currentHash, file.FileOutput.sourceContentSha256, StringComparison.Ordinal))
                {
                    continue;
                }

                return "The request source '" + file.ProjectRelativePath
                    + "' changed after it was transformed, so the reload would apply stale code.";
            }

            return null;
        }

        private static Dictionary<string, string> CollectTransformHashes(TransformWorkerOutputDto workerOutput)
        {
            Dictionary<string, string> hashesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            if (workerOutput.files == null)
            {
                return hashesByPath;
            }

            foreach (TransformWorkerFileOutputDto file in workerOutput.files)
            {
                hashesByPath[file.projectRelativePath] = file.sourceContentSha256;
            }

            return hashesByPath;
        }

        // Why a failed read is drift rather than a throw: the file can be deleted or locked by an
        // external editor between the transform run and this instant, which is the very window
        // these checks exist for.
        private static string TryComputeCurrentHash(string sourcePath)
        {
            try
            {
                return new HotReloadSourceContentHasher().ComputeContentHash(File.ReadAllBytes(sourcePath));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
