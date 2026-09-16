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
                    ResolveIdentityAssemblyName(assemblyName, entry.homeAssemblyName),
                    HotReloadMethodKeys.BuildMethodKey(entry)));
            }

            foreach (TransformWorkerUnchangedMethodDto unchangedMethod in unchangedMethods)
            {
                identities.Add(new HotReloadQualifiedMethodIdentity(
                    ResolveIdentityAssemblyName(assemblyName, unchangedMethod.homeAssemblyName),
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

        // Why not always the target assembly: a row whose method lives in an introduced-type
        // artifact names that assembly, and a removed signature of the target must not be
        // considered still live because an artifact carries the same type and method.
        private static string ResolveIdentityAssemblyName(string assemblyName, string homeAssemblyName)
        {
            return string.IsNullOrEmpty(homeAssemblyName) ? assemblyName : homeAssemblyName;
        }
    }
}
