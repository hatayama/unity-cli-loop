namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Why a file has no verified source snapshot, so the warning can say whether a compile would
    /// give it one.
    /// </summary>
    internal enum HotReloadSnapshotMissReason
    {
        /// <summary>The snapshot passed the PDB checksum check; nothing is missing.</summary>
        None,

        /// <summary>The compiled assembly or its PDB is not on disk.</summary>
        NoCompiledAssembly,

        /// <summary>No snapshot was written for this file and assembly generation.</summary>
        NoSnapshotFile,

        /// <summary>
        /// The PDB has no document for the file because none of its code compiled to a method
        /// body, so no compile can produce a baseline for it.
        /// </summary>
        NoDocumentInPdb,

        /// <summary>The snapshot bytes do not match the checksum the PDB recorded.</summary>
        HashMismatch
    }
}
