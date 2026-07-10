using System;
using System.Collections.Generic;
using System.Reflection;
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
            bool truncated = false;
            // An async state machine's own fields include hoisted copies of the original method's
            // parameters under their plain source name (only true locals get the "<name>5__N"
            // treatment); tracking already-captured names keeps those from being double-reported
            // once as Parameter (from the array below) and again as InstanceField (from the walk).
            HashSet<string> capturedNames = new();

            AppendPairs(results, capturedNames, ref truncated, localNamesAndValues, UloopCapturedVariableScope.Local);
            AppendPairs(results, capturedNames, ref truncated, parameterNamesAndValues, UloopCapturedVariableScope.Parameter);

            if (instance != null && !truncated)
            {
                CollectInstanceFieldVariables(instance, results, capturedNames, ref truncated);
            }

            return (results, truncated);
        }

        private static void AppendPairs(
            List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated,
            object[] namesAndValues, string scope)
        {
            for (int i = 0; i < namesAndValues.Length; i += 2)
            {
                if (truncated)
                {
                    return;
                }

                string name = (string)namesAndValues[i];
                object value = namesAndValues[i + 1];
                if (!TryAppendVariable(results, capturedNames, ref truncated, name, scope, value))
                {
                    return;
                }
            }
        }

        // Async/iterator state machines hoist the original `this` into a `<>4__this` field; this
        // follows it exactly one level deep to also capture the real instance's fields, without
        // recursing into any further state-machine hop.
        private static void CollectInstanceFieldVariables(
            object instance, List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated)
        {
            object outerThis = CollectDirectFieldVariables(instance, results, capturedNames, ref truncated, followOuterThis: true);
            if (truncated || outerThis == null)
            {
                return;
            }

            CollectDirectFieldVariables(outerThis, results, capturedNames, ref truncated, followOuterThis: false);
        }

        private static object CollectDirectFieldVariables(
            object source, List<UloopCapturedVariable> results, HashSet<string> capturedNames, ref bool truncated,
            bool followOuterThis)
        {
            object outerThis = null;
            foreach (FieldInfo field in EnumerateInstanceFields(source.GetType()))
            {
                if (truncated)
                {
                    return outerThis;
                }

                if (followOuterThis && field.Name == StateMachineOuterThisFieldName)
                {
                    outerThis = field.GetValue(source);
                    continue;
                }

                Match hoistedLocalMatch = HoistedLocalFieldNamePattern.Match(field.Name);
                if (hoistedLocalMatch.Success)
                {
                    TryAppendVariable(
                        results, capturedNames, ref truncated, hoistedLocalMatch.Groups[1].Value,
                        UloopCapturedVariableScope.Local, field.GetValue(source));
                    continue;
                }

                if (field.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    // Other compiler-generated plumbing (state machine "<>1__state", "<>t__builder",
                    // auto-property backing fields, etc.) carries no source-level meaning to capture.
                    continue;
                }

                TryAppendVariable(
                    results, capturedNames, ref truncated, field.Name, UloopCapturedVariableScope.InstanceField,
                    field.GetValue(source));
            }

            return outerThis;
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

            string value = ApplyValueLengthCap(SafeToString(rawValue), ref truncated);
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

        private static string ApplyValueLengthCap(string value, ref bool truncated)
        {
            if (value.Length <= SourcePausePointConstants.MaxCapturedVariableValueLength)
            {
                return value;
            }

            truncated = true;
            return value.Substring(0, SourcePausePointConstants.MaxCapturedVariableValueLength);
        }

        // The single sanctioned try-catch in this codebase's capture path: user ToString()
        // overrides are untrusted code we must not let crash a pause-point hit.
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
