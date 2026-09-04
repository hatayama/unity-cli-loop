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
        public int UnchangedMethodCount { get; }
        public int RevertedUnchangedCount { get; }
        public string[] AddedFieldNames { get; }
        public string[] AddedConstNames { get; }
        public string SourceContentSha256 { get; }

        public HotReloadFileProcessResult(
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            int patchedCount,
            List<string> suppressedPausePointIds = null,
            List<string> inlineRiskMethodLabels = null,
            int unchangedMethodCount = 0,
            List<string> retargetedPausePointIds = null,
            string[] addedFieldNames = null,
            string sourceContentSha256 = null,
            string[] addedConstNames = null,
            int revertedUnchangedCount = 0)
        {
            Outcomes = outcomes;
            Warnings = warnings;
            PatchedCount = patchedCount;
            SuppressedPausePointIds = suppressedPausePointIds ?? new List<string>();
            InlineRiskMethodLabels = inlineRiskMethodLabels ?? new List<string>();
            UnchangedMethodCount = unchangedMethodCount;
            RetargetedPausePointIds = retargetedPausePointIds ?? new List<string>();
            AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
            SourceContentSha256 = sourceContentSha256;
            AddedConstNames = addedConstNames ?? Array.Empty<string>();
            RevertedUnchangedCount = revertedUnchangedCount;
        }
    }
}
