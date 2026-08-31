using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the shared demangled capture frame consumed by the formatter and raw-ref holder.
    /// </summary>
    internal static class SourcePausePointVariableCollector
    {
        private static readonly Regex HoistedLocalFieldNamePattern = new(@"^<([^>]+)>5__\d+$", RegexOptions.Compiled);
        private const string StateMachineOuterThisFieldName = "<>4__this";

        // The synthetic entry name for the paused instance itself. C# identifiers cannot be named
        // "this", so this never collides with a captured local, parameter, or field.
        private const string ThisEntryName = "this";

        public static UloopPausePointCapturedVariableFrame Collect(
            object instance, object[] parameterNamesAndValues, object[] localNamesAndValues)
        {
            Debug.Assert(parameterNamesAndValues != null, "parameterNamesAndValues must not be null");
            Debug.Assert(localNamesAndValues != null, "localNamesAndValues must not be null");
            Debug.Assert(parameterNamesAndValues.Length % 2 == 0, "parameterNamesAndValues must contain name/value pairs");
            Debug.Assert(localNamesAndValues.Length % 2 == 0, "localNamesAndValues must contain name/value pairs");

            List<UloopPausePointCapturedVariableEntry> entries = new();
            List<string> truncatedVariableNames = new();
            int truncatedVariableCount = 0;
            bool truncated = false;
            HashSet<string> capturedNames = new();

            // Why keep scanning after the count cap: callers need the discarded names and the exact
            // dropped count, not only a Truncated bool. Values past the cap are never retained.
            AppendPairs(
                entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                localNamesAndValues, UloopCapturedVariableScope.Local);
            AppendPairs(
                entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                parameterNamesAndValues, UloopCapturedVariableScope.Parameter);

            if (instance != null)
            {
                CollectInstanceFieldVariables(
                    instance, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                    ref truncated);
            }

            return new UloopPausePointCapturedVariableFrame(
                entries, truncated, truncatedVariableNames, truncatedVariableCount);
        }

        private static void AppendPairs(
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated,
            object[] namesAndValues,
            string scope)
        {
            for (int i = 0; i < namesAndValues.Length; i += 2)
            {
                string name = (string)namesAndValues[i];
                object value = namesAndValues[i + 1];
                TryAppendEntry(
                    entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                    name, scope, value);
            }
        }

        private static void CollectInstanceFieldVariables(
            object instance,
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated)
        {
            bool isCompilerGeneratedStateMachine =
                Attribute.IsDefined(instance.GetType(), typeof(CompilerGeneratedAttribute));

            // Normal method: the paused instance itself is `this`, emitted before its fields so the
            // count cap keeps prioritizing locals and parameters over instance state.
            if (!isCompilerGeneratedStateMachine)
            {
                TryAppendEntry(
                    entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                    ThisEntryName, UloopCapturedVariableScope.This, instance);
            }

            object outerThis = CollectDirectFieldVariables(
                instance, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                ref truncated, followOuterThis: true);
            if (outerThis == null)
            {
                return;
            }

            // Async/coroutine state machine: the real `this` is the hoisted outer instance, never the
            // compiler-generated state machine object. Emit it before the outer instance's fields.
            TryAppendEntry(
                entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                ThisEntryName, UloopCapturedVariableScope.This, outerThis);

            CollectDirectFieldVariables(
                outerThis, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                ref truncated, followOuterThis: false);
        }

        private static object CollectDirectFieldVariables(
            object source,
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated,
            bool followOuterThis)
        {
            object outerThis = null;
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
                    TryAppendEntry(
                        entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                        hoistedLocalMatch.Groups[1].Value, UloopCapturedVariableScope.Local, field.GetValue(source));
                    continue;
                }

                Match autoPropertyMatch = SourcePausePointConstants.AutoPropertyBackingFieldPattern.Match(field.Name);
                string fieldName = autoPropertyMatch.Success ? autoPropertyMatch.Groups[1].Value : field.Name;

                if (!autoPropertyMatch.Success && field.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                TryAppendEntry(
                    entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                    fieldName, plainFieldScope, field.GetValue(source));
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

        private static void TryAppendEntry(
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated,
            string name,
            string scope,
            object rawValue)
        {
            if (!capturedNames.Add(name))
            {
                return;
            }

            if (entries.Count >= SourcePausePointConstants.MaxCapturedVariableCount)
            {
                truncated = true;
                truncatedVariableCount++;
                if (truncatedVariableNames.Count < SourcePausePointConstants.MaxTruncatedVariableNamesReported)
                {
                    truncatedVariableNames.Add(name);
                }

                return;
            }

            entries.Add(new UloopPausePointCapturedVariableEntry(name, scope, rawValue));
        }
    }
}
