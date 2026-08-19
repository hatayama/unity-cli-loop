using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats the removed-members warning, suppressing gated signature-change replacements.
    /// </summary>
    internal static class HotReloadRemovedMembersWarning
    {
        internal static string FormatRemovedMembersWarning(
            TransformWorkerRemovedMemberDto[] removedMembers,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (removedMembers == null || removedMembers.Length == 0)
            {
                return null;
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

            if (names.Count == 0)
            {
                return null;
            }

            return string.Format(
                HotReloadConstants.RemovedMembersWarningFormat,
                string.Join(", ", names));
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
                string signatureKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
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
