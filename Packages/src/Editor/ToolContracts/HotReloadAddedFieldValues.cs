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
    /// A read whose restore failed keeps a pending slot and asks the restorer again whenever its
    /// restore generation moved, until a value is found or the field is written.
    /// </summary>
    public sealed class HotReloadAddedFieldValues
    {
        private ConditionalWeakTable<object, Dictionary<string, object>> _instanceTables =
            new ConditionalWeakTable<object, Dictionary<string, object>>();

        private readonly Dictionary<string, object> _staticValues =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Hands a wired value back to a host that has no slot yet, so a value wired into the
        /// instance a scene reload destroyed reaches its replacement. Null when nothing restores.
        /// Survives <see cref="Clear"/>: forgetting what was wired is the restorer's own job.
        /// </summary>
        public IHotReloadWiredValuePersistence Restorer { get; set; }

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
                // Checked before TryReadAs: it reads a null slot as a hit for reference types.
                if (stored is PendingRestoreValue pending)
                {
                    stored = RetryPendingRestore(instance, fieldKey, fields, pending, typeof(T));
                }

                (bool readable, T existing) = TryReadAs<T>(stored);
                if (readable)
                {
                    return existing;
                }
            }
            else if (Restorer != null)
            {
                if (!Restorer.TryRestore(instance, fieldKey, out object restored))
                {
                    T fallback = CreateValue(initializer);
                    fields[fieldKey] = new PendingRestoreValue(fallback);
                    return fallback;
                }

                (bool readable, T restoredAs) = TryReadAs<T>(restored);
                if (readable)
                {
                    fields[fieldKey] = restored;
                    return restoredAs;
                }

                // A restored value of another type is not kept: the initializer below overwrites it.
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
        /// Reports whether an instance field holds a stored value, and what it is, without
        /// creating the slot. A field nothing has written yet stays unwritten, so the reading shim
        /// still runs its initializer. The one slot it does create is a value <see cref="Restorer"/>
        /// hands back that <paramref name="fieldType"/> can hold, which then reads as stored. A slot
        /// whose restore is pending is retried first and reports the restored value, or the
        /// initializer's value the field reads until then.
        /// </summary>
        public bool TryGet(object instance, string fieldKey, Type fieldType, out object value)
        {
            Debug.Assert(instance != null, "instance must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");
            Debug.Assert(fieldType != null, "fieldType must not be null.");

            if (_instanceTables.TryGetValue(instance, out Dictionary<string, object> fields)
                && fields.TryGetValue(fieldKey, out value))
            {
                if (value is PendingRestoreValue pending)
                {
                    value = RetryPendingRestore(instance, fieldKey, fields, pending, fieldType);
                }

                return true;
            }

            value = null;
            if (Restorer == null || !Restorer.TryRestore(instance, fieldKey, out object restored))
            {
                return false;
            }

            // Why the reader's rule: GetOrInit replaces a value the field's type cannot hold with
            // the initializer's, so reporting it here would name a value the shim never uses.
            if (!IsReadableAs(restored, fieldType))
            {
                return false;
            }

            GetOrCreateInstanceTable(instance)[fieldKey] = restored;
            value = restored;
            return true;
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
        /// The static counterpart of <see cref="TryGet"/>: reports a stored static value without
        /// creating the slot.
        /// </summary>
        public bool TryGetStatic(string fieldKey, out object value)
        {
            Debug.Assert(!string.IsNullOrEmpty(fieldKey), "fieldKey must not be empty.");
            return _staticValues.TryGetValue(fieldKey, out value);
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

        // Returns the value the slot reads as now. A pending slot is only created while Restorer is
        // set, but a later assignment can clear it, so a missing restorer keeps the fallback.
        // Why a restored value of another type settles the slot on the fallback: retrying would
        // bring back the same unreadable value on every generation.
        private object RetryPendingRestore(
            object instance,
            string fieldKey,
            Dictionary<string, object> fields,
            PendingRestoreValue pending,
            Type fieldType)
        {
            if (Restorer == null
                || !Restorer.TryRestoreAgain(instance, fieldKey, ref pending.LastAttemptGeneration, out object restored))
            {
                return pending.FallbackValue;
            }

            object settled = IsReadableAs(restored, fieldType) ? restored : pending.FallbackValue;
            fields[fieldKey] = settled;
            return settled;
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

        // A slot whose restore failed: it reads as FallbackValue, the initializer's value, until a
        // retry restores the wired value. LastAttemptGeneration belongs to the restorer, which
        // stores into it the generation of its last retry.
        private sealed class PendingRestoreValue
        {
            internal readonly object FallbackValue;
            internal int LastAttemptGeneration;

            internal PendingRestoreValue(object fallbackValue)
            {
                FallbackValue = fallbackValue;
            }
        }

        // The rule of TryReadAs<T>, for a type known only at run time.
        private static bool IsReadableAs(object stored, Type fieldType)
        {
            if (stored == null)
            {
                return !fieldType.IsValueType || Nullable.GetUnderlyingType(fieldType) != null;
            }

            return fieldType.IsInstanceOfType(stored);
        }
    }
}
