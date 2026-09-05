using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identifies removed callers that cannot remain compiled after the initial worker output.
    /// </summary>
    internal static class HotReloadDeletedCallerExemptions
    {
        internal static HashSet<HotReloadQualifiedMethodIdentity> Collect(
            string assemblyName,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods,
            TransformWorkerSkippedDto[] skipped,
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures)
        {
            if (ContainsUnknownSkippedMethodKey(skipped))
            {
                // A skipped row without an identity might be a live compiled caller. Do not
                // exempt any deletion when that fact cannot be established from worker output.
                return new HashSet<HotReloadQualifiedMethodIdentity>();
            }

            HashSet<HotReloadQualifiedMethodIdentity> sourceLiveIdentities =
                CollectSourceLiveIdentities(assemblyName, entries, unchangedMethods, skipped);
            HashSet<HotReloadQualifiedMethodIdentity> deletedCallerExemptions =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            foreach (TransformWorkerRemovedMethodSignatureDto removedSignature in removedSignatures)
            {
                HotReloadQualifiedMethodIdentity identity = new HotReloadQualifiedMethodIdentity(
                    assemblyName,
                    HotReloadMethodKeys.BuildMethodKeyParts(
                        removedSignature.typeMetadataName,
                        removedSignature.methodName,
                        removedSignature.parameterTypeFullNames,
                        removedSignature.genericArity));
                if (!sourceLiveIdentities.Contains(identity))
                {
                    deletedCallerExemptions.Add(identity);
                }
            }

            return deletedCallerExemptions;
        }

        private static bool ContainsUnknownSkippedMethodKey(TransformWorkerSkippedDto[] skipped)
        {
            foreach (TransformWorkerSkippedDto skippedMethod in skipped)
            {
                if (string.IsNullOrEmpty(skippedMethod.methodKey))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<HotReloadQualifiedMethodIdentity> CollectSourceLiveIdentities(
            string assemblyName,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods,
            TransformWorkerSkippedDto[] skipped)
        {
            HashSet<HotReloadQualifiedMethodIdentity> identities =
                new HashSet<HotReloadQualifiedMethodIdentity>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                identities.Add(new HotReloadQualifiedMethodIdentity(
                    assemblyName,
                    HotReloadMethodKeys.BuildMethodKey(entry)));
            }

            foreach (TransformWorkerUnchangedMethodDto unchangedMethod in unchangedMethods)
            {
                identities.Add(new HotReloadQualifiedMethodIdentity(
                    assemblyName,
                    HotReloadMethodKeys.BuildMethodKeyParts(
                        unchangedMethod.typeMetadataName,
                        unchangedMethod.methodName,
                        unchangedMethod.parameterTypeFullNames,
                        unchangedMethod.genericArity)));
            }

            foreach (TransformWorkerSkippedDto skippedMethod in skipped)
            {
                identities.Add(new HotReloadQualifiedMethodIdentity(assemblyName, skippedMethod.methodKey));
            }

            return identities;
        }
    }
}
