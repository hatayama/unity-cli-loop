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
            IReadOnlyList<HotReloadCallSiteHit> directHits,
            Func<string, HotReloadCompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteHit, bool> isOneShotLifecycleCaller)
        {
            Debug.Assert(directHits != null, "directHits must not be null.");
            Debug.Assert(scan != null, "scan must not be null.");
            Debug.Assert(isOneShotLifecycleCaller != null, "isOneShotLifecycleCaller must not be null.");

            List<OneShotCallerClassification> roots = new List<OneShotCallerClassification>();
            List<HotReloadCompiledMethodIdentity> frontier =
                new List<HotReloadCompiledMethodIdentity>();
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
            List<HotReloadCompiledMethodIdentity> frontier,
            List<OneShotCallerClassification> roots,
            HashSet<string> visited,
            Func<string, HotReloadCompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteHit, bool> isOneShotLifecycleCaller)
        {
            int depth = 1;
            while (frontier.Count > 0)
            {
                if (depth >= MaxCallerDepth)
                {
                    return null;
                }

                List<HotReloadCompiledMethodIdentity> nextFrontier =
                    new List<HotReloadCompiledMethodIdentity>();
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
            List<HotReloadCompiledMethodIdentity> frontier,
            List<HotReloadCompiledMethodIdentity> nextFrontier,
            List<OneShotCallerClassification> roots,
            HashSet<string> visited,
            Func<string, HotReloadCompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan,
            Func<HotReloadCallSiteHit, bool> isOneShotLifecycleCaller)
        {
            Dictionary<string, List<HotReloadCompiledMethodIdentity>> groups =
                GroupFrontierByAssembly(frontier);
            foreach (KeyValuePair<string, List<HotReloadCompiledMethodIdentity>> pair in groups)
            {
                HotReloadCompiledMethodIdentity[] identities = pair.Value.ToArray();
                HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = scan(pair.Key, identities);
                if (result.IsIncomplete)
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
            List<HotReloadCompiledMethodIdentity> identities,
            IReadOnlyList<HotReloadCallSiteHit> hits,
            Func<HotReloadCallSiteHit, bool> isOneShotLifecycleCaller,
            List<OneShotCallerClassification> roots,
            List<HotReloadCompiledMethodIdentity> nextFrontier,
            HashSet<string> visited)
        {
            foreach (HotReloadCompiledMethodIdentity identity in identities)
            {
                string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    identity.TypeMetadataName.Value,
                    identity.MethodName,
                    identity.ParameterTypeFullNames,
                    identity.GenericArity);
                List<HotReloadCallSiteHit> hitsForIdentity =
                    new List<HotReloadCallSiteHit>();
                foreach (HotReloadCallSiteHit hit in hits)
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
            IReadOnlyList<HotReloadCallSiteHit> hits,
            Func<HotReloadCallSiteHit, bool> isOneShotLifecycleCaller,
            List<OneShotCallerClassification> roots,
            List<HotReloadCompiledMethodIdentity> frontier,
            HashSet<string> visited)
        {
            foreach (HotReloadCallSiteHit hit in hits)
            {
                // A delegate target can run after its Awake registration, so a function-pointer
                // load cannot prove the target is called only from one-shot lifecycle methods.
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

        private static Dictionary<string, List<HotReloadCompiledMethodIdentity>> GroupFrontierByAssembly(
            List<HotReloadCompiledMethodIdentity> frontier)
        {
            Dictionary<string, List<HotReloadCompiledMethodIdentity>> groups =
                new Dictionary<string, List<HotReloadCompiledMethodIdentity>>(StringComparer.Ordinal);
            foreach (HotReloadCompiledMethodIdentity identity in frontier)
            {
                if (!groups.TryGetValue(identity.AssemblyName, out List<HotReloadCompiledMethodIdentity> group))
                {
                    group = new List<HotReloadCompiledMethodIdentity>();
                    groups.Add(identity.AssemblyName, group);
                }

                group.Add(identity);
            }

            return groups;
        }

        private static string BuildVisitKey(HotReloadCallSiteHit hit)
        {
            string callerKey = hit.CallerMethodKey;
            if (string.IsNullOrEmpty(callerKey))
            {
                callerKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    hit.CallerTypeMetadataName.Value,
                    hit.CallerMethodName,
                    hit.CallerParameterTypeFullNames,
                    hit.CallerGenericArity);
            }

            return hit.CallerAssemblyName + VisitKeySeparator + callerKey;
        }

        private static HotReloadCompiledMethodIdentity ToIdentity(
            HotReloadCallSiteHit hit)
        {
            string[] parameterTypeFullNames = hit.CallerParameterTypeFullNames ?? Array.Empty<string>();
            return new HotReloadCompiledMethodIdentity(
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
