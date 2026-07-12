using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns the raw name/value pairs a pause point captured into the DTOs a CLI response can
    /// serialize: demangling compiler-hoisted local/`this` fields, classifying UnityEngine.Object
    /// references, and capping both value length and variable count.
    /// </summary>
    internal static class SourcePausePointVariableFormatter
    {
        // Roslyn hoists a local that crosses an await/yield into a state machine field named
        // "<name>5__N"; this demangles it back to the source-level local name.
        private static readonly Regex HoistedLocalFieldNamePattern = new(@"^<([^>]+)>5__\d+$", RegexOptions.Compiled);
        private const string StateMachineOuterThisFieldName = "<>4__this";
        private const string OffMainThreadValue = "(captured off main thread)";
        private const string DestroyedValue = "(destroyed)";

        public static (List<UloopCapturedVariable> Variables, bool Truncated) Format(
            object instance, object[] parameterNamesAndValues, object[] localNamesAndValues)
        {
            Debug.Assert(parameterNamesAndValues != null, "parameterNamesAndValues must not be null");
            Debug.Assert(localNamesAndValues != null, "localNamesAndValues must not be null");
            Debug.Assert(parameterNamesAndValues.Length % 2 == 0, "parameterNamesAndValues must contain name/value pairs");
            Debug.Assert(localNamesAndValues.Length % 2 == 0, "localNamesAndValues must contain name/value pairs");

            List<UloopCapturedVariable> results = new();
            // Reports whether ANY value was clipped or the count cap was hit; a single over-long
            // value must not stop enumeration of the remaining locals/parameters/instance fields.
            bool truncated = false;
            // An async state machine's own fields include hoisted copies of the original method's
            // parameters under their plain source name (only true locals get the "<name>5__N"
            // treatment); tracking already-captured names keeps those from being double-reported
            // once as Parameter (from the array below) and again as InstanceField (from the walk).
            HashSet<string> capturedNames = new();

            bool countCapReached = AppendPairs(
                results, capturedNames, ref truncated, localNamesAndValues, UloopCapturedVariableScope.Local);
            if (!countCapReached)
            {
                countCapReached = AppendPairs(
                    results, capturedNames, ref truncated, parameterNamesAndValues, UloopCapturedVariableScope.Parameter);
            }

            if (instance != null && !countCapReached)
            {
                CollectInstanceFieldVariables(instance, results, capturedNames, ref truncated);
            }

            return (results, truncated);
        }

        // Returns true once the count cap is hit, so the caller can stop enumerating further
        // arrays/fields; a per-value length truncation alone must not signal this.
        private static bool AppendPairs(
            List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated,
            object[] namesAndValues, string scope)
        {
            for (int i = 0; i < namesAndValues.Length; i += 2)
            {
                string name = (string)namesAndValues[i];
                object value = namesAndValues[i + 1];
                if (!TryAppendVariable(results, capturedNames, ref truncated, name, scope, value))
                {
                    return true;
                }
            }

            return false;
        }

        // Async/iterator state machines hoist the original `this` into a `<>4__this` field; this
        // follows it exactly one level deep to also capture the real instance's fields, without
        // recursing into any further state-machine hop.
        private static void CollectInstanceFieldVariables(
            object instance, List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated)
        {
            (object outerThis, bool countCapReached) = CollectDirectFieldVariables(
                instance, results, capturedNames, ref truncated, followOuterThis: true);
            if (countCapReached || outerThis == null)
            {
                return;
            }

            CollectDirectFieldVariables(outerThis, results, capturedNames, ref truncated, followOuterThis: false);
        }

        private static (object OuterThis, bool CountCapReached) CollectDirectFieldVariables(
            object source, List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated,
            bool followOuterThis)
        {
            object outerThis = null;
            // A compiler-generated state machine hoists the original method's parameters as
            // plain-named fields (only true locals get the "<name>5__N" treatment), so those
            // fields are the method's Parameter scope, not this type's own InstanceField scope.
            bool isCompilerGeneratedStateMachine = Attribute.IsDefined(source.GetType(), typeof(CompilerGeneratedAttribute));
            string plainFieldScope = isCompilerGeneratedStateMachine
                ? UloopCapturedVariableScope.Parameter
                : UloopCapturedVariableScope.InstanceField;

            foreach (FieldInfo field in EnumerateInstanceFields(source.GetType()))
            {
                if (followOuterThis && field.Name == StateMachineOuterThisFieldName)
                {
                    outerThis = field.GetValue(source);
                    continue;
                }

                Match hoistedLocalMatch = HoistedLocalFieldNamePattern.Match(field.Name);
                if (hoistedLocalMatch.Success)
                {
                    if (!TryAppendVariable(
                        results, capturedNames, ref truncated, hoistedLocalMatch.Groups[1].Value,
                        UloopCapturedVariableScope.Local, field.GetValue(source)))
                    {
                        return (outerThis, true);
                    }

                    continue;
                }

                if (field.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    // Other compiler-generated plumbing (state machine "<>1__state", "<>t__builder",
                    // auto-property backing fields, etc.) carries no source-level meaning to capture.
                    continue;
                }

                if (!TryAppendVariable(results, capturedNames, ref truncated, field.Name, plainFieldScope, field.GetValue(source)))
                {
                    return (outerThis, true);
                }
            }

            return (outerThis, false);
        }

        private static IEnumerable<FieldInfo> EnumerateInstanceFields(Type type)
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type current = type;
                 current != null && current != typeof(UnityEngine.Object) && current != typeof(object);
                 current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    yield return field;
                }
            }
        }

        private static bool TryAppendVariable(
            List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated, string name,
            string scope, object rawValue)
        {
            if (!capturedNames.Add(name))
            {
                return true;
            }

            if (results.Count >= SourcePausePointConstants.MaxCapturedVariableCount)
            {
                truncated = true;
                return false;
            }

            results.Add(FormatVariable(name, scope, rawValue, ref truncated));
            return true;
        }

        private static UloopCapturedVariable FormatVariable(string name, string scope, object rawValue, ref bool truncated)
        {
            if (rawValue == null)
            {
                return new UloopCapturedVariable(name, scope, string.Empty, "null", string.Empty, string.Empty, 0);
            }

            string typeName = rawValue.GetType().FullName;
            if (rawValue is UnityEngine.Object unityObjectCandidate)
            {
                return FormatUnityObjectVariable(name, scope, typeName, unityObjectCandidate);
            }

            if (SourcePausePointCollectionPreviewSerializer.TrySerialize(rawValue, ref truncated, out string collectionPreview))
            {
                string cappedPreview = ApplyValueLengthCap(
                    collectionPreview,
                    SourcePausePointConstants.MaxCollectionPreviewValueLength,
                    ref truncated);
                return new UloopCapturedVariable(name, scope, typeName, cappedPreview, string.Empty, string.Empty, 0);
            }

            string value = ApplyValueLengthCap(
                SafeToString(rawValue), SourcePausePointConstants.MaxCapturedVariableValueLength, ref truncated);
            return new UloopCapturedVariable(name, scope, typeName, value, string.Empty, string.Empty, 0);
        }

        private static UloopCapturedVariable FormatUnityObjectVariable(
            string name, string scope, string typeName, UnityEngine.Object unityObjectCandidate)
        {
            if (!MainThreadSwitcher.IsMainThread)
            {
                // Transform/AssetDatabase/InstanceID access all require the main thread; degrade
                // to a plain type-tagged placeholder rather than risk touching engine state here.
                return new UloopCapturedVariable(name, scope, typeName, OffMainThreadValue, string.Empty, string.Empty, 0);
            }

            if (unityObjectCandidate == null)
            {
                // Fake-null: the managed wrapper is a live reference, so GetInstanceID() is still safe.
                return new UloopCapturedVariable(
                    name, scope, typeName, DestroyedValue,
                    UloopCapturedVariableUnityObjectKind.Destroyed, string.Empty, unityObjectCandidate.GetInstanceID());
            }

            SourcePausePointUnityObjectClassifier.Classification classification =
                SourcePausePointUnityObjectClassifier.Classify(unityObjectCandidate);
            return new UloopCapturedVariable(
                name, scope, typeName, unityObjectCandidate.name,
                classification.Kind, classification.Path, classification.InstanceId);
        }

        private static string ApplyValueLengthCap(string value, int maxLength, ref bool truncated)
        {
            if (value.Length <= maxLength)
            {
                return value;
            }

            truncated = true;
            return value.Substring(0, maxLength);
        }

        // Sanctioned try-catch sites in the capture path: user ToString() overrides (below) and
        // materialized-collection enumeration in SourcePausePointCollectionPreviewSerializer.
        // Both must not let untrusted user code crash a pause-point hit.
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
    }
}
