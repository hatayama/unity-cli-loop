using System;

using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // [InitializeOnLoad] runs this type's static field initializers once per AppDomain load, so
    // LoadedAtUtc marks this domain's birth. Used by physics-callback dispatch diagnostics to
    // report how long the current domain has been alive without a reload -- a suspected factor in
    // the existing-instance physics-dispatch miss (see docs/regression-harness.md).
    [InitializeOnLoad]
    internal static class PausePointDomainReloadTracker
    {
        public static readonly DateTime LoadedAtUtc = DateTime.UtcNow;

        public static double SecondsSinceLoad()
        {
            return (DateTime.UtcNow - LoadedAtUtc).TotalSeconds;
        }
    }
}
