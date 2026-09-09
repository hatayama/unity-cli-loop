using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// The fixed entry point hot-reload shims reach the current domain's added field values
    /// through, and the store key format they share with the transform worker.
    /// </summary>
    /// <remarks>
    /// Why a static gateway rather than an injected value: the callers are emitted IL inside shim
    /// bodies, which can only call a static method. The values themselves live in
    /// <see cref="HotReloadAddedFieldValues"/>, owned by the domain and installed here by the
    /// composition root.
    /// </remarks>
    public static class HotReloadAddedFieldStore
    {
        // Keep in sync with TransformWorkerProgramMarker.AddedFieldKeySeparator.
        public const string FieldKeySeparator = "::";

        /// <summary>
        /// The values of the domain currently installed, or null while none is. Set by the
        /// composition root only.
        /// </summary>
        public static HotReloadAddedFieldValues Current { get; set; }

        /// <summary>
        /// Builds the store key "&lt;TypeMetadataName&gt;::&lt;fieldName&gt;".
        /// </summary>
        public static string FormatFieldKey(string typeMetadataName, string fieldName)
        {
            Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(fieldName), "fieldName must not be empty.");
            return typeMetadataName + FieldKeySeparator + fieldName;
        }

        /// <summary>
        /// Returns the stored instance field, running <paramref name="initializer"/> (or
        /// default(T) when it is null) on first access or after a stored type mismatch.
        /// </summary>
        public static T GetOrInit<T>(object instance, string fieldKey, Func<T> initializer)
        {
            // Why the slot is read once into a local: a shim body can run while the composition
            // root swaps domains, and reading twice could hit two different value sets.
            HotReloadAddedFieldValues values = Current;
            if (values == null)
            {
                return CreateValue(initializer);
            }

            return values.GetOrInit(instance, fieldKey, initializer);
        }

        public static void Set<T>(object instance, string fieldKey, T value)
        {
            HotReloadAddedFieldValues values = Current;
            // Why a dropped write is accepted: with no domain installed there is nothing whose
            // state the value could belong to, and the shim that wrote it cannot be patched in.
            values?.Set(instance, fieldKey, value);
        }

        /// <summary>
        /// Returns the stored static field, running <paramref name="initializer"/> (or
        /// default(T) when it is null) on first access or after a stored type mismatch.
        /// </summary>
        public static T GetOrInitStatic<T>(string fieldKey, Func<T> initializer)
        {
            HotReloadAddedFieldValues values = Current;
            if (values == null)
            {
                return CreateValue(initializer);
            }

            return values.GetOrInitStatic(fieldKey, initializer);
        }

        public static void SetStatic<T>(string fieldKey, T value)
        {
            HotReloadAddedFieldValues values = Current;
            values?.SetStatic(fieldKey, value);
        }

        /// <summary>
        /// Drops every instance and static entry of the installed domain.
        /// </summary>
        public static void Clear()
        {
            HotReloadAddedFieldValues values = Current;
            values?.Clear();
        }

        private static T CreateValue<T>(Func<T> initializer)
        {
            if (initializer == null)
            {
                return default;
            }

            return initializer();
        }
    }
}
