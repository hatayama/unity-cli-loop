using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A wired value in the form that survives the host being replaced: plain values as they are,
    /// scene objects and assets as identities, and anything else as the reason it cannot come back.
    /// </summary>
    /// <remarks>
    /// Why scene objects are not held by reference: the scene reload destroys them too, so the
    /// restored field would point at a destroyed object.
    /// </remarks>
    internal sealed class HotReloadWiredValueDescriptor
    {
        private HotReloadWiredValueDescriptor(
            HotReloadWiredValueKind kind,
            object plainValue,
            string identity,
            string valueTypeName,
            string unrestorableReason)
        {
            Kind = kind;
            PlainValue = plainValue;
            Identity = identity;
            ValueTypeName = valueTypeName;
            UnrestorableReason = unrestorableReason;
        }

        internal HotReloadWiredValueKind Kind { get; }

        /// <summary>
        /// The value itself for <see cref="HotReloadWiredValueKind.Plain"/>; null is a value
        /// wired explicitly.
        /// </summary>
        internal object PlainValue { get; }

        /// <summary>
        /// Where a resolver finds a <see cref="HotReloadWiredValueKind.SceneObject"/> or
        /// <see cref="HotReloadWiredValueKind.Asset"/> value again.
        /// </summary>
        internal string Identity { get; }

        internal string ValueTypeName { get; }

        internal string UnrestorableReason { get; }

        internal static HotReloadWiredValueDescriptor Plain(object value)
        {
            return new HotReloadWiredValueDescriptor(
                HotReloadWiredValueKind.Plain, value, null, value?.GetType().FullName, null);
        }

        internal static HotReloadWiredValueDescriptor SceneObject(string identity, string valueTypeName)
        {
            Debug.Assert(!string.IsNullOrEmpty(identity), "identity must not be empty.");
            return new HotReloadWiredValueDescriptor(
                HotReloadWiredValueKind.SceneObject, null, identity, valueTypeName, null);
        }

        internal static HotReloadWiredValueDescriptor Asset(string identity, string valueTypeName)
        {
            Debug.Assert(!string.IsNullOrEmpty(identity), "identity must not be empty.");
            return new HotReloadWiredValueDescriptor(
                HotReloadWiredValueKind.Asset, null, identity, valueTypeName, null);
        }

        internal static HotReloadWiredValueDescriptor Unrestorable(string valueTypeName, string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty.");
            return new HotReloadWiredValueDescriptor(
                HotReloadWiredValueKind.Unrestorable, null, null, valueTypeName, reason);
        }
    }
}
