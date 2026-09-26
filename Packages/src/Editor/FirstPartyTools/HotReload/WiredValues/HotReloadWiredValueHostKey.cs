using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One added field of one host, named in a form that still matches after a scene reload has
    /// replaced the host object: the identity string a resolver built, plus the store field key.
    /// </summary>
    internal readonly struct HotReloadWiredValueHostKey : IEquatable<HotReloadWiredValueHostKey>
    {
        internal HotReloadWiredValueHostKey(string identity, string storeFieldKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(identity), "identity must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(storeFieldKey), "storeFieldKey must not be empty.");
            Identity = identity;
            StoreFieldKey = storeFieldKey;
        }

        internal string Identity { get; }

        internal string StoreFieldKey { get; }

        public bool Equals(HotReloadWiredValueHostKey other)
        {
            return string.Equals(Identity, other.Identity, StringComparison.Ordinal)
                && string.Equals(StoreFieldKey, other.StoreFieldKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is HotReloadWiredValueHostKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Identity) * 397)
                    ^ StringComparer.Ordinal.GetHashCode(StoreFieldKey);
            }
        }
    }
}
