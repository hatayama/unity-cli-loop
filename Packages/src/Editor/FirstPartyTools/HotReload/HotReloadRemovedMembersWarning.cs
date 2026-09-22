using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats the removed-members warning, suppressing gated signature-change replacements.
    /// </summary>
    internal static class HotReloadRemovedMembersWarning
    {
        /// <summary>
        /// The removed-member names this run reports for one file, in the order the worker listed
        /// them. Empty when the file has none left to report after the gate.
        /// </summary>
        internal static IReadOnlyList<string> SelectDisplayedRemovedMemberNames(
            TransformWorkerRemovedMemberDto[] removedMembers,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (removedMembers == null || removedMembers.Length == 0)
            {
                return Array.Empty<string>();
            }

            HashSet<string> gatedKeys = new HashSet<string>(
                gatedReplacementMethodKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerRemovedMemberDto removed in removedMembers)
            {
                if (removed == null || string.IsNullOrEmpty(removed.name) || !seen.Add(removed.name))
                {
                    continue;
                }

                if (removed.kind == HotReloadConstants.RemovedMemberKindMethod
                    && ShouldSuppressGatedRemovedMethodName(
                        removed.name,
                        removedMethodSignatures,
                        gatedKeys))
                {
                    continue;
                }

                names.Add(removed.name);
            }

            return names;
        }

        /// <summary>The full warning, which every run that changes the reported set prints.</summary>
        internal static string FormatRemovedMembersWarning(IReadOnlyList<string> displayedNames)
        {
            Debug.Assert(displayedNames != null, "displayedNames must not be null.");
            Debug.Assert(displayedNames.Count > 0, "displayedNames must not be empty.");

            return string.Format(
                HotReloadConstants.RemovedMembersWarningFormat,
                string.Join(", ", displayedNames));
        }

        // Why the names are sorted here and not in the full warning: the continuation line stands
        // for a set, so two runs that list the same members in a different order must read the
        // same. The full warning reports this run's own listing order.
        internal static string FormatContinuingRemovedMembersWarning(IReadOnlyList<string> displayedNames)
        {
            Debug.Assert(displayedNames != null, "displayedNames must not be null.");
            Debug.Assert(displayedNames.Count > 0, "displayedNames must not be empty.");

            List<string> sortedNames = new List<string>(displayedNames);
            sortedNames.Sort(StringComparer.Ordinal);
            return string.Format(
                CultureInfo.InvariantCulture,
                HotReloadConstants.ContinuingRemovedMembersWarningFormat,
                sortedNames.Count,
                string.Join(", ", sortedNames));
        }

        // Why signature keys, not simple names: a gated replacement and a real deletion can
        // share a method name across types in the same file. Name-only suppression would
        // drop the deletion warning (fail-open).
        private static bool ShouldSuppressGatedRemovedMethodName(
            string methodName,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            HashSet<string> gatedReplacementMethodKeys)
        {
            if (removedMethodSignatures == null)
            {
                return false;
            }

            bool sawSignature = false;
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedMethodSignatures)
            {
                if (signature == null || signature.methodName != methodName)
                {
                    continue;
                }

                sawSignature = true;
                string signatureKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
                if (!gatedReplacementMethodKeys.Contains(signatureKey))
                {
                    return false;
                }
            }

            return sawSignature;
        }
    }
}
