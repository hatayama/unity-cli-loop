using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Attaches one-shot lifecycle notes to a run's method outcomes: asks for a note for each
    /// candidate whose outcome has none yet, and replaces each outcome that got one.
    /// </summary>
    internal static class HotReloadOneShotCallerNoteAttacher
    {
        internal static void Attach(
            List<HotReloadMethodOutcome> outcomes,
            IReadOnlyList<HotReloadOneShotCallerNoteCandidate> candidates,
            Func<IReadOnlyList<HotReloadOneShotCallerNoteRequest>, IReadOnlyList<string>> buildNotes)
        {
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(candidates != null, "candidates must not be null.");
            Debug.Assert(buildNotes != null, "buildNotes must not be null.");

            List<HotReloadOneShotCallerNoteCandidate> pending = new List<HotReloadOneShotCallerNoteCandidate>();
            List<HotReloadOneShotCallerNoteRequest> requests = new List<HotReloadOneShotCallerNoteRequest>();
            foreach (HotReloadOneShotCallerNoteCandidate candidate in candidates)
            {
                // Why skipped: a note the transform worker already wrote stays authoritative.
                if (!string.IsNullOrEmpty(candidate.Outcome.LifecycleNote))
                {
                    continue;
                }

                pending.Add(candidate);
                requests.Add(new HotReloadOneShotCallerNoteRequest(candidate.Identity, candidate.Outcome.Method));
            }

            IReadOnlyList<string> notes = buildNotes(requests);
            Debug.Assert(notes != null && notes.Count == requests.Count, "buildNotes returns one entry per request.");
            for (int index = 0; index < pending.Count; index++)
            {
                if (notes[index] == null)
                {
                    continue;
                }

                // Why by reference: an outcome an earlier candidate already replaced is no longer in
                // the list, so the first note for one outcome wins.
                int outcomeIndex = outcomes.IndexOf(pending[index].Outcome);
                if (outcomeIndex < 0)
                {
                    continue;
                }

                outcomes[outcomeIndex] = pending[index].Outcome.WithLifecycleNote(notes[index]);
            }
        }
    }
}
