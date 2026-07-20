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
            bool truncated = false;
            HashSet<string> capturedNames = new();

            bool countCapReached = AppendPairs(
                entries, capturedNames, ref truncated, localNamesAndValues, UloopCapturedVariableScope.Local);
            if (!countCapReached)
            {
                countCapReached = AppendPairs(
                    entries, capturedNames, ref truncated, parameterNamesAndValues, UloopCapturedVariableScope.Parameter);
            }

            if (instance != null && !countCapReached)
            {
                CollectInstanceFieldVariables(instance, entries, capturedNames, ref truncated);
            }

            return new UloopPausePointCapturedVariableFrame(entries, truncated);
        }

        private static bool AppendPairs(
            List<UloopPausePointCapturedVariableEntry> entries, HashSet<string> capturedNames, ref bool truncated,
            object[] namesAndValues, string scope)
        {
            for (int i = 0; i < namesAndValues.Length; i += 2)
            {
                string name = (string)namesAndValues[i];
                object value = namesAndValues[i + 1];
                if (!TryAppendEntry(entries, capturedNames, ref truncated, name, scope, value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectInstanceFieldVariables(
            object instance, List<UloopPausePointCapturedVariableEntry> entries, HashSet<string> capturedNames,
            ref bool truncated)
        {
            bool isCompilerGeneratedStateMachine =
                Attribute.IsDefined(instance.GetType(), typeof(CompilerGeneratedAttribute));

            // Normal method: the paused instance itself is `this`, emitted before its fields so the
            // count cap keeps prioritizing locals and parameters over instance state.
            if (!isCompilerGeneratedStateMachine)
            {
                if (!TryAppendEntry(
                    entries, capturedNames, ref truncated, ThisEntryName, UloopCapturedVariableScope.This, instance))
                {
                    return;
                }
            }

            (object outerThis, bool countCapReached) = CollectDirectFieldVariables(
                instance, entries, capturedNames, ref truncated, followOuterThis: true);
            if (countCapReached || outerThis == null)
            {
                return;
            }

            // Async/coroutine state machine: the real `this` is the hoisted outer instance, never the
            // compiler-generated state machine object. Emit it before the outer instance's fields.
            if (!TryAppendEntry(
                entries, capturedNames, ref truncated, ThisEntryName, UloopCapturedVariableScope.This, outerThis))
            {
                return;
            }

            CollectDirectFieldVariables(outerThis, entries, capturedNames, ref truncated, followOuterThis: false);
        }

        private static (object OuterThis, bool CountCapReached) CollectDirectFieldVariables(
            object source, List<UloopPausePointCapturedVariableEntry> entries, HashSet<string> capturedNames,
            ref bool truncated, bool followOuterThis)
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
                    if (!TryAppendEntry(
                        entries, capturedNames, ref truncated, hoistedLocalMatch.Groups[1].Value,
                        UloopCapturedVariableScope.Local, field.GetValue(source)))
                    {
                        return (outerThis, true);
                    }

                    continue;
                }

                Match autoPropertyMatch = SourcePausePointConstants.AutoPropertyBackingFieldPattern.Match(field.Name);
                string fieldName = autoPropertyMatch.Success ? autoPropertyMatch.Groups[1].Value : field.Name;

                if (!autoPropertyMatch.Success && field.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryAppendEntry(entries, capturedNames, ref truncated, fieldName, plainFieldScope, field.GetValue(source)))
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

        private static bool TryAppendEntry(
            List<UloopPausePointCapturedVariableEntry> entries, HashSet<string> capturedNames, ref bool truncated,
            string name, string scope, object rawValue)
        {
            if (!capturedNames.Add(name))
            {
                return true;
            }

            if (entries.Count >= SourcePausePointConstants.MaxCapturedVariableCount)
            {
                truncated = true;
                return false;
            }

            entries.Add(new UloopPausePointCapturedVariableEntry(name, scope, rawValue));
            return true;
        }
    }
}
