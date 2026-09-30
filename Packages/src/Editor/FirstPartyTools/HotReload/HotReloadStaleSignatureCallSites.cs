using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One removed signature and the compiled call sites the signature-change gate left uncovered
    /// for it, one scanner hit per caller in the order the scan found them.
    /// </summary>
    /// <remarks>
    /// Why hits rather than a formatted warning: a caller in another assembly may be patched by a
    /// later group of the same run, so whether it still runs its compiled body is only known once
    /// every group has applied.
    /// </remarks>
    internal sealed class HotReloadStaleSignatureCallSites
    {
        internal HotReloadStaleSignatureCallSites(
            string removedMethodKey,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> callers)
        {
            Debug.Assert(!string.IsNullOrEmpty(removedMethodKey), "removedMethodKey must not be null or empty.");
            Debug.Assert(callers != null && callers.Count > 0, "callers must hold at least one hit.");
            RemovedMethodKey = removedMethodKey;
            Callers = callers;
        }

        // The wire key of the removed signature (Type::Method(params)), as the warning names it.
        internal string RemovedMethodKey { get; }

        internal IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> Callers { get; }
    }
}
