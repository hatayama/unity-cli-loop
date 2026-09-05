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
        internal static HashSet<HotReloadQualifiedMethodIdentity> CollectCoveredMethodIdentities(
            string assemblyName,
            TransformWorkerEntryDto[] entries)
        {
            HashSet<HotReloadQualifiedMethodIdentity> coveredIdentities =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                coveredIdentities.Add(CreateEntryIdentity(assemblyName, entry));
            }

            return coveredIdentities;
        }

        internal static Dictionary<string, List<HotReloadQualifiedMethodIdentity>> CollectUncoveredCallersByTarget(
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            HashSet<HotReloadQualifiedMethodIdentity> coveredIdentities)
        {
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget =
                new Dictionary<string, List<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                HotReloadQualifiedMethodIdentity callerIdentity = CreateCallerIdentity(hit);
                if (coveredIdentities.Contains(callerIdentity))
                {
                    continue;
                }

                if (!uncoveredCallersByTarget.TryGetValue(
                        hit.TargetMethodKey,
                        out List<HotReloadQualifiedMethodIdentity> callers))
                {
                    callers = new List<HotReloadQualifiedMethodIdentity>();
                    uncoveredCallersByTarget.Add(hit.TargetMethodKey, callers);
                }

                if (!callers.Contains(callerIdentity))
                {
                    callers.Add(callerIdentity);
                }
            }

            return uncoveredCallersByTarget;
        }

        internal static List<string> CollectStaleSignatureWarnings(
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures,
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget)
        {
            List<string> warnings = new List<string>();
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedSignatures)
            {
                string methodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
                if (!uncoveredCallersByTarget.TryGetValue(
                        methodKey,
                        out List<HotReloadQualifiedMethodIdentity> callers)
                    || callers.Count == 0)
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.StaleSignatureCallersWarningFormat,
                        methodKey,
                        FormatUncoveredCallerMethodKeys(callers)));
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
            string assemblyName,
            TransformWorkerEntryDto[] entriesToPatch,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            HashSet<string> snapshotLabels)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(hits != null, "hits must not be null.");
            Debug.Assert(snapshotLabels != null, "snapshotLabels must not be null.");

            HashSet<HotReloadQualifiedMethodIdentity> entryIdentities =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            HashSet<string> appliedReplacementKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                entryIdentities.Add(new HotReloadQualifiedMethodIdentity(assemblyName, methodKey));
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
                    || !entryIdentities.Contains(CreateCallerIdentity(hit)))
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

        private static string FormatCallSiteCallerLabel(HotReloadCallSiteScanner.CallSiteHit hit)
        {
            Debug.Assert(hit != null, "hit must not be null.");
            return HotReloadMethodKeys.FormatMethodLabelParts(
                hit.CallerTypeMetadataName,
                hit.CallerMethodName,
                hit.CallerParameterTypeFullNames ?? Array.Empty<string>(),
                ReadGenericArityFromWireMethodKey(hit.CallerMethodKey, hit.CallerMethodName));
        }

        private static int ReadGenericArityFromWireMethodKey(string methodKey, string methodName)
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
            string assemblyName,
            IReadOnlyList<HotReloadQualifiedMethodIdentity> uncoveredCallerIdentities,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(uncoveredCallerIdentities != null, "uncoveredCallerIdentities must not be null.");
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            return AreAllUncoveredCallersInEditedFile(
                uncoveredCallerIdentities,
                CollectEditedFileMethodIdentities(assemblyName, entries, unchangedMethods));
        }

        /// <summary>
        /// Rechecks scan hits against the final apply set. Returns replacement keys that would
        /// still have uncovered compiled callers after isolation or a gate retry shrank entries.
        /// </summary>
        internal static List<string> FindSignatureChangeCoverageLosses(
            string assemblyName,
            TransformWorkerEntryDto[] entriesToPatch,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            IReadOnlyCollection<HotReloadQualifiedMethodIdentity> deletedCallerExemptions)
        {
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(hits != null, "hits must not be null.");
            Debug.Assert(deletedCallerExemptions != null, "deletedCallerExemptions must not be null.");

            HashSet<HotReloadQualifiedMethodIdentity> coveredIdentities =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                coveredIdentities.Add(CreateEntryIdentity(assemblyName, entry));
            }

            foreach (HotReloadQualifiedMethodIdentity deletedCallerExemption in deletedCallerExemptions)
            {
                coveredIdentities.Add(deletedCallerExemption);
            }

            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredCallersByTarget =
                CollectUncoveredCallersByTarget(hits, coveredIdentities);
            List<string> lostReplacementKeys = new List<string>();
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                if (!entry.replacesCompiledMethod)
                {
                    continue;
                }

                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(
                        methodKey,
                        out List<HotReloadQualifiedMethodIdentity> callers)
                    && callers.Count > 0)
                {
                    lostReplacementKeys.Add(methodKey);
                }
            }

            return lostReplacementKeys;
        }

        internal static HashSet<HotReloadQualifiedMethodIdentity> CollectEditedFileMethodIdentities(
            string assemblyName,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            HashSet<HotReloadQualifiedMethodIdentity> methodIdentities =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                methodIdentities.Add(CreateEntryIdentity(assemblyName, entry));
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in unchangedMethods)
            {
                methodIdentities.Add(CreateUnchangedMethodIdentity(assemblyName, unchanged));
            }

            return methodIdentities;
        }

        /// <summary>
        /// The edited-method keys of each file of the group, keyed by that file's project
        /// relative path.
        /// </summary>
        /// <remarks>
        /// Why per file: "every uncovered caller is in the edited file" is a statement about one
        /// file. A group run transforms several files at once, so a set built from all of them
        /// would call a caller in a sibling file a same-file caller.
        /// </remarks>
        internal static Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> CollectEditedFileMethodIdentitiesByFile(
            string assemblyName,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> methodIdentitiesByFile =
                new Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                AddMethodIdentityOfFile(
                    methodIdentitiesByFile,
                    entry.sourceProjectRelativePath,
                    CreateEntryIdentity(assemblyName, entry));
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in unchangedMethods)
            {
                AddMethodIdentityOfFile(
                    methodIdentitiesByFile,
                    unchanged.sourceProjectRelativePath,
                    CreateUnchangedMethodIdentity(assemblyName, unchanged));
            }

            return methodIdentitiesByFile;
        }

        private static void AddMethodIdentityOfFile(
            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> methodIdentitiesByFile,
            string projectRelativePath,
            HotReloadQualifiedMethodIdentity methodIdentity)
        {
            Debug.Assert(
                !string.IsNullOrEmpty(projectRelativePath),
                "A worker row must name the source file it came from.");
            if (!methodIdentitiesByFile.TryGetValue(
                    projectRelativePath,
                    out HashSet<HotReloadQualifiedMethodIdentity> methodIdentities))
            {
                methodIdentities = new HashSet<HotReloadQualifiedMethodIdentity>();
                methodIdentitiesByFile[projectRelativePath] = methodIdentities;
            }

            methodIdentities.Add(methodIdentity);
        }

        internal static bool AreAllUncoveredCallersInEditedFile(
            IReadOnlyList<HotReloadQualifiedMethodIdentity> uncoveredCallerIdentities,
            HashSet<HotReloadQualifiedMethodIdentity> editedFileMethodIdentities)
        {
            foreach (HotReloadQualifiedMethodIdentity callerIdentity in uncoveredCallerIdentities)
            {
                if (!editedFileMethodIdentities.Contains(callerIdentity))
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
        internal static string FormatUncoveredCallerShortNames(
            IReadOnlyList<HotReloadQualifiedMethodIdentity> uncoveredCallers)
        {
            if (uncoveredCallers == null || uncoveredCallers.Count == 0)
            {
                return string.Empty;
            }

            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadQualifiedMethodIdentity caller in uncoveredCallers)
            {
                string shortName = FormatCallerShortName(caller.MethodKey);
                if (string.IsNullOrEmpty(shortName) || !seen.Add(shortName))
                {
                    continue;
                }

                names.Add(shortName);
            }

            return string.Join(", ", names);
        }

        private static string FormatUncoveredCallerMethodKeys(
            IReadOnlyList<HotReloadQualifiedMethodIdentity> callers)
        {
            List<string> methodKeys = new List<string>(callers.Count);
            HashSet<string> seenMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadQualifiedMethodIdentity caller in callers)
            {
                if (!seenMethodKeys.Add(caller.MethodKey))
                {
                    continue;
                }

                methodKeys.Add(caller.MethodKey);
            }

            return string.Join(", ", methodKeys);
        }

        private static HotReloadQualifiedMethodIdentity CreateEntryIdentity(
            string assemblyName,
            TransformWorkerEntryDto entry)
        {
            return new HotReloadQualifiedMethodIdentity(
                assemblyName,
                HotReloadMethodKeys.BuildMethodKey(entry));
        }

        private static HotReloadQualifiedMethodIdentity CreateUnchangedMethodIdentity(
            string assemblyName,
            TransformWorkerUnchangedMethodDto unchanged)
        {
            string methodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                unchanged.typeMetadataName,
                unchanged.methodName,
                unchanged.parameterTypeFullNames,
                unchanged.genericArity);
            return new HotReloadQualifiedMethodIdentity(assemblyName, methodKey);
        }

        private static HotReloadQualifiedMethodIdentity CreateCallerIdentity(
            HotReloadCallSiteScanner.CallSiteHit hit)
        {
            Debug.Assert(hit != null, "hit must not be null.");
            return new HotReloadQualifiedMethodIdentity(hit.CallerAssemblyName, hit.CallerMethodKey);
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
