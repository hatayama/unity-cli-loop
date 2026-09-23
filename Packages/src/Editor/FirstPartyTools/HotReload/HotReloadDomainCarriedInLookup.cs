using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the sibling records of a live domain.
    /// </summary>
    internal sealed class HotReloadDomainCarriedInLookup : IHotReloadCarriedInLookup
    {
        private readonly HotReloadDomain _domain;

        internal HotReloadDomainCarriedInLookup(HotReloadDomain domain)
        {
            _domain = domain ?? throw new ArgumentNullException(nameof(domain));
        }

        public string TryGetCompanionHash(string projectRelativePath)
        {
            return _domain.CompanionSources.TryGetHash(projectRelativePath);
        }

        public string TryGetAppliedHash(string projectRelativePath)
        {
            return _domain.TryGetAppliedSource(projectRelativePath)?.Hash;
        }

        public bool IsActive(string projectRelativePath)
        {
            return ListActivePaths().Contains(projectRelativePath);
        }

        /// <summary>
        /// The files that hold changes of their own: patched methods, added members, or the
        /// declarations of introduced types. The sibling planner brings back the same set.
        /// </summary>
        internal HashSet<string> ListActivePaths()
        {
            HashSet<string> activePaths = new HashSet<string>(
                _domain.ListActiveFilePaths(),
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            activePaths.UnionWith(_domain.ListPathsWithActiveAddedMembers());
            // Why the declaring files of introduced types count as active: removing the last
            // introduced type from such a file leaves no row, yet the file had changes of its own,
            // and a Play-entry reload drops the types while the companion ledger comes back.
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> introducedTypes = _domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < introducedTypes.Count; index++)
            {
                if (!string.IsNullOrEmpty(introducedTypes[index].OwnerProjectRelativePath))
                {
                    activePaths.Add(introducedTypes[index].OwnerProjectRelativePath);
                }
            }

            return activePaths;
        }
    }
}
