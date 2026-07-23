using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // MarkDomainLoaded is invoked once per AppDomain load by the composition-root bootstrap
    // (through FirstPartyToolsEditorStartup), so the recorded timestamp marks this domain's
    // birth. Used by physics-callback dispatch diagnostics to report how long the current
    // domain has been alive without a reload -- a suspected factor in the existing-instance
    // physics-dispatch miss (see docs/regression-harness.md).
    internal static class PausePointDomainReloadTracker
    {
        private static DateTime? _loadedAtUtc;

        public static void MarkDomainLoaded()
        {
            Debug.Assert(!_loadedAtUtc.HasValue, "MarkDomainLoaded must run once per domain load");

            _loadedAtUtc = DateTime.UtcNow;
        }

        public static double SecondsSinceLoad()
        {
            Debug.Assert(_loadedAtUtc.HasValue, "MarkDomainLoaded must run before SecondsSinceLoad");

            return (DateTime.UtcNow - _loadedAtUtc.Value).TotalSeconds;
        }
    }
}
