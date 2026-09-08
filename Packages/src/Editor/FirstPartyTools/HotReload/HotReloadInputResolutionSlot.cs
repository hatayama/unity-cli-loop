using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything one run knows about one input file, from resolution to its final result.
    /// </summary>
    /// <remarks>
    /// Why one object per input instead of parallel arrays: the four pieces are only aligned by a
    /// shared index, so every helper that reads one of them had to receive all of them, and a
    /// helper that copied an array lost the write-back the orchestrator depends on.
    /// </remarks>
    internal sealed class HotReloadInputResolutionSlot
    {
        public HotReloadFileProcessResult Result { get; set; }

        public string ResultPath { get; set; }

        public HotReloadGroupFile GroupFile { get; set; }

        /// <summary>
        /// AlreadyActive rows held back while the last changed group of the assembly can still
        /// absorb this input, and applied afterwards when nothing else filled the slot.
        /// </summary>
        public List<HotReloadMethodOutcome> DeferredAlreadyActive { get; set; }
    }
}
