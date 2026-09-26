using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tells which active patches and added members live inside a type the domain introduced, and
    /// gives each change the Play-entry drop ledger identity that records it under that type.
    /// </summary>
    /// <remarks>
    /// Why the domain decides and not the method key: a key is only a label, and a compiled type
    /// declared in the owner file of an introduced one shares that file. The declaring assembly
    /// tells a patch on the introduced type apart, and the declaring type name tells an added
    /// member apart, because an introduced type is never partial and so declares every member in
    /// its owner file.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeChangeOwners
    {
        private readonly Dictionary<string, string> _typeIdentityByPatchedMethodKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _typeIdentityByAddedMemberKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Reads the owners from the types <paramref name="domain"/> holds now, which only a
        /// domain that has not yet unloaded them can answer.
        /// </summary>
        internal HotReloadIntroducedTypeChangeOwners(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null");
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors = domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < descriptors.Count; index++)
            {
                HotReloadIntroducedTypeDescriptor descriptor = descriptors[index];
                string typeIdentity = HotReloadPlayModeEntryDropIdentity.ForType(
                    descriptor.OriginalAssemblyName,
                    descriptor.MetadataName.Value);
                MapPatchedMethods(domain, descriptor, typeIdentity);
                MapAddedMembers(domain, descriptor, typeIdentity);
            }
        }

        /// <summary>The ledger identity of the active patch with this method key.</summary>
        internal string ToPatchIdentity(string methodKey)
        {
            return ToIdentity(_typeIdentityByPatchedMethodKey, methodKey);
        }

        /// <summary>The ledger identity of the active added member with this method key.</summary>
        internal string ToAddedMemberIdentity(string methodKey)
        {
            return ToIdentity(_typeIdentityByAddedMemberKey, methodKey);
        }

        private static string ToIdentity(Dictionary<string, string> typeIdentityByKey, string methodKey)
        {
            if (!typeIdentityByKey.TryGetValue(methodKey, out string typeIdentity))
            {
                return methodKey;
            }

            return HotReloadPlayModeEntryDropIdentity.ForMemberOfType(typeIdentity, methodKey);
        }

        private void MapPatchedMethods(
            HotReloadDomain domain,
            HotReloadIntroducedTypeDescriptor descriptor,
            string typeIdentity)
        {
            bool found = domain.IntroducedTypes.TryFindActiveDescriptor(
                descriptor,
                out HotReloadIntroducedTypeArtifact artifact);
            // Why skipped rather than thrown: Play entry must still record every change, and a
            // patch left unmapped is recorded under its own key, which is how it was before.
            Debug.Assert(found, "An active descriptor must resolve to the artifact that serves it.");
            if (!found)
            {
                return;
            }

            IReadOnlyList<MethodBase> methods = domain.ListActiveMethodsDeclaredBy(
                artifact.Assembly,
                descriptor.MetadataName.ToReflectionName());
            for (int index = 0; index < methods.Count; index++)
            {
                _typeIdentityByPatchedMethodKey[HotReloadMethodKeys.FormatMethodLabel(methods[index])] = typeIdentity;
            }
        }

        private void MapAddedMembers(
            HotReloadDomain domain,
            HotReloadIntroducedTypeDescriptor descriptor,
            string typeIdentity)
        {
            // A type with no owner file has no file generation its added members could sit in.
            if (string.IsNullOrEmpty(descriptor.OwnerProjectRelativePath))
            {
                return;
            }

            IReadOnlyList<HotReloadAddedMemberInfo> members =
                domain.DescribeAddedMembersOfFile(descriptor.OwnerProjectRelativePath);
            for (int index = 0; index < members.Count; index++)
            {
                if (!string.Equals(
                        members[index].DeclaringTypeMetadataName,
                        descriptor.MetadataName.Value,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _typeIdentityByAddedMemberKey[members[index].MethodKey] = typeIdentity;
            }
        }
    }
}
