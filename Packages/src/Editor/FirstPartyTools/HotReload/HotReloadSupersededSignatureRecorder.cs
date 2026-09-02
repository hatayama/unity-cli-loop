using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records superseded compiled signatures from a worker output so --status can explain
    /// leftover Active rows after a return-type change.
    /// </summary>
    internal static class HotReloadSupersededSignatureRecorder
    {
        public static void RecordFromWorkerOutput(
            TransformWorkerOutputDto workerOutput,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (workerOutput?.removedMethodSignatures == null)
            {
                return;
            }

            HashSet<string> gatedKeys = new HashSet<string>(
                gatedReplacementMethodKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            TransformWorkerEntryDto[] entries =
                workerOutput.entries ?? Array.Empty<TransformWorkerEntryDto>();

            foreach (TransformWorkerRemovedMethodSignatureDto signature in workerOutput.removedMethodSignatures)
            {
                if (signature == null || string.IsNullOrEmpty(signature.methodName))
                {
                    continue;
                }

                string[] parameterTypeFullNames =
                    signature.parameterTypeFullNames ?? Array.Empty<string>();
                string wireKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                if (gatedKeys.Contains(wireKey))
                {
                    continue;
                }

                TransformWorkerEntryDto replacement = FindReplacingCompiledMethodEntry(
                    wireKey,
                    entries);
                if (replacement == null)
                {
                    continue;
                }

                string oldKey = HotReloadPatcher.FormatMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                string newDisplayName = HotReloadPatcher.FormatMethodKeyParts(
                    replacement.typeMetadataName,
                    replacement.methodName,
                    replacement.parameterTypeFullNames ?? Array.Empty<string>(),
                    replacement.genericArity);
                HotReloadSupersededSignatureRegistry.Record(oldKey, newDisplayName);
            }
        }

        private static TransformWorkerEntryDto FindReplacingCompiledMethodEntry(
            string removedWireKey,
            TransformWorkerEntryDto[] entries)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                TransformWorkerEntryDto entry = entries[index];
                if (entry == null || !entry.replacesCompiledMethod)
                {
                    continue;
                }

                string entryWireKey = HotReloadWireMethodKeys.BuildMethodKey(entry);
                if (entryWireKey == removedWireKey)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
