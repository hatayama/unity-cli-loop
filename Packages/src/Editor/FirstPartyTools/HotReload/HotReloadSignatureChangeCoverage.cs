using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Call-site coverage, same-file caller checks, and signature-change notice formatting.
    /// </summary>
    internal static class HotReloadSignatureChangeCoverage
    {
        internal static HashSet<string> CollectCoveredMethodKeys(
            TransformWorkerEntryDto[] entries,
            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets)
        {
            HashSet<string> coveredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                coveredKeys.Add(HotReloadWireMethodKeys.BuildMethodKey(entry));
            }

            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity target in targets)
            {
                // Why include removed-signature targets: a deleted helper that called the
                // replaced method is already stale (removed-members warning). Treating that
                // corpse as uncovered would gate a same-file helper-delete + return-type
                // change, which is still a consistent old world. Fail-closed only for live
                // compiled callers that will keep invoking the old method.
                coveredKeys.Add(
                    HotReloadWireMethodKeys.BuildMethodKeyParts(
                        target.TypeMetadataName,
                        target.MethodName,
                        target.ParameterTypeFullNames,
                        target.GenericArity));
            }

            return coveredKeys;
        }

        internal static Dictionary<string, List<string>> CollectUncoveredCallersByTarget(
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            HashSet<string> coveredKeys)
        {
            Dictionary<string, List<string>> uncoveredCallersByTarget =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                if (coveredKeys.Contains(hit.CallerMethodKey))
                {
                    continue;
                }

                if (!uncoveredCallersByTarget.TryGetValue(hit.TargetMethodKey, out List<string> callers))
                {
                    callers = new List<string>();
                    uncoveredCallersByTarget.Add(hit.TargetMethodKey, callers);
                }

                if (!callers.Contains(hit.CallerMethodKey))
                {
                    callers.Add(hit.CallerMethodKey);
                }
            }

            return uncoveredCallersByTarget;
        }

        internal static List<string> CollectStaleSignatureWarnings(
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures,
            Dictionary<string, List<string>> uncoveredCallersByTarget)
        {
            List<string> warnings = new List<string>();
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedSignatures)
            {
                string methodKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
                if (!uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    || callers.Count == 0)
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.StaleSignatureCallersWarningFormat,
                        methodKey,
                        string.Join(", ", callers)));
            }

            return warnings;
        }

        // Why only already-patched callers of applied replacements: a caller the user
        // edited this run is obvious in Methods. The non-obvious case is a caller that
        // was already patched at run start and was re-entered only to satisfy the
        // signature-change gate. Hits still list gated replacements and rename/param
        // removals, so restrict to replacements that remain in the apply set. Known
        // limits: an already-patched caller that is also edited this run is
        // over-reported (the text is still true); a caller that stayed Skipped and
        // drifted is missed; constructed generics can miss when the label space differs
        // from the wire key (same constraint as IsActiveMember).
        internal static void AppendSignatureChangeCallersRepatchedWarnings(
            List<string> warnings,
            TransformWorkerEntryDto[] entriesToPatch,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            HashSet<string> snapshotLabels)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(hits != null, "hits must not be null.");
            Debug.Assert(snapshotLabels != null, "snapshotLabels must not be null.");

            HashSet<string> entryKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> appliedReplacementKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                entryKeys.Add(methodKey);
                if (entry.replacesCompiledMethod)
                {
                    appliedReplacementKeys.Add(methodKey);
                }
            }

            Dictionary<string, List<string>> callerLabelsByOldSignature =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                if (hit == null
                    || !appliedReplacementKeys.Contains(hit.TargetMethodKey)
                    || !entryKeys.Contains(hit.CallerMethodKey))
                {
                    continue;
                }

                string callerLabel = FormatCallSiteCallerLabel(hit);
                if (!snapshotLabels.Contains(callerLabel))
                {
                    continue;
                }

                if (!callerLabelsByOldSignature.TryGetValue(
                    hit.TargetMethodKey,
                    out List<string> callerLabels))
                {
                    callerLabels = new List<string>();
                    callerLabelsByOldSignature.Add(hit.TargetMethodKey, callerLabels);
                }

                if (!callerLabels.Contains(callerLabel))
                {
                    callerLabels.Add(callerLabel);
                }
            }

            foreach (KeyValuePair<string, List<string>> pair in callerLabelsByOldSignature)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.SignatureChangeCallersRepatchedNoticeFormat,
                        pair.Key,
                        string.Join(", ", pair.Value)));
            }
        }

        internal static string FormatCallSiteCallerLabel(HotReloadCallSiteScanner.CallSiteHit hit)
        {
            Debug.Assert(hit != null, "hit must not be null.");
            return HotReloadPatcher.FormatMethodKeyParts(
                hit.CallerTypeMetadataName,
                hit.CallerMethodName,
                hit.CallerParameterTypeFullNames ?? Array.Empty<string>(),
                ReadGenericArityFromWireMethodKey(hit.CallerMethodKey, hit.CallerMethodName));
        }

        internal static int ReadGenericArityFromWireMethodKey(string methodKey, string methodName)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");

            string arityPrefix = "::" + methodName + "`";
            int arityPrefixIndex = methodKey.IndexOf(arityPrefix, StringComparison.Ordinal);
            if (arityPrefixIndex < 0)
            {
                return 0;
            }

            int arityStart = arityPrefixIndex + arityPrefix.Length;
            int arityEnd = methodKey.IndexOf('(', arityStart);
            Debug.Assert(arityEnd > arityStart, "wire method key arity must precede '('.");
            return int.Parse(
                methodKey.Substring(arityStart, arityEnd - arityStart),
                CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when every uncovered caller key is an apply entry or unchanged method in the
        /// edited file. A same-type caller that the worker did not see (other partial file,
        /// ctor, or another assembly) must return false so the compile-only wording is used.
        /// </summary>
        internal static bool AreUncoveredCallersInEditedFile(
            IReadOnlyList<string> uncoveredCallerKeys,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(uncoveredCallerKeys != null, "uncoveredCallerKeys must not be null.");
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            return AreAllUncoveredCallersInEditedFile(
                uncoveredCallerKeys,
                CollectEditedFileMethodKeys(entries, unchangedMethods));
        }

        /// <summary>
        /// Rechecks scan hits against the final apply set. Returns replacement keys that would
        /// still have uncovered compiled callers after isolation or a gate retry shrank entries.
        /// </summary>
        internal static List<string> FindSignatureChangeCoverageLosses(
            TransformWorkerEntryDto[] entriesToPatch,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            IReadOnlyList<string> scanTargetKeys)
        {
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(hits != null, "hits must not be null.");
            Debug.Assert(scanTargetKeys != null, "scanTargetKeys must not be null.");

            HashSet<string> coveredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                coveredKeys.Add(HotReloadWireMethodKeys.BuildMethodKey(entry));
            }

            foreach (string targetKey in scanTargetKeys)
            {
                coveredKeys.Add(targetKey);
            }

            Dictionary<string, List<string>> uncoveredCallersByTarget =
                CollectUncoveredCallersByTarget(hits, coveredKeys);
            List<string> lostReplacementKeys = new List<string>();
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                if (!entry.replacesCompiledMethod)
                {
                    continue;
                }

                string methodKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    && callers.Count > 0)
                {
                    lostReplacementKeys.Add(methodKey);
                }
            }

            return lostReplacementKeys;
        }

        internal static List<string> CollectScanTargetKeys(
            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets)
        {
            List<string> keys = new List<string>(targets.Length);
            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity target in targets)
            {
                keys.Add(
                    HotReloadWireMethodKeys.BuildMethodKeyParts(
                        target.TypeMetadataName,
                        target.MethodName,
                        target.ParameterTypeFullNames,
                        target.GenericArity));
            }

            return keys;
        }

        internal static HashSet<string> CollectEditedFileMethodKeys(
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            HashSet<string> methodKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                methodKeys.Add(HotReloadWireMethodKeys.BuildMethodKey(entry));
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in unchangedMethods)
            {
                methodKeys.Add(
                    HotReloadWireMethodKeys.BuildMethodKeyParts(
                        unchanged.typeMetadataName,
                        unchanged.methodName,
                        unchanged.parameterTypeFullNames,
                        unchanged.genericArity));
            }

            return methodKeys;
        }

        internal static bool AreAllUncoveredCallersInEditedFile(
            IReadOnlyList<string> uncoveredCallerKeys,
            HashSet<string> editedFileMethodKeys)
        {
            foreach (string callerKey in uncoveredCallerKeys)
            {
                if (!editedFileMethodKeys.Contains(callerKey))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Derives Type.Caller short names from wire keys (Ns.Type::Caller(params)), in list
        /// order, de-duplicated. Nested type '/' is normalized to '.' and only the last type
        /// segment is kept.
        /// </summary>
        internal static string FormatUncoveredCallerShortNames(IReadOnlyList<string> uncoveredCallers)
        {
            if (uncoveredCallers == null || uncoveredCallers.Count == 0)
            {
                return string.Empty;
            }

            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string wireKey in uncoveredCallers)
            {
                string shortName = FormatCallerShortName(wireKey);
                if (string.IsNullOrEmpty(shortName) || !seen.Add(shortName))
                {
                    continue;
                }

                names.Add(shortName);
            }

            return string.Join(", ", names);
        }

        internal static string FormatCallerShortName(string wireKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(wireKey), "wireKey must not be empty.");
            int separatorIndex = wireKey.IndexOf("::", StringComparison.Ordinal);
            Debug.Assert(separatorIndex >= 0, "wireKey must contain '::'.");
            string typePart = wireKey.Substring(0, separatorIndex).Replace('/', '.');
            int lastDot = typePart.LastIndexOf('.');
            string typeName = lastDot >= 0 ? typePart.Substring(lastDot + 1) : typePart;
            string methodAndParams = wireKey.Substring(separatorIndex + 2);
            int parenIndex = methodAndParams.IndexOf('(');
            string methodName = parenIndex >= 0
                ? methodAndParams.Substring(0, parenIndex)
                : methodAndParams;
            return typeName + "." + methodName;
        }
    }
}
