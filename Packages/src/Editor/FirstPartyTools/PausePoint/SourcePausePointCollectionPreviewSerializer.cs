using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds a shallow JSON preview for captured materialized collections and plain custom types
    /// so pause-point status can show real contents instead of the default type-name ToString.
    /// </summary>
    internal static class SourcePausePointCollectionPreviewSerializer
    {
        private const string OffMainThreadValue = "(captured off main thread)";
        private const string DestroyedValue = "(destroyed)";

        private static readonly JsonSerializer PrimitiveSerializer = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = HandleSerializationError
            });

        public static bool TrySerialize(object rawValue, ref bool truncated, out string preview)
        {
            preview = string.Empty;
            if (rawValue == null)
            {
                return false;
            }

            if (rawValue is string || rawValue is byte[])
            {
                return false;
            }

            bool isMaterializedCollection = rawValue is ICollection || rawValue is IDictionary;

            // Why: deferred IEnumerable/LINQ must not execute user code during preview; only
            // materialized ICollection/IDictionary snapshots and plain objects without a custom
            // ToString (below) are safe to walk.
            if (!isMaterializedCollection && rawValue is IEnumerable)
            {
                return false;
            }

            // Primitives and enums already have a meaningful ToString; only route custom types
            // that never overrode it to the field-based JSON preview below.
            if (!isMaterializedCollection && HasToStringOverride(rawValue.GetType()))
            {
                return false;
            }

            try
            {
                HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
                JToken token = BuildToken(
                    rawValue, SourcePausePointConstants.MaxCollectionPreviewDepth, visited, ref truncated);
                preview = token.ToString(Formatting.None);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static void HandleSerializationError(object sender, ErrorEventArgs args)
        {
            Debug.LogException(args.ErrorContext.Error);
            args.ErrorContext.Handled = true;
        }

        private static JToken BuildToken(
            object value, int remainingDepth, HashSet<object> visited, ref bool truncated)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            if (value is UnityEngine.Object unityObject)
            {
                return new JValue(FormatUnityObjectElement(unityObject));
            }

            if (value is string stringValue)
            {
                return JValue.FromObject(stringValue, PrimitiveSerializer);
            }

            if (IsJsonPrimitive(value))
            {
                return JToken.FromObject(value, PrimitiveSerializer);
            }

            if (value is IEnumerable enumerable)
            {
                if (!visited.Add(value))
                {
                    return new JValue("(circular)");
                }

                if (remainingDepth <= 0)
                {
                    return new JValue(SafeToString(value));
                }

                if (value is IDictionary dictionary)
                {
                    return BuildDictionaryToken(dictionary, remainingDepth, visited, ref truncated);
                }

                if (value is ICollection)
                {
                    return BuildArrayToken(enumerable, remainingDepth, visited, ref truncated);
                }

                return new JValue(SafeToString(value));
            }

            if (!HasToStringOverride(value.GetType()))
            {
                if (!visited.Add(value))
                {
                    return new JValue("(circular)");
                }

                if (remainingDepth <= 0)
                {
                    return new JValue(SafeToString(value));
                }

                return BuildObjectFieldsToken(value, remainingDepth, visited, ref truncated);
            }

            return new JValue(SafeToString(value));
        }

        private static JObject BuildObjectFieldsToken(
            object value, int remainingDepth, HashSet<object> visited, ref bool truncated)
        {
            JObject jsonObject = new();
            int fieldCount = 0;
            foreach ((string name, FieldInfo field) in EnumerateObjectFields(value.GetType()))
            {
                // EnumerateObjectFields walks derived-to-base, so a name already present here is
                // the derived class's own field; skip the base class's shadowed field instead of
                // letting it silently overwrite the value that is actually in effect at runtime.
                if (jsonObject.ContainsKey(name))
                {
                    continue;
                }

                if (fieldCount >= SourcePausePointConstants.MaxCollectionPreviewElementCount)
                {
                    truncated = true;
                    break;
                }

                jsonObject[name] = BuildToken(field.GetValue(value), remainingDepth - 1, visited, ref truncated);
                fieldCount++;
            }

            return jsonObject;
        }

        private static IEnumerable<(string Name, FieldInfo Field)> EnumerateObjectFields(Type type)
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = type;
                 current != null && current != typeof(object) && current != typeof(ValueType);
                 current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    Match backingFieldMatch = SourcePausePointConstants.AutoPropertyBackingFieldPattern.Match(field.Name);
                    if (backingFieldMatch.Success)
                    {
                        yield return (backingFieldMatch.Groups[1].Value, field);
                        continue;
                    }

                    if (field.Name.StartsWith("<", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return (field.Name, field);
                }
            }
        }

        private static bool HasToStringOverride(Type type)
        {
            MethodInfo toStringMethod = type.GetMethod(nameof(ToString), Type.EmptyTypes);
            return toStringMethod != null
                && toStringMethod.DeclaringType != typeof(object)
                && toStringMethod.DeclaringType != typeof(ValueType);
        }

        private static JArray BuildArrayToken(
            IEnumerable enumerable, int remainingDepth, HashSet<object> visited, ref bool truncated)
        {
            JArray array = new();
            int elementCount = 0;
            foreach (object element in enumerable)
            {
                if (elementCount >= SourcePausePointConstants.MaxCollectionPreviewElementCount)
                {
                    truncated = true;
                    break;
                }

                array.Add(BuildToken(element, remainingDepth - 1, visited, ref truncated));
                elementCount++;
            }

            return array;
        }

        private static JObject BuildDictionaryToken(
            IDictionary dictionary, int remainingDepth, HashSet<object> visited, ref bool truncated)
        {
            JObject jsonObject = new();
            int elementCount = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (elementCount >= SourcePausePointConstants.MaxCollectionPreviewElementCount)
                {
                    truncated = true;
                    break;
                }

                string key = FormatDictionaryKey(entry.Key);
                jsonObject[key] = BuildToken(entry.Value, remainingDepth - 1, visited, ref truncated);
                elementCount++;
            }

            return jsonObject;
        }

        private static string FormatDictionaryKey(object key)
        {
            if (key == null)
            {
                return "null";
            }

            if (key is UnityEngine.Object unityObject)
            {
                return FormatUnityObjectElement(unityObject);
            }

            return SafeToString(key);
        }

        private static bool IsJsonPrimitive(object value)
        {
            return value is bool
                or byte or sbyte
                or short or ushort
                or int or uint
                or long or ulong
                or float or double
                or decimal
                or char
                or Enum;
        }

        private static string FormatUnityObjectElement(UnityEngine.Object unityObject)
        {
            if (!MainThreadSwitcher.IsMainThread)
            {
                return OffMainThreadValue;
            }

            if (unityObject == null)
            {
                return DestroyedValue;
            }

            return unityObject.name;
        }

        // Sanctioned try-catch in the capture path: user ToString() overrides are untrusted code
        // we must not let crash a pause-point hit.
        private static string SafeToString(object value)
        {
            try
            {
                return value.ToString();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return $"(toString threw {exception.GetType().Name})";
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static ReferenceEqualityComparer Instance { get; } = new();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpersGetHashCode(obj);
            }

            // Why: object.GetHashCode is overridden by many collection types; reference identity is required.
            private static int RuntimeHelpersGetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
