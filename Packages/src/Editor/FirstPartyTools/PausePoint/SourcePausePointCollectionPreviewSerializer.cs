using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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

        // Why: without this converter, Newtonsoft serializes enums by their underlying integer,
        // diverging from the scalar-capture path (which already renders enum values by name) and
        // producing an inconsistent preview like [0,null,1] instead of ["Alpha",null,"Beta"].
        private static readonly JsonSerializer PrimitiveSerializer = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = HandleSerializationError,
                Converters = { new StringEnumConverter() }
            });

        public static bool TrySerialize(object rawValue, int maxElementCount, ref bool truncated, out string preview)
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
                    rawValue, SourcePausePointConstants.MaxCollectionPreviewDepth, maxElementCount, visited, ref truncated);
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
            object value, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            // Why: Collision2D's internal fields are raw instance IDs; prefer the property-based
            // preview that exposes collider hierarchy paths when main-thread Classify is available.
            // Why remainingDepth: without this gate a nested Collision2D would bypass the same
            // MaxCollectionPreviewDepth cutoff that BuildObjectFieldsToken already enforces.
            if (remainingDepth > 0
                && SourcePausePointCollision2DPreviewBuilder.TryBuildToken(value, out JToken collision2DToken))
            {
                return collision2DToken;
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
                return BuildEnumerableToken(enumerable, remainingDepth, maxElementCount, visited, ref truncated);
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

                return BuildObjectFieldsToken(value, remainingDepth, maxElementCount, visited, ref truncated);
            }

            return new JValue(SafeToString(value));
        }

        private static JObject BuildObjectFieldsToken(
            object value, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
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

                if (fieldCount >= maxElementCount)
                {
                    truncated = true;
                    break;
                }

                jsonObject[name] = BuildToken(field.GetValue(value), remainingDepth - 1, maxElementCount, visited, ref truncated);
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

        // Why: Array.GetEnumerator() flattens every rank in row-major order with no dimension
        // info, so a T[,] preview otherwise reads as a flat (possibly truncated-looking) list
        // with no way to tell it apart from an empty or 1D collection.
        private static JObject BuildMultidimensionalArrayToken(
            Array array, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
        {
            string elementTypeName = array.GetType().GetElementType().Name;
            IEnumerable<string> dimensions = Enumerable.Range(0, array.Rank).Select(dimension => array.GetLength(dimension).ToString());

            JArray elements = BuildArrayToken(array, remainingDepth, maxElementCount, visited, ref truncated);
            int previewedElements = elements.Count;
            JObject shapeToken = new()
            {
                ["Shape"] = $"{elementTypeName}[{string.Join(",", dimensions)}]",
                ["TotalElements"] = array.Length,
                ["PreviewedElements"] = previewedElements,
                ["ElementOrder"] = SourcePausePointConstants.MultidimensionalArrayElementOrder,
                ["Elements"] = elements
            };
            if (previewedElements < array.Length)
            {
                shapeToken["ElementsTruncated"] = true;
            }

            return shapeToken;
        }

        private static JArray BuildArrayToken(
            IEnumerable enumerable, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
        {
            JArray array = new();
            int elementCount = 0;
            foreach (object element in enumerable)
            {
                if (elementCount >= maxElementCount)
                {
                    truncated = true;
                    break;
                }

                array.Add(BuildToken(element, remainingDepth - 1, maxElementCount, visited, ref truncated));
                elementCount++;
            }

            return array;
        }

        private static JObject BuildDictionaryToken(
            IDictionary dictionary, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
        {
            JObject jsonObject = new();
            int elementCount = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (elementCount >= maxElementCount)
                {
                    truncated = true;
                    break;
                }

                string key = FormatDictionaryKey(entry.Key);
                jsonObject[key] = BuildToken(entry.Value, remainingDepth - 1, maxElementCount, visited, ref truncated);
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

        // Type-level counterpart of IsJsonPrimitive (plus string, which BuildToken also resolves
        // unconditionally above), used to recognize an array/dictionary whose elements will always
        // resolve regardless of remaining depth budget.
        // Why a helper: the IEnumerable branch has its own circular-ref, depth-budget, and
        // collection-kind decisions, and leaving them inline kept BuildToken over CA1502.
        private static JToken BuildEnumerableToken(
            IEnumerable enumerable, int remainingDepth, int maxElementCount, HashSet<object> visited, ref bool truncated)
        {
            if (!visited.Add(enumerable))
            {
                return new JValue("(circular)");
            }

            // Why: an array or dictionary whose elements/keys/values are all primitives/enums
            // costs no further recursion budget once reached (each one resolves via the
            // IsJsonPrimitive branch above regardless of depth), so gating it on remainingDepth
            // here would degrade a nested field like "Board._cells" (a PieceType?[,]) to a bare
            // type-name string purely because two field-access hops already spent the depth
            // budget, not because previewing it is actually unsafe or unbounded.
            bool isPrimitiveElementArray =
                enumerable is Array primitiveElementArray && IsJsonPrimitiveElementType(primitiveElementArray.GetType().GetElementType());
            bool isPrimitiveKeyValueDictionary =
                enumerable is IDictionary && IsPrimitiveKeyValueDictionaryType(enumerable.GetType());

            if (remainingDepth <= 0 && !isPrimitiveElementArray && !isPrimitiveKeyValueDictionary)
            {
                return new JValue(SafeToString(enumerable));
            }

            if (enumerable is IDictionary dictionary)
            {
                return BuildDictionaryToken(dictionary, remainingDepth, maxElementCount, visited, ref truncated);
            }

            if (enumerable is Array multidimensionalArray && multidimensionalArray.Rank > 1)
            {
                return BuildMultidimensionalArrayToken(multidimensionalArray, remainingDepth, maxElementCount, visited, ref truncated);
            }

            if (enumerable is ICollection)
            {
                return BuildArrayToken(enumerable, remainingDepth, maxElementCount, visited, ref truncated);
            }

            return new JValue(SafeToString(enumerable));
        }

        private static bool IsJsonPrimitiveElementType(Type elementType)
        {
            Type underlyingType = Nullable.GetUnderlyingType(elementType) ?? elementType;
            return IsJsonPrimitiveIntegerElementType(underlyingType)
                || IsJsonPrimitiveNonIntegerElementType(underlyingType);
        }

        private static bool IsJsonPrimitiveIntegerElementType(Type underlyingType)
        {
            return underlyingType == typeof(byte) || underlyingType == typeof(sbyte)
                || underlyingType == typeof(short) || underlyingType == typeof(ushort)
                || underlyingType == typeof(int) || underlyingType == typeof(uint)
                || underlyingType == typeof(long) || underlyingType == typeof(ulong);
        }

        private static bool IsJsonPrimitiveNonIntegerElementType(Type underlyingType)
        {
            return underlyingType == typeof(bool)
                || underlyingType == typeof(float) || underlyingType == typeof(double)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(char)
                || underlyingType == typeof(string)
                || underlyingType.IsEnum;
        }

        // Dictionary counterpart of IsJsonPrimitiveElementType: recognizes an IDictionary<TKey,
        // TValue> whose key and value types will both always resolve through the IsJsonPrimitive
        // branch regardless of remaining depth budget.
        private static bool IsPrimitiveKeyValueDictionaryType(Type dictionaryType)
        {
            Type dictionaryInterface = dictionaryType.GetInterfaces()
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            if (dictionaryInterface == null)
            {
                return false;
            }

            Type[] typeArguments = dictionaryInterface.GetGenericArguments();
            return IsJsonPrimitiveElementType(typeArguments[0]) && IsJsonPrimitiveElementType(typeArguments[1]);
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
