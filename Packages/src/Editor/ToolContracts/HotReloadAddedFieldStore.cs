using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Side table for hot-reload added fields. Compiled types cannot gain real fields, so
    /// shims store values here. Instance entries follow the host object's lifetime via
    /// ConditionalWeakTable; static entries live until Clear or domain reload.
    /// Editor main thread only. Thread safety is the caller's responsibility (the current
    /// hot-reload pipeline applies and clears on the main thread).
    /// </summary>
    public static class HotReloadAddedFieldStore
    {
        // Keep in sync with TransformWorkerProgramMarker.AddedFieldKeySeparator.
        public const string FieldKeySeparator = "::";

        private static ConditionalWeakTable<object, Dictionary<string, object>> InstanceTables =
            new ConditionalWeakTable<object, Dictionary<string, object>>();

        private static readonly Dictionary<string, object> StaticValues =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Builds the store key "<TypeMetadataName>::<fieldName>".
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
        /// Reference-type instances only. Struct hosts box on every access and would always
        /// reinitialize; the worker (PR-4) skips struct hosts.
        /// </summary>
        public static T GetOrInit<T>(object instance, string fieldKey, Func<T> initializer)
        {
            Debug.Assert(instance != null, "instance must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");

            Dictionary<string, object> fields = GetOrCreateInstanceTable(instance);
            if (fields.TryGetValue(fieldKey, out object stored))
            {
                (bool readable, T existing) = TryReadAs<T>(stored);
                if (readable)
                {
                    return existing;
                }
            }

            T created = CreateValue(initializer);
            fields[fieldKey] = created;
            return created;
        }

        public static void Set<T>(object instance, string fieldKey, T value)
        {
            Debug.Assert(instance != null, "instance must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");

            Dictionary<string, object> fields = GetOrCreateInstanceTable(instance);
            fields[fieldKey] = value;
        }

        /// <summary>
        /// Returns the stored static field, running <paramref name="initializer"/> (or
        /// default(T) when it is null) on first access or after a stored type mismatch.
        /// </summary>
        public static T GetOrInitStatic<T>(string fieldKey, Func<T> initializer)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");

            if (StaticValues.TryGetValue(fieldKey, out object stored))
            {
                (bool readable, T existing) = TryReadAs<T>(stored);
                if (readable)
                {
                    return existing;
                }
            }

            T created = CreateValue(initializer);
            StaticValues[fieldKey] = created;
            return created;
        }

        public static void SetStatic<T>(string fieldKey, T value)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");
            StaticValues[fieldKey] = value;
        }

        /// <summary>
        /// Drops every instance and static entry. Called from RevertAll; domain reload also
        /// drops the tables because they are static.
        /// </summary>
        public static void Clear()
        {
            // Why replace rather than ConditionalWeakTable.Clear: replacing drops the old
            // table for GC even on profiles where Clear is missing, and matches static Clear.
            InstanceTables = new ConditionalWeakTable<object, Dictionary<string, object>>();
            StaticValues.Clear();
        }

        private static Dictionary<string, object> GetOrCreateInstanceTable(object instance)
        {
            return InstanceTables.GetValue(
                instance,
                _ => new Dictionary<string, object>(StringComparer.Ordinal));
        }

        private static T CreateValue<T>(Func<T> initializer)
        {
            if (initializer == null)
            {
                return default;
            }

            return initializer();
        }

        private static (bool Readable, T Value) TryReadAs<T>(object stored)
        {
            if (stored is T typed)
            {
                return (true, typed);
            }

            // Why treat null as a hit for reference T and Nullable<T>: Set stores a null
            // dictionary value, and `is T` is false for null. Non-nullable value types are
            // boxed and never stored as null.
            if (stored == null
                && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
            {
                return (true, default);
            }

            return (false, default);
        }
    }
}
