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
            IReadOnlyDictionary<string, TransformWorkerEntryDto> replacementsByWireKey =
                HotReloadReplacedCompiledMethodEntries.IndexByReplacedWireKey(workerOutput.entries);

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

                if (!replacementsByWireKey.TryGetValue(wireKey, out TransformWorkerEntryDto replacement))
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
    }
}
