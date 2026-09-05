using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records superseded compiled signatures from the entries a file actually applied, so
    /// --status can explain leftover Active rows after a return-type change.
    /// </summary>
    internal static class HotReloadSupersededSignatureRecorder
    {
        /// <remarks>
        /// Why the applied entries (not the run's whole output): a group can apply one file while
        /// isolation drops another, and a replacement that was never patched must not claim the
        /// compiled signature it would have superseded.
        /// </remarks>
        public static void RecordFromAppliedEntries(
            IReadOnlyList<TransformWorkerEntryDto> appliedEntries,
            IReadOnlyList<TransformWorkerRemovedMethodSignatureDto> removedMethodSignatures,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (appliedEntries == null || removedMethodSignatures == null)
            {
                return;
            }

            HashSet<string> gatedKeys = new HashSet<string>(
                gatedReplacementMethodKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            IReadOnlyDictionary<string, TransformWorkerEntryDto> replacementsByWireKey =
                HotReloadReplacedCompiledMethodEntries.IndexByReplacedWireKey(appliedEntries);

            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedMethodSignatures)
            {
                if (signature == null || string.IsNullOrEmpty(signature.methodName))
                {
                    continue;
                }

                string[] parameterTypeFullNames =
                    signature.parameterTypeFullNames ?? Array.Empty<string>();
                string wireKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                if (gatedKeys.Contains(wireKey))
                {
                    continue;
                }

                if (!replacementsByWireKey.TryGetValue(wireKey, out TransformWorkerEntryDto replacement))
                {
                    continue;
                }

                string oldKey = HotReloadMethodKeys.FormatMethodLabelParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                string newDisplayName = HotReloadMethodKeys.FormatMethodLabelParts(
                    replacement.typeMetadataName,
                    replacement.methodName,
                    replacement.parameterTypeFullNames ?? Array.Empty<string>(),
                    replacement.genericArity);
                HotReloadSupersededSignatureRegistry.Record(oldKey, newDisplayName);
            }
        }
    }
}
