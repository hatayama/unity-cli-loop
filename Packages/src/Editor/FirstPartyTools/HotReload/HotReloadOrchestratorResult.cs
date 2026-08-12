using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Aggregated outcome of a hot-reload orchestrator run across one or more files.
    /// </summary>
    internal sealed class HotReloadOrchestratorResult
    {
        public IReadOnlyList<HotReloadMethodOutcome> Methods { get; }
        public IReadOnlyList<string> Warnings { get; }
        public int PatchedTotal { get; }
        public int ActivePatchTotal { get; }
        public IReadOnlyList<string> SuppressedPausePointIds { get; }
        public IReadOnlyList<string> RetargetedPausePointIds { get; }
        public int UnchangedTotal { get; }

        public HotReloadOrchestratorResult(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyList<string> warnings,
            int patchedTotal,
            int activePatchTotal,
            IReadOnlyList<string> suppressedPausePointIds = null,
            int unchangedTotal = 0,
            IReadOnlyList<string> retargetedPausePointIds = null)
        {
            Methods = methods;
            Warnings = warnings;
            PatchedTotal = patchedTotal;
            ActivePatchTotal = activePatchTotal;
            SuppressedPausePointIds = suppressedPausePointIds ?? Array.Empty<string>();
            UnchangedTotal = unchangedTotal;
            RetargetedPausePointIds = retargetedPausePointIds ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Per-method outcome: Patched, Skipped, or Failed.
    /// </summary>
    internal sealed class HotReloadMethodOutcome
    {
        public HotReloadMethodOutcomeKind Kind { get; }
        public string Method { get; }
        public string Reason { get; }
        public string FilePath { get; }
        public string LifecycleNote { get; }

        private HotReloadMethodOutcome(
            HotReloadMethodOutcomeKind kind,
            string method,
            string reason,
            string filePath,
            string lifecycleNote)
        {
            Kind = kind;
            Method = method;
            Reason = reason;
            FilePath = filePath;
            LifecycleNote = lifecycleNote ?? string.Empty;
        }

        public static HotReloadMethodOutcome Patched(
            string method,
            string filePath,
            string lifecycleNote = null)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Patched,
                method,
                string.Empty,
                filePath,
                lifecycleNote);
        }

        public static HotReloadMethodOutcome Skipped(string method, string reason, string filePath)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Skipped,
                method,
                reason,
                filePath,
                string.Empty);
        }

        public static HotReloadMethodOutcome Failed(string method, string reason, string filePath)
        {
            return new HotReloadMethodOutcome(
                HotReloadMethodOutcomeKind.Failed,
                method,
                reason,
                filePath,
                string.Empty);
        }
    }

    internal enum HotReloadMethodOutcomeKind
    {
        Patched = 0,
        Skipped = 1,
        Failed = 2
    }
}
