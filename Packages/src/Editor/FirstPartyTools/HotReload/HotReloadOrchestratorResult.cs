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
        public int RevertedUnchangedTotal { get; }
        public string[] AddedFields { get; }
        public string[] AddedConsts { get; }
        public bool AutoRefreshHeld { get; }
        public bool AutoRefreshHoldNewlyArmed { get; }
        public bool AutoRefreshHoldReleaseDeferred { get; }
        public string AutoRefreshHoldSceneRefreshWarning { get; }
        public IReadOnlyList<string> ReappliedSiblingPaths { get; }
        public IReadOnlyList<HotReloadIntroducedTypeOutcome> IntroducedTypes { get; }

        public HotReloadOrchestratorResult(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyList<string> warnings,
            int patchedTotal,
            int activePatchTotal,
            IReadOnlyList<string> suppressedPausePointIds = null,
            int unchangedTotal = 0,
            IReadOnlyList<string> retargetedPausePointIds = null,
            string[] addedFields = null,
            string[] addedConsts = null,
            int revertedUnchangedTotal = 0,
            HotReloadAutoRefreshHoldSyncResult autoRefreshHold = null,
            IReadOnlyList<string> reappliedSiblingPaths = null,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> introducedTypes = null,
            bool autoRefreshHoldNewlyArmed = false)
        {
            Methods = methods;
            Warnings = warnings;
            PatchedTotal = patchedTotal;
            ActivePatchTotal = activePatchTotal;
            SuppressedPausePointIds = suppressedPausePointIds ?? Array.Empty<string>();
            UnchangedTotal = unchangedTotal;
            RetargetedPausePointIds = retargetedPausePointIds ?? Array.Empty<string>();
            AddedFields = addedFields ?? Array.Empty<string>();
            AddedConsts = addedConsts ?? Array.Empty<string>();
            RevertedUnchangedTotal = revertedUnchangedTotal;
            AutoRefreshHeld = autoRefreshHold != null && autoRefreshHold.Held;
            // Why a separate argument and not autoRefreshHold.NewlyArmed: only the caller knows
            // whether the hold was already armed before its run, and one Sync call cannot tell a
            // run that armed the hold from one that found the reconcile had.
            AutoRefreshHoldNewlyArmed = autoRefreshHoldNewlyArmed;
            AutoRefreshHoldReleaseDeferred = autoRefreshHold != null && autoRefreshHold.ReleaseDeferred;
            AutoRefreshHoldSceneRefreshWarning =
                autoRefreshHold != null ? autoRefreshHold.SceneRefreshWarning : null;
            ReappliedSiblingPaths = reappliedSiblingPaths ?? Array.Empty<string>();
            IntroducedTypes = introducedTypes ?? Array.Empty<HotReloadIntroducedTypeOutcome>();
        }
    }
}
