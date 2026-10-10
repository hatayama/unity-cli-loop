using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Collects the removed signatures whose compiled callers each group's signature-change gate
    /// left uncovered, and turns them into warnings once the run has applied every group.
    /// </summary>
    /// <remarks>
    /// Why at the end of the run: a gate sees only its own group's entries, and groups run one
    /// assembly at a time, so a caller in another assembly may be patched by an earlier group, a
    /// later group, or an earlier run. A caller whose patch is active when the run ends no longer
    /// runs the compiled body that calls the old signature; one whose patch a later group peeled
    /// runs it again.
    /// </remarks>
    internal sealed class HotReloadRunStaleSignatureWarnings
    {
        private readonly List<HotReloadStaleSignatureCallSites> _callSites =
            new List<HotReloadStaleSignatureCallSites>();

        internal void AddRange(IReadOnlyList<HotReloadStaleSignatureCallSites> callSites)
        {
            Debug.Assert(callSites != null, "callSites must not be null.");
            _callSites.AddRange(callSites);
        }

        /// <summary>
        /// Appends one warning per recorded signature that still has a caller running its compiled
        /// body, leaving out every caller one of the active patches replaces.
        /// </summary>
        internal void AppendTo(List<string> warnings, IReadOnlyList<HotReloadActivePatchInfo> activePatches)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(activePatches != null, "activePatches must not be null.");
            Dictionary<string, HashSet<string>> activeLabelsByAssembly =
                CollectActiveLabelsByAssembly(activePatches);
            foreach (HotReloadStaleSignatureCallSites callSites in _callSites)
            {
                List<string> callerKeys = CollectCompiledCallerKeys(callSites, activeLabelsByAssembly);
                if (callerKeys.Count == 0)
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.StaleSignatureCallersWarningFormat,
                        callSites.RemovedMethodKey,
                        string.Join(", ", callerKeys)));
            }
        }

        private static Dictionary<string, HashSet<string>> CollectActiveLabelsByAssembly(
            IReadOnlyList<HotReloadActivePatchInfo> activePatches)
        {
            Dictionary<string, HashSet<string>> activeLabelsByAssembly =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (HotReloadActivePatchInfo patch in activePatches)
            {
                if (!activeLabelsByAssembly.TryGetValue(patch.AssemblyName, out HashSet<string> labels))
                {
                    labels = new HashSet<string>(StringComparer.Ordinal);
                    activeLabelsByAssembly.Add(patch.AssemblyName, labels);
                }

                labels.Add(patch.MethodKey);
            }

            return activeLabelsByAssembly;
        }

        // Why active callers are dropped before the display de-duplication: two assemblies can
        // declare a caller with the same wire key, and the one still running its compiled body has
        // to stay listed even when the other one, recorded first, is active.
        private static List<string> CollectCompiledCallerKeys(
            HotReloadStaleSignatureCallSites callSites,
            Dictionary<string, HashSet<string>> activeLabelsByAssembly)
        {
            List<string> callerKeys = new List<string>();
            HashSet<string> seenCallerKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteHit caller in callSites.Callers)
            {
                if (IsReplacedByActivePatch(caller, activeLabelsByAssembly))
                {
                    continue;
                }

                if (seenCallerKeys.Add(caller.CallerMethodKey))
                {
                    callerKeys.Add(caller.CallerMethodKey);
                }
            }

            return callerKeys;
        }

        // Why the label rather than the wire key: an active patch is described by the label of its
        // resolved method, which the hit's label spells the same way, nested types included.
        private static bool IsReplacedByActivePatch(
            HotReloadCallSiteHit caller,
            Dictionary<string, HashSet<string>> activeLabelsByAssembly)
        {
            return activeLabelsByAssembly.TryGetValue(caller.CallerAssemblyName, out HashSet<string> labels)
                && labels.Contains(HotReloadSignatureChangeCoverage.FormatCallSiteCallerLabel(caller));
        }
    }
}
