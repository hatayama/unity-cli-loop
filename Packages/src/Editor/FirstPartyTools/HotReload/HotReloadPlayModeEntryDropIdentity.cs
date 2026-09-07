using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Play-entry drop ledger identity of an introduced type.
    /// </summary>
    internal static class HotReloadPlayModeEntryDropIdentity
    {
        // Why the prefix: types and methods share one ledger, and a method identity is a display
        // label ("Type.Method(...)"). Without a shape of its own a metadata name that happens to
        // look like a label would collide with a patched method and recover the wrong entry.
        private const string TypeIdentityPrefix = "type|";

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
    }
}
