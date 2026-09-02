using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records superseded compiled signatures from a worker output so --status can explain
    /// leftover Active rows after a signature change.
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
            TransformWorkerRemovedMemberDto[] removedMembers =
                workerOutput.removedMembers ?? Array.Empty<TransformWorkerRemovedMemberDto>();

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

                string oldKey = HotReloadPatcher.FormatMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                string newDisplayName = ResolveReplacementDisplayName(
                    signature,
                    entries,
                    removedMembers,
                    oldKey);
                HotReloadSupersededSignatureRegistry.Record(oldKey, newDisplayName);
            }
        }

        private static string ResolveReplacementDisplayName(
            TransformWorkerRemovedMethodSignatureDto signature,
            TransformWorkerEntryDto[] entries,
            TransformWorkerRemovedMemberDto[] removedMembers,
            string oldKey)
        {
            TransformWorkerEntryDto matchingEntry = FindMatchingEntry(signature, entries);
            if (matchingEntry != null)
            {
                return HotReloadPatcher.FormatMethodKeyParts(
                    matchingEntry.typeMetadataName,
                    matchingEntry.methodName,
                    matchingEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                    matchingEntry.genericArity);
            }

            string matchingRemovedName = FindMatchingRemovedMemberName(signature, removedMembers);
            if (matchingRemovedName != null)
            {
                return matchingRemovedName;
            }

            return oldKey;
        }

        private static TransformWorkerEntryDto FindMatchingEntry(
            TransformWorkerRemovedMethodSignatureDto signature,
            TransformWorkerEntryDto[] entries)
        {
            string oldType = NormalizeTypeName(signature.typeMetadataName);
            for (int index = 0; index < entries.Length; index++)
            {
                TransformWorkerEntryDto entry = entries[index];
                if (entry == null || entry.methodName != signature.methodName)
                {
                    continue;
                }

                if (NormalizeTypeName(entry.typeMetadataName) == oldType)
                {
                    return entry;
                }
            }

            return null;
        }

        private static string FindMatchingRemovedMemberName(
            TransformWorkerRemovedMethodSignatureDto signature,
            TransformWorkerRemovedMemberDto[] removedMembers)
        {
            for (int index = 0; index < removedMembers.Length; index++)
            {
                TransformWorkerRemovedMemberDto removed = removedMembers[index];
                if (removed == null || removed.name != signature.methodName)
                {
                    continue;
                }

                if (removed.kind == HotReloadConstants.RemovedMemberKindMethod)
                {
                    return removed.name;
                }
            }

            return null;
        }

        private static string NormalizeTypeName(string typeMetadataName)
        {
            if (string.IsNullOrEmpty(typeMetadataName))
            {
                return string.Empty;
            }

            return typeMetadataName.Replace('/', '+');
        }
    }
}
