using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Files of one assembly that should be pulled into this reload so their patches bind
    /// to the newest shim.
    /// </summary>
    /// <remarks>
    /// Added methods are emitted into a per-run shim assembly. Callers patched against the
    /// previous run's shim keep calling that old body unless they are re-applied here.
    /// </remarks>
    internal sealed class HotReloadActiveSiblingRebindPlan
    {
        internal HotReloadActiveSiblingRebindPlan(
            IReadOnlyList<(string ProjectRelativePath, string WorkerSourcePath)> filesToInclude,
            IReadOnlyList<string> changedSinceApplyPaths)
        {
            FilesToInclude = filesToInclude;
            ChangedSinceApplyPaths = changedSinceApplyPaths;
        }

        internal IReadOnlyList<(string ProjectRelativePath, string WorkerSourcePath)> FilesToInclude { get; }

        internal IReadOnlyList<string> ChangedSinceApplyPaths { get; }
    }

    /// <summary>
    /// Collects active sibling files of an assembly that are not already in this run.
    /// </summary>
    internal static class HotReloadActiveSiblingRebindPlanner
    {
        internal static HotReloadActiveSiblingRebindPlan Plan(
            string assemblyName,
            string[] assemblySourceFiles,
            IReadOnlyCollection<string> pathsAlreadyInRun,
            Func<string, string> resolveWorkerSourcePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(assemblySourceFiles != null, "assemblySourceFiles must not be null.");
            Debug.Assert(pathsAlreadyInRun != null, "pathsAlreadyInRun must not be null.");
            Debug.Assert(resolveWorkerSourcePath != null, "resolveWorkerSourcePath must not be null.");

            StringComparer comparer = HotReloadSourcePathNormalizer.ProjectRelativePathComparer();
            HashSet<string> candidates = new HashSet<string>(comparer);
            AddCandidatePaths(candidates, HotReloadPatcher.ListActiveFilePaths());
            AddCandidatePaths(candidates, HotReloadFileGenerations.ListPathsWithActiveAddedMembers());

            HashSet<string> assemblyFiles = new HashSet<string>(comparer);
            for (int index = 0; index < assemblySourceFiles.Length; index++)
            {
                assemblyFiles.Add(assemblySourceFiles[index].Replace('\\', '/'));
            }

            List<(string ProjectRelativePath, string WorkerSourcePath)> filesToInclude =
                new List<(string ProjectRelativePath, string WorkerSourcePath)>();
            List<string> changedSinceApplyPaths = new List<string>();
            foreach (string path in candidates)
            {
                ClassifyCandidate(
                    path,
                    assemblyFiles,
                    pathsAlreadyInRun,
                    resolveWorkerSourcePath,
                    filesToInclude,
                    changedSinceApplyPaths);
            }

            filesToInclude.Sort(
                (left, right) => string.CompareOrdinal(left.ProjectRelativePath, right.ProjectRelativePath));
            changedSinceApplyPaths.Sort(string.CompareOrdinal);
            return new HotReloadActiveSiblingRebindPlan(filesToInclude, changedSinceApplyPaths);
        }

        private static void AddCandidatePaths(HashSet<string> candidates, IReadOnlyList<string> paths)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                candidates.Add(paths[index]);
            }
        }

        private static void ClassifyCandidate(
            string path,
            HashSet<string> assemblyFiles,
            IReadOnlyCollection<string> pathsAlreadyInRun,
            Func<string, string> resolveWorkerSourcePath,
            List<(string ProjectRelativePath, string WorkerSourcePath)> filesToInclude,
            List<string> changedSinceApplyPaths)
        {
            if (!assemblyFiles.Contains(path) || ContainsPath(pathsAlreadyInRun, path))
            {
                return;
            }

            (string Hash, bool IsFullyApplied)? recorded = HotReloadAppliedSourceLedger.TryGet(path);
            if (recorded == null)
            {
                return;
            }

            string workerSourcePath = resolveWorkerSourcePath(path);
            if (string.IsNullOrEmpty(workerSourcePath) || !File.Exists(workerSourcePath))
            {
                return;
            }

            string probeHash = HotReloadAppliedSourceLedger.ComputeContentHash(
                File.ReadAllBytes(workerSourcePath));
            if (string.Equals(probeHash, recorded.Value.Hash, StringComparison.Ordinal))
            {
                filesToInclude.Add((path, workerSourcePath));
                return;
            }

            changedSinceApplyPaths.Add(path);
        }

        private static bool ContainsPath(IReadOnlyCollection<string> pathsAlreadyInRun, string path)
        {
            foreach (string existing in pathsAlreadyInRun)
            {
                if (HotReloadSourcePathNormalizer.ProjectRelativePathComparer().Equals(existing, path))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
