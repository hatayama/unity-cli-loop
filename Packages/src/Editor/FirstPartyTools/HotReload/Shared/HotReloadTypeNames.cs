using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A type name in metadata form, the spelling Cecil and the transform worker produce, where a
    /// nested type reads Outer/Inner.
    /// </summary>
    /// <remarks>
    /// Why a type and not a string: metadata, reflection and display names differ only in the
    /// separator of a nested type, so a value that crosses from one world to the other silently
    /// stops matching. Holding the world in the type makes the crossing a call, not a habit.
    /// </remarks>
    internal readonly struct HotReloadMetadataTypeName : IEquatable<HotReloadMetadataTypeName>
    {
        public string Value { get; }

        public HotReloadMetadataTypeName(string value)
        {
            Debug.Assert(!string.IsNullOrEmpty(value), "A metadata type name must not be null or empty.");

            Value = value;
        }

        /// <summary>
        /// The same type as reflection spells it, so it can be compared with Type.FullName or
        /// handed to Assembly.GetType.
        /// </summary>
        public HotReloadReflectionTypeName ToReflectionName()
        {
            return new HotReloadReflectionTypeName(Value.Replace('/', '+'));
        }

        /// <summary>
        /// The same type as C# source spells it. Display only: this form is ambiguous, because a
        /// namespace segment and a nesting step become the same character.
        /// </summary>
        public string ToDisplayShortName()
        {
            return Value.Replace('/', '.');
        }

        public bool Equals(HotReloadMetadataTypeName other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is HotReloadMetadataTypeName other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }

    /// <summary>
    /// A type name in reflection form, the spelling Type.FullName produces, where a nested type
    /// reads Outer+Inner.
    /// </summary>
    internal readonly struct HotReloadReflectionTypeName : IEquatable<HotReloadReflectionTypeName>
    {
        public string Value { get; }

        public HotReloadReflectionTypeName(string value)
        {
            Debug.Assert(!string.IsNullOrEmpty(value), "A reflection type name must not be null or empty.");

            Value = value;
        }

        public bool Equals(HotReloadReflectionTypeName other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is HotReloadReflectionTypeName other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
