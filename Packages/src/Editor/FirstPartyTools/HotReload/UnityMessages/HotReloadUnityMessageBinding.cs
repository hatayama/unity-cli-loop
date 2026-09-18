using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything one proxy type needs to forward the added Unity messages of one target type,
    /// slot by slot, in the order the proxy's emitted methods refer to them.
    /// </summary>
    /// <remarks>
    /// Why the type is public while every member stays internal: the emitted proxy type loads a
    /// static field of this type before calling Forward, and that IL is checked for accessibility
    /// like compiled code. Nothing outside this assembly reads a slot, so the members do not follow.
    /// The instance is immutable because one binding is shared by every proxy of its type and is
    /// read from Unity's message dispatch while the reconciler builds the next one.
    /// </remarks>
    public sealed class HotReloadUnityMessageBinding
    {
        private readonly string[] _names;
        private readonly bool[] _gated;
        private readonly Action<MonoBehaviour, object[]>[] _forwarders;
        private readonly Type[][] _parameterTypes;

        internal HotReloadUnityMessageBinding(
            Type targetType,
            string[] names,
            bool[] gated,
            Action<MonoBehaviour, object[]>[] forwarders,
            Type[][] parameterTypes)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            Debug.Assert(names != null, "names must not be null.");
            Debug.Assert(gated != null && gated.Length == names.Length, "gated must match names.");
            Debug.Assert(
                forwarders != null && forwarders.Length == names.Length,
                "forwarders must match names.");
            Debug.Assert(
                parameterTypes != null && parameterTypes.Length == names.Length,
                "parameterTypes must match names.");

            TargetType = targetType;
            _names = names;
            _gated = gated;
            _forwarders = forwarders;
            _parameterTypes = parameterTypes;
        }

        /// <summary>The component type whose added messages these slots belong to.</summary>
        internal Type TargetType { get; }

        internal int Count => _names.Length;

        /// <summary>The Unity message name the proxy declares for this slot.</summary>
        internal string GetName(int slot)
        {
            return _names[slot];
        }

        /// <summary>The message's parameters, without the shim's receiver parameter.</summary>
        internal Type[] GetParameterTypes(int slot)
        {
            return _parameterTypes[slot];
        }

        /// <summary>
        /// Whether this slot is only forwarded while the target itself is active and enabled.
        /// </summary>
        internal bool IsGated(int slot)
        {
            return _gated[slot];
        }

        /// <summary>Calls the shim of this slot on <paramref name="target"/>.</summary>
        internal void Invoke(int slot, MonoBehaviour target, object[] args)
        {
            _forwarders[slot](target, args);
        }
    }
}
