using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Per-file apply outcome collected by the orchestrator before aggregation.
    /// </summary>
    internal sealed class HotReloadFileProcessResult
    {
        public List<HotReloadMethodOutcome> Outcomes { get; }
        public List<string> Warnings { get; }
        public int PatchedCount { get; }
        public List<string> SuppressedPausePointIds { get; }
        public List<string> RetargetedPausePointIds { get; }
        public List<string> InlineRiskMethodLabels { get; }

        /// <summary>
        /// The labels of the added Unity messages the engine will not reach until a compile, so
        /// the run can name them in one warning rather than one per method.
        /// </summary>
        public List<string> UnforwardedUnityMessageLabels { get; }
        public int UnchangedMethodCount { get; }
        public int RevertedUnchangedCount { get; }
        public string[] AddedFieldNames { get; }
        public string[] AddedConstNames { get; }
        public string SourceContentSha256 { get; }
        public IReadOnlyList<HotReloadIntroducedTypeOutcome> IntroducedTypes { get; }

        /// <summary>
        /// What proved this file belongs to the assembly it was patched into, for a file the
        /// last compile did not list. Null for every file the compiler already accounts for.
        /// </summary>
        public HotReloadNewSourceMembershipEvidence NewSourceMembershipEvidence { get; }

        /// <summary>
        /// The removed-member names this file's warning listed, for the run to record once it
        /// ends. Null when the file never reached the removed-member notices, which leaves the
        /// record alone.
        /// </summary>
        public IReadOnlyList<string> DisplayedRemovedMembers { get; }

        public HotReloadFileProcessResult(
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            int patchedCount,
            List<string> suppressedPausePointIds = null,
            List<string> inlineRiskMethodLabels = null,
            List<string> unforwardedUnityMessageLabels = null,
            int unchangedMethodCount = 0,
            List<string> retargetedPausePointIds = null,
            string[] addedFieldNames = null,
            string sourceContentSha256 = null,
            string[] addedConstNames = null,
            int revertedUnchangedCount = 0,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> introducedTypes = null,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null,
            IReadOnlyList<string> displayedRemovedMembers = null)
        {
            Outcomes = outcomes;
            Warnings = warnings;
            PatchedCount = patchedCount;
            SuppressedPausePointIds = suppressedPausePointIds ?? new List<string>();
            InlineRiskMethodLabels = inlineRiskMethodLabels ?? new List<string>();
            UnforwardedUnityMessageLabels =
                unforwardedUnityMessageLabels ?? new List<string>();
            UnchangedMethodCount = unchangedMethodCount;
            RetargetedPausePointIds = retargetedPausePointIds ?? new List<string>();
            AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
            SourceContentSha256 = sourceContentSha256;
            AddedConstNames = addedConstNames ?? Array.Empty<string>();
            RevertedUnchangedCount = revertedUnchangedCount;
            IntroducedTypes = introducedTypes ?? Array.Empty<HotReloadIntroducedTypeOutcome>();
            NewSourceMembershipEvidence = newSourceMembershipEvidence;
            DisplayedRemovedMembers = displayedRemovedMembers;
        }
    }
}
