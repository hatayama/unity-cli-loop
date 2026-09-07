using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What one run's introduced-type preparation produced: the artifact assembly the run may
    /// activate, and the source hashes that artifact was compiled from.
    /// </summary>
    /// <remarks>
    /// Why the owner hashes travel with the artifact: the commit boundary has to confirm the
    /// transform run read the very sources the artifact was compiled from, and the only evidence
    /// of that is the hash each run reported for the owner file.
    /// </remarks>
    internal sealed class HotReloadPreparedIntroducedTypes
    {
        internal HotReloadPreparedIntroducedTypes(
            HotReloadIntroducedTypeArtifact artifact,
            IReadOnlyDictionary<string, string> ownerSourceHashes)
        {
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            OwnerSourceHashes = ownerSourceHashes ?? throw new ArgumentNullException(nameof(ownerSourceHashes));
        }

        internal HotReloadIntroducedTypeArtifact Artifact { get; }

        /// <summary>
        /// Owner project-relative path to the source content hash the preparation run read.
        /// </summary>
        internal IReadOnlyDictionary<string, string> OwnerSourceHashes { get; }
    }
}
