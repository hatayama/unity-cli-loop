using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// The values of one domain's hot-reload added fields. Compiled types cannot gain real fields,
    /// so shims store values here. Instance entries follow the host object's lifetime via
    /// ConditionalWeakTable; static entries live until <see cref="Clear"/> or domain reload.
    /// Editor main thread only. Thread safety is the caller's responsibility (the current
    /// hot-reload pipeline applies and clears on the main thread).
    /// </summary>
    public sealed class HotReloadAddedFieldValues
    {
        private ConditionalWeakTable<object, Dictionary<string, object>> _instanceTables =
            new ConditionalWeakTable<object, Dictionary<string, object>>();

        private readonly Dictionary<string, object> _staticValues =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Returns the stored instance field, running <paramref name="initializer"/> (or
        /// default(T) when it is null) on first access or after a stored type mismatch.
        /// Reference-type instances only. Struct hosts box on every access and would always
        /// reinitialize; the worker skips struct hosts.
        /// </summary>
        public T GetOrInit<T>(object instance, string fieldKey, Func<T> initializer)
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

        public void Set<T>(object instance, string fieldKey, T value)
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
        public T GetOrInitStatic<T>(string fieldKey, Func<T> initializer)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");

            if (_staticValues.TryGetValue(fieldKey, out object stored))
            {
                (bool readable, T existing) = TryReadAs<T>(stored);
                if (readable)
                {
                    return existing;
                }
            }

            T created = CreateValue(initializer);
            _staticValues[fieldKey] = created;
            return created;
        }

        public void SetStatic<T>(string fieldKey, T value)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");
            _staticValues[fieldKey] = value;
        }

        /// <summary>
        /// Drops every instance and static entry. Called from RevertAll; domain reload also drops
        /// the tables because the domain that holds them goes with it.
        /// </summary>
        public void Clear()
        {
            // Why replace rather than ConditionalWeakTable.Clear: replacing drops the old
            // table for GC even on profiles where Clear is missing, and matches static Clear.
            _instanceTables = new ConditionalWeakTable<object, Dictionary<string, object>>();
            _staticValues.Clear();
        }

        private Dictionary<string, object> GetOrCreateInstanceTable(object instance)
        {
            return _instanceTables.GetValue(
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
