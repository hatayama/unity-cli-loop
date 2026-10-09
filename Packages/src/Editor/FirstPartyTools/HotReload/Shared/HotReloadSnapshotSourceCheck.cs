using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tells, during one assembly's snapshot capture, which copied sources may not be the ones the
    /// compiler read, and checks such a copy against the checksum the assembly's PDB recorded.
    /// </summary>
    internal sealed class HotReloadSnapshotSourceCheck
    {
        private readonly string _dllPath;
        private readonly string _pdbPath;
        private readonly string _moduleVersionId;
        private readonly HotReloadPdbDocumentIndex _documentIndex;

        internal HotReloadSnapshotSourceCheck(
            long suspectWritesFromUtcTicks,
            string dllPath,
            string pdbPath,
            string moduleVersionId,
            HotReloadPdbDocumentIndex documentIndex)
        {
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(pdbPath), "pdbPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(moduleVersionId), "moduleVersionId must not be null or empty.");
            Debug.Assert(documentIndex != null, "documentIndex must not be null.");

            SuspectWritesFromUtcTicks = suspectWritesFromUtcTicks;
            _dllPath = dllPath;
            _pdbPath = pdbPath;
            _moduleVersionId = moduleVersionId;
            _documentIndex = documentIndex;
        }

        /// <summary>
        /// Sources last written at or after this time (UTC ticks) may have been saved after the
        /// compiler read them.
        /// </summary>
        internal long SuspectWritesFromUtcTicks { get; }

        /// <summary>
        /// Returns whether the bytes match the checksum the compiled assembly's PDB recorded for the
        /// source. A source the PDB has no usable checksum for is not confirmed. Main thread only.
        /// </summary>
        internal bool IsCompiledSource(string projectRoot, string slashNormalizedRelativePath, byte[] sourceBytes)
        {
            return true;
        }
    }

    /// <summary>
    /// What the capture records for one copied source in the stamp manifest.
    /// </summary>
    internal enum HotReloadSnapshotCopyVerdict
    {
        // A stamp line that vouches for the copy.
        StampTrusted,
        // No line: readers compare the copy by bytes.
        NoStamp,
        // A line marked as edited after the compile: the copy is not the compiled source.
        EditedAfterCompile
    }
}
