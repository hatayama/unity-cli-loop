using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Play-entry drop ledger identities of an introduced type and of the patches and
    /// added members that live inside one.
    /// </summary>
    internal static class HotReloadPlayModeEntryDropIdentity
    {
        // Why the prefix: types and methods share one ledger, and a method identity is a display
        // label ("Type.Method(...)"). Without a shape of its own a metadata name that happens to
        // look like a label would collide with a patched method and recover the wrong entry.
        private const string TypeIdentityPrefix = "type|";

        // Why a change inside an introduced type carries that type's identity: the apply that
        // introduces the type again declares the edited bodies and the added members inside it
        // and reports no row of its own for them, so the type is the only thing that can say
        // they came back.
        private const string MemberIdentityPrefix = "member|";

        /// <summary>The ledger identity of a type this domain introduced.</summary>
        internal static string ForType(string originalAssemblyName, string metadataName)
        {
            Debug.Assert(!string.IsNullOrEmpty(originalAssemblyName), "originalAssemblyName must not be empty");
            Debug.Assert(!string.IsNullOrEmpty(metadataName), "metadataName must not be empty");
            // Why no MVID even though the registry identity carries one: the recovery side only
            // has the apply outcome, which carries no MVID, and the one route that changes a
            // module's MVID is a compile, which clears the whole ledger.
            return TypeIdentityPrefix + originalAssemblyName + "|" + metadataName;
        }

        /// <summary>
        /// The ledger identity of a patch or an added member inside a type this domain introduced,
        /// given that type's identity and the change's method key.
        /// </summary>
        internal static string ForMemberOfType(string typeIdentity, string methodKey)
        {
            Debug.Assert(
                typeIdentity != null && typeIdentity.StartsWith(TypeIdentityPrefix, StringComparison.Ordinal),
                "typeIdentity must be an identity ForType built");
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be empty");
            return BuildMemberPrefix(typeIdentity) + methodKey;
        }

        /// <summary>
        /// Answers whether a ledger identity names a change inside the type <paramref name="typeIdentity"/>
        /// identifies.
        /// </summary>
        internal static bool IsMemberOfType(string identity, string typeIdentity)
        {
            Debug.Assert(identity != null, "identity must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeIdentity), "typeIdentity must not be empty");
            // Why the separator ends the prefix: without it a type whose name starts with the same
            // text ("Fixture.T1Other" after "Fixture.T1") would lose its rows with this one.
            return identity.StartsWith(BuildMemberPrefix(typeIdentity), StringComparison.Ordinal);
        }

        private static string BuildMemberPrefix(string typeIdentity)
        {
            return MemberIdentityPrefix + typeIdentity + "|";
        }
    }
}
