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
            IReadOnlyList<(string ProjectRelativePath, string WorkerSourcePath,
                HotReloadNewSourceMembershipEvidence Evidence)> filesToInclude,
            IReadOnlyList<string> changedSinceApplyPaths)
        {
            FilesToInclude = filesToInclude;
            ChangedSinceApplyPaths = changedSinceApplyPaths;
        }

        internal IReadOnlyList<(string ProjectRelativePath, string WorkerSourcePath,
            HotReloadNewSourceMembershipEvidence Evidence)> FilesToInclude { get; }

        internal IReadOnlyList<string> ChangedSinceApplyPaths { get; }
    }

    /// <summary>
    /// Collects active sibling files of an assembly that are not already in this run.
    /// </summary>
    internal static class HotReloadActiveSiblingRebindPlanner
    {
        internal static HotReloadActiveSiblingRebindPlan Plan(
            HotReloadDomain domain,
            string assemblyName,
            string[] assemblySourceFiles,
            IReadOnlyCollection<string> pathsAlreadyInRun,
            Func<string, string> resolveWorkerSourcePath)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(assemblySourceFiles != null, "assemblySourceFiles must not be null.");
            Debug.Assert(pathsAlreadyInRun != null, "pathsAlreadyInRun must not be null.");
            Debug.Assert(resolveWorkerSourcePath != null, "resolveWorkerSourcePath must not be null.");

            StringComparer comparer = HotReloadSourcePathNormalizer.ProjectRelativePathComparer();
            HashSet<string> candidates = new HashSet<string>(comparer);
            AddCandidatePaths(candidates, domain.ListActiveFilePaths());
            AddCandidatePaths(
                candidates,
                domain.ListPathsWithActiveAddedMembers());
            AddIntroducedTypeOwnerPaths(candidates, domain, assemblyName);

            HashSet<string> assemblyFiles = new HashSet<string>(comparer);
            for (int index = 0; index < assemblySourceFiles.Length; index++)
            {
                assemblyFiles.Add(assemblySourceFiles[index].Replace('\\', '/'));
            }

            List<(string ProjectRelativePath, string WorkerSourcePath,
                HotReloadNewSourceMembershipEvidence Evidence)> filesToInclude =
                new List<(string ProjectRelativePath, string WorkerSourcePath,
                    HotReloadNewSourceMembershipEvidence Evidence)>();
            List<string> changedSinceApplyPaths = new List<string>();
            foreach (string path in candidates)
            {
                ClassifyCandidate(
                    domain,
                    path,
                    assemblyName,
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

        // Why the declaring files of introduced types are candidates: such a file holds no patch
        // and no added member of its own, so neither of the other two sources ever names it, yet
        // the members added to the type it declares live in the shim this run replaces.
        private static void AddIntroducedTypeOwnerPaths(
            HashSet<string> candidates,
            HotReloadDomain domain,
            string assemblyName)
        {
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors = domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < descriptors.Count; index++)
            {
                HotReloadIntroducedTypeDescriptor descriptor = descriptors[index];
                if (!string.Equals(descriptor.OriginalAssemblyName, assemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(descriptor.OwnerProjectRelativePath))
                {
                    continue;
                }

                candidates.Add(descriptor.OwnerProjectRelativePath);
            }
        }

        // Why a file outside the compiled source list needs evidence: Unity does not answer for
        // the assembly of a file it never compiled, so the only thing that can place it in this
        // assembly is what the reload that first applied it verified.
        private static bool BelongsToAssembly(
            HotReloadDomain domain,
            string path,
            HashSet<string> assemblyFiles,
            string assemblyName,
            out HotReloadNewSourceMembershipEvidence evidence)
        {
            if (assemblyFiles.Contains(path))
            {
                evidence = null;
                return true;
            }

            evidence = domain.TryGetNewSourceMembershipEvidence(path);
            return evidence != null
                && string.Equals(evidence.AssemblyName, assemblyName, StringComparison.Ordinal);
        }

        private static void ClassifyCandidate(
            HotReloadDomain domain,
            string path,
            string assemblyName,
            HashSet<string> assemblyFiles,
            IReadOnlyCollection<string> pathsAlreadyInRun,
            Func<string, string> resolveWorkerSourcePath,
            List<(string ProjectRelativePath, string WorkerSourcePath,
                HotReloadNewSourceMembershipEvidence Evidence)> filesToInclude,
            List<string> changedSinceApplyPaths)
        {
            if (!BelongsToAssembly(
                    domain,
                    path,
                    assemblyFiles,
                    assemblyName,
                    out HotReloadNewSourceMembershipEvidence evidence)
                || ContainsPath(pathsAlreadyInRun, path))
            {
                return;
            }

            (string Hash, bool IsFullyApplied)? recorded =
                domain.TryGetAppliedSource(path);
            if (recorded == null)
            {
                return;
            }

            string workerSourcePath = resolveWorkerSourcePath(path);
            if (string.IsNullOrEmpty(workerSourcePath) || !File.Exists(workerSourcePath))
            {
                return;
            }

            string probeHash = new HotReloadSourceContentHasher().ComputeContentHash(
                File.ReadAllBytes(workerSourcePath));
            if (string.Equals(probeHash, recorded.Value.Hash, StringComparison.Ordinal))
            {
                filesToInclude.Add((path, workerSourcePath, evidence));
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
