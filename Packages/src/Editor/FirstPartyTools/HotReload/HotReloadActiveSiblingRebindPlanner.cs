using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>Why a file of the assembly comes back into a reload it was not passed to.</summary>
    internal enum HotReloadSiblingInclusionReason
    {
        // It holds patches or added members that must bind to this reload's shim.
        ActiveChanges = 0,
        // Its last reload left it Skipped or Failed with nothing active, and it gets one more try.
        RetryAfterSkip = 1,
        // An earlier reload was given it unchanged beside what that reload applied.
        Companion = 2
    }

    /// <summary>One file a reload brings back, with where its source is read from and why.</summary>
    internal sealed class HotReloadSiblingInclusion
    {
        internal HotReloadSiblingInclusion(
            string projectRelativePath,
            string workerSourcePath,
            HotReloadNewSourceMembershipEvidence evidence,
            HotReloadSiblingInclusionReason reason)
        {
            ProjectRelativePath = projectRelativePath;
            WorkerSourcePath = workerSourcePath;
            Evidence = evidence;
            Reason = reason;
        }

        internal string ProjectRelativePath { get; }

        internal string WorkerSourcePath { get; }

        internal HotReloadNewSourceMembershipEvidence Evidence { get; }

        internal HotReloadSiblingInclusionReason Reason { get; }
    }

    /// <summary>
    /// Files of one assembly that should be pulled into this reload so their patches bind
    /// to the newest shim, and the files that would have been but changed since.
    /// </summary>
    /// <remarks>
    /// Added methods are emitted into a per-run shim assembly. Callers patched against the
    /// previous run's shim keep calling that old body unless they are re-applied here.
    /// </remarks>
    internal sealed class HotReloadActiveSiblingRebindPlan
    {
        internal HotReloadActiveSiblingRebindPlan(
            IReadOnlyList<HotReloadSiblingInclusion> filesToInclude,
            IReadOnlyList<string> changedSinceApplyPaths,
            IReadOnlyList<string> changedSinceSkipPaths,
            IReadOnlyList<string> changedCompanionPaths)
        {
            FilesToInclude = filesToInclude;
            ChangedSinceApplyPaths = changedSinceApplyPaths;
            ChangedSinceSkipPaths = changedSinceSkipPaths;
            ChangedCompanionPaths = changedCompanionPaths;
        }

        internal IReadOnlyList<HotReloadSiblingInclusion> FilesToInclude { get; }

        internal IReadOnlyList<string> ChangedSinceApplyPaths { get; }

        internal IReadOnlyList<string> ChangedSinceSkipPaths { get; }

        internal IReadOnlyList<string> ChangedCompanionPaths { get; }
    }

    /// <summary>
    /// Collects the files of an assembly that come back into a reload not passed them: files with
    /// active changes, files the last reload left Skipped or Failed, and companion files.
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
            // Why the first reason wins: a file named by several sources comes back once, and the
            // strongest reason is the one its report and its ledger updates follow.
            Dictionary<string, HotReloadSiblingInclusionReason> candidates =
                new Dictionary<string, HotReloadSiblingInclusionReason>(comparer);
            AddCandidatePaths(candidates, domain.ListActiveFilePaths(), HotReloadSiblingInclusionReason.ActiveChanges);
            AddCandidatePaths(
                candidates,
                domain.ListPathsWithActiveAddedMembers(),
                HotReloadSiblingInclusionReason.ActiveChanges);
            AddIntroducedTypeOwnerPaths(candidates, domain, assemblyName);
            AddCandidatePaths(
                candidates,
                domain.ListNotFullyAppliedSourcePaths(),
                HotReloadSiblingInclusionReason.RetryAfterSkip);
            AddCandidatePaths(
                candidates,
                domain.CompanionSources.ListPaths(),
                HotReloadSiblingInclusionReason.Companion);

            HashSet<string> assemblyFiles = new HashSet<string>(comparer);
            for (int index = 0; index < assemblySourceFiles.Length; index++)
            {
                assemblyFiles.Add(assemblySourceFiles[index].Replace('\\', '/'));
            }

            List<HotReloadSiblingInclusion> filesToInclude = new List<HotReloadSiblingInclusion>();
            Dictionary<HotReloadSiblingInclusionReason, List<string>> changedByReason =
                new Dictionary<HotReloadSiblingInclusionReason, List<string>>
                {
                    [HotReloadSiblingInclusionReason.ActiveChanges] = new List<string>(),
                    [HotReloadSiblingInclusionReason.RetryAfterSkip] = new List<string>(),
                    [HotReloadSiblingInclusionReason.Companion] = new List<string>()
                };
            foreach (KeyValuePair<string, HotReloadSiblingInclusionReason> candidate in candidates)
            {
                if (!BelongsToAssembly(
                        domain,
                        candidate.Key,
                        assemblyFiles,
                        assemblyName,
                        out HotReloadNewSourceMembershipEvidence evidence)
                    || ContainsPath(pathsAlreadyInRun, candidate.Key))
                {
                    continue;
                }

                string expectedHash = ExpectedHash(domain, candidate.Key, candidate.Value);
                if (expectedHash == null)
                {
                    continue;
                }

                ClassifyCandidate(
                    expectedHash,
                    new HotReloadSiblingInclusion(
                        candidate.Key,
                        resolveWorkerSourcePath(candidate.Key),
                        evidence,
                        candidate.Value),
                    filesToInclude,
                    changedByReason[candidate.Value]);
            }

            filesToInclude.Sort(
                (left, right) => string.CompareOrdinal(left.ProjectRelativePath, right.ProjectRelativePath));
            foreach (List<string> changed in changedByReason.Values)
            {
                changed.Sort(string.CompareOrdinal);
            }

            return new HotReloadActiveSiblingRebindPlan(
                filesToInclude,
                changedByReason[HotReloadSiblingInclusionReason.ActiveChanges],
                changedByReason[HotReloadSiblingInclusionReason.RetryAfterSkip],
                changedByReason[HotReloadSiblingInclusionReason.Companion]);
        }

        private static void AddCandidatePaths(
            Dictionary<string, HotReloadSiblingInclusionReason> candidates,
            IReadOnlyList<string> paths,
            HotReloadSiblingInclusionReason reason)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                if (!candidates.ContainsKey(paths[index]))
                {
                    candidates.Add(paths[index], reason);
                }
            }
        }

        // Why the declaring files of introduced types are candidates: such a file holds no patch
        // and no added member of its own, so neither of the other two sources ever names it, yet
        // the members added to the type it declares live in the shim this run replaces.
        private static void AddIntroducedTypeOwnerPaths(
            Dictionary<string, HotReloadSiblingInclusionReason> candidates,
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

                candidates[descriptor.OwnerProjectRelativePath] = HotReloadSiblingInclusionReason.ActiveChanges;
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

        // Why a companion is compared against its own ledger: it has no applied-source record,
        // because the reload that was given it wrote no row for it.
        private static string ExpectedHash(
            HotReloadDomain domain,
            string path,
            HotReloadSiblingInclusionReason reason)
        {
            if (reason == HotReloadSiblingInclusionReason.Companion)
            {
                return domain.CompanionSources.TryGetHash(path);
            }

            return domain.TryGetAppliedSource(path)?.Hash;
        }

        private static void ClassifyCandidate(
            string expectedHash,
            HotReloadSiblingInclusion inclusion,
            List<HotReloadSiblingInclusion> filesToInclude,
            List<string> changedPaths)
        {
            string workerSourcePath = inclusion.WorkerSourcePath;
            if (string.IsNullOrEmpty(workerSourcePath) || !File.Exists(workerSourcePath))
            {
                return;
            }

            string probeHash = new HotReloadSourceContentHasher().ComputeContentHash(
                File.ReadAllBytes(workerSourcePath));
            if (string.Equals(probeHash, expectedHash, StringComparison.Ordinal))
            {
                filesToInclude.Add(inclusion);
                return;
            }

            changedPaths.Add(inclusion.ProjectRelativePath);
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
