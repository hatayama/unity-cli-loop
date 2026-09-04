using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The output buffers every stage of one file's apply pipeline appends to.
    /// </summary>
    /// <remarks>
    /// Why separate from HotReloadApplyContext: these are the mutable half. Keeping them apart
    /// makes it visible at each call site which stage writes results and which only reads inputs.
    /// The two injected lists span the whole run, not one file, so the caller owns them.
    /// </remarks>
    internal sealed class HotReloadFileSinks
    {
        internal HotReloadFileSinks(
            List<string> siblingDerivedWarnings,
            List<HotReloadOneShotCallerNoteEnricher.Candidate> oneShotCallerNoteCandidates)
        {
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");

            Outcomes = new List<HotReloadMethodOutcome>();
            Warnings = new List<string>();
            SuppressedPausePointIds = new List<string>();
            RetargetedPausePointIds = new List<string>();
            SiblingDerivedWarnings = siblingDerivedWarnings;
            OneShotCallerNoteCandidates = oneShotCallerNoteCandidates;
        }

        internal List<HotReloadMethodOutcome> Outcomes { get; }

        internal List<string> Warnings { get; }

        internal List<string> SuppressedPausePointIds { get; }

        internal List<string> RetargetedPausePointIds { get; }

        // Shared across the whole run so sibling-derived text can be deduped once at the end.
        internal List<string> SiblingDerivedWarnings { get; }

        // Shared across the whole run; null when the caller collects no one-shot caller notes.
        internal List<HotReloadOneShotCallerNoteEnricher.Candidate> OneShotCallerNoteCandidates { get; }
    }
}
