using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Walks compiled callers of callers until every remaining path is a proven one-shot lifecycle message.
    /// </summary>
    internal static class HotReloadOneShotCallerClosure
    {
        internal const int MaxCallerDepth = 4;
        private const string VisitKeySeparator = "|";

        /// <summary>
        /// Returns proven lifecycle roots, or null when any abort condition makes the note unsafe.
        /// </summary>
        internal static List<OneShotCallerClassification> Resolve(
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> directHits,
            Func<string, HotReloadCallSiteScanner.CompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteScanner.CallSiteHit, bool> isOneShotLifecycleCaller)
        {
            Debug.Assert(directHits != null, "directHits must not be null.");
            Debug.Assert(scan != null, "scan must not be null.");
            Debug.Assert(isOneShotLifecycleCaller != null, "isOneShotLifecycleCaller must not be null.");

            List<OneShotCallerClassification> roots = new List<OneShotCallerClassification>();
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> frontier =
                new List<HotReloadCallSiteScanner.CompiledMethodIdentity>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            if (!ClassifyHits(directHits, isOneShotLifecycleCaller, roots, frontier, visited))
            {
                return null;
            }

            if (frontier.Count == 0)
            {
                return CompletedRootsOrNull(roots);
            }

            return WalkFrontier(frontier, roots, visited, scan, isOneShotLifecycleCaller);
        }

        private static List<OneShotCallerClassification> WalkFrontier(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> frontier,
            List<OneShotCallerClassification> roots,
            HashSet<string> visited,
            Func<string, HotReloadCallSiteScanner.CompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteScanner.CallSiteHit, bool> isOneShotLifecycleCaller)
        {
            int depth = 1;
            while (frontier.Count > 0)
            {
                if (depth >= MaxCallerDepth)
                {
                    return null;
                }

                List<HotReloadCallSiteScanner.CompiledMethodIdentity> nextFrontier =
                    new List<HotReloadCallSiteScanner.CompiledMethodIdentity>();
                if (!ScanFrontierLevel(frontier, nextFrontier, roots, visited, scan, isOneShotLifecycleCaller))
                {
                    return null;
                }

                frontier = nextFrontier;
                depth++;
            }

            return CompletedRootsOrNull(roots);
        }

        private static bool ScanFrontierLevel(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> frontier,
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> nextFrontier,
            List<OneShotCallerClassification> roots,
            HashSet<string> visited,
            Func<string, HotReloadCallSiteScanner.CompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteScanner.CallSiteHit, bool> isOneShotLifecycleCaller)
        {
            Dictionary<string, List<HotReloadCallSiteScanner.CompiledMethodIdentity>> groups =
                GroupFrontierByAssembly(frontier);
            foreach (KeyValuePair<string, List<HotReloadCallSiteScanner.CompiledMethodIdentity>> pair in groups)
            {
                HotReloadCallSiteScanner.CompiledMethodIdentity[] identities = pair.Value.ToArray();
                HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = scan(pair.Key, identities);
                if (result.MissingScanAssemblyNames.Count > 0)
                {
                    return false;
                }

                if (!AssignHitsForGroup(pair.Value, result.Hits, isOneShotLifecycleCaller, roots, nextFrontier, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AssignHitsForGroup(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> identities,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            Func<HotReloadCallSiteScanner.CallSiteHit, bool> isOneShotLifecycleCaller,
            List<OneShotCallerClassification> roots,
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> nextFrontier,
            HashSet<string> visited)
        {
            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity identity in identities)
            {
                string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    identity.TypeMetadataName,
                    identity.MethodName,
                    identity.ParameterTypeFullNames,
                    identity.GenericArity);
                List<HotReloadCallSiteScanner.CallSiteHit> hitsForIdentity =
                    new List<HotReloadCallSiteScanner.CallSiteHit>();
                foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
                {
                    if (hit.TargetMethodKey == targetKey)
                    {
                        hitsForIdentity.Add(hit);
                    }
                }

                if (hitsForIdentity.Count == 0)
                {
                    return false;
                }

                if (!ClassifyHits(hitsForIdentity, isOneShotLifecycleCaller, roots, nextFrontier, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ClassifyHits(
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            Func<HotReloadCallSiteScanner.CallSiteHit, bool> isOneShotLifecycleCaller,
            List<OneShotCallerClassification> roots,
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> frontier,
            HashSet<string> visited)
        {
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                if (hit.IsFunctionPointerLoad)
                {
                    return false;
                }

                if (!visited.Add(BuildVisitKey(hit)))
                {
                    continue;
                }

                if (isOneShotLifecycleCaller(hit))
                {
                    roots.Add(new OneShotCallerClassification(hit.CallerMethodName, true));
                    continue;
                }

                frontier.Add(ToIdentity(hit));
            }

            return true;
        }

        private static Dictionary<string, List<HotReloadCallSiteScanner.CompiledMethodIdentity>> GroupFrontierByAssembly(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> frontier)
        {
            Dictionary<string, List<HotReloadCallSiteScanner.CompiledMethodIdentity>> groups =
                new Dictionary<string, List<HotReloadCallSiteScanner.CompiledMethodIdentity>>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity identity in frontier)
            {
                if (!groups.TryGetValue(identity.AssemblyName, out List<HotReloadCallSiteScanner.CompiledMethodIdentity> group))
                {
                    group = new List<HotReloadCallSiteScanner.CompiledMethodIdentity>();
                    groups.Add(identity.AssemblyName, group);
                }

                group.Add(identity);
            }

            return groups;
        }

        private static string BuildVisitKey(HotReloadCallSiteScanner.CallSiteHit hit)
        {
            string callerKey = hit.CallerMethodKey;
            if (string.IsNullOrEmpty(callerKey))
            {
                callerKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    hit.CallerTypeMetadataName,
                    hit.CallerMethodName,
                    hit.CallerParameterTypeFullNames,
                    hit.CallerGenericArity);
            }

            return hit.CallerAssemblyName + VisitKeySeparator + callerKey;
        }

        private static HotReloadCallSiteScanner.CompiledMethodIdentity ToIdentity(
            HotReloadCallSiteScanner.CallSiteHit hit)
        {
            string[] parameterTypeFullNames = hit.CallerParameterTypeFullNames ?? Array.Empty<string>();
            return new HotReloadCallSiteScanner.CompiledMethodIdentity(
                hit.CallerAssemblyName,
                hit.CallerTypeMetadataName,
                hit.CallerMethodName,
                parameterTypeFullNames,
                hit.CallerGenericArity);
        }

        private static List<OneShotCallerClassification> CompletedRootsOrNull(
            List<OneShotCallerClassification> roots)
        {
            if (roots.Count == 0)
            {
                return null;
            }

            return roots;
        }
    }
}
