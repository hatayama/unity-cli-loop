using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What one warm-up loads: the project root and at least one target assembly.
    /// </summary>
    internal sealed class HotReloadWarmUpContext
    {
        internal HotReloadWarmUpContext(string projectRoot, IReadOnlyList<HotReloadWarmUpTarget> targets)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(targets != null && targets.Count > 0, "targets must hold at least one target.");
            ProjectRoot = projectRoot;
            Targets = targets;
        }

        internal string ProjectRoot { get; }

        internal IReadOnlyList<HotReloadWarmUpTarget> Targets { get; }
    }
}
