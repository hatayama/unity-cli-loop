using System;
using System.Collections.Generic;
using System.IO;

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
        internal static string DescribeStaleReason(HotReloadApplyContext context)
        {
            // The Editor may have started compiling or importing while the run awaited its worker.
            string notReadyReason = HotReloadEditorStateSnapshotProvider.GetNotReadyReason(
                HotReloadEditorStateSnapshotProvider.CaptureCurrent());
            if (notReadyReason != null)
            {
                return "The Editor became busy before the reload could be applied: " + notReadyReason;
            }

            string preparationDrift = DescribePreparationDrift(context);
            if (preparationDrift != null)
            {
                return preparationDrift;
            }

            return DescribeRequestSourceDrift(context.Files);
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
                if (!transformHashesByPath.TryGetValue(ownerHash.Key, out string transformHash))
                {
                    continue;
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
                    continue;
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
                return HotReloadAppliedSourceLedger.ComputeContentHash(File.ReadAllBytes(sourcePath));
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
