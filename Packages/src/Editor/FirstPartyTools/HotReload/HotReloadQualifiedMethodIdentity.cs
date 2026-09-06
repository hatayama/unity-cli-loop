using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Identifies a compiled method without conflating identical wire keys from different assemblies.
    /// </summary>
    internal readonly struct HotReloadQualifiedMethodIdentity : IEquatable<HotReloadQualifiedMethodIdentity>
    {
        internal string AssemblyName { get; }
        internal string MethodKey { get; }

        internal HotReloadQualifiedMethodIdentity(string assemblyName, string methodKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be null or empty.");
            AssemblyName = assemblyName;
            MethodKey = methodKey;
        }

        bool IEquatable<HotReloadQualifiedMethodIdentity>.Equals(HotReloadQualifiedMethodIdentity other)
        {
            return HasSameValue(other);
        }

        public override bool Equals(object obj)
        {
            return obj is HotReloadQualifiedMethodIdentity other && HasSameValue(other);
        }

        public override int GetHashCode()
        {
            int assemblyHashCode = StringComparer.Ordinal.GetHashCode(AssemblyName);
            int methodKeyHashCode = StringComparer.Ordinal.GetHashCode(MethodKey);
            return (assemblyHashCode * 397) ^ methodKeyHashCode;
        }

        private bool HasSameValue(HotReloadQualifiedMethodIdentity other)
        {
            return string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal)
                && string.Equals(MethodKey, other.MethodKey, StringComparison.Ordinal);
        }
    }
}
