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
    /// Builds the shared demangled capture frame consumed by the formatter and raw-ref holder.
    /// </summary>
    internal static class SourcePausePointVariableCollector
    {
        private static readonly Regex HoistedLocalFieldNamePattern = new(@"^<([^>]+)>5__\d+$", RegexOptions.Compiled);
        private const string StateMachineOuterThisFieldName = "<>4__this";
        private static readonly Regex EnclosingClosureFieldPattern = new(@"^CS\$<>8__locals\d+$", RegexOptions.Compiled);

        // The synthetic entry name for the paused instance itself. C# identifiers cannot be named
        // "this", so this never collides with a captured local, parameter, or field.
        private const string ThisEntryName = "this";

        /// <summary>
        /// Lists locals, then parameters, then the variables the compiler-generated holders carry,
        /// then the instance as "this" followed by its fields. The holders are the instance when it
        /// is a closure or state machine, and every closure struct in closureFrames.
        /// </summary>
        public static UloopPausePointCapturedVariableFrame Collect(
            object instance, object[] closureFrames, object[] parameterNamesAndValues, object[] localNamesAndValues)
        {
            Debug.Assert(closureFrames != null, "closureFrames must not be null");
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

            CollectHolderAndInstanceVariables(
                instance, closureFrames, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                ref truncated);

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

        private static void CollectHolderAndInstanceVariables(
            object instance,
            object[] closureFrames,
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated)
        {
            // A normal instance is `this` itself. A compiler-generated instance (closure or state
            // machine) is only a holder: the real `this` is found through its links, never the
            // holder itself.
            object receiver = instance != null && !IsCompilerGeneratedHolder(instance.GetType()) ? instance : null;
            object outerThis = null;
            if (instance != null && receiver == null)
            {
                object linkedOuterThis = CollectDirectFieldVariables(
                    instance, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                    ref truncated, followOuterThis: true);
                outerThis ??= linkedOuterThis;
            }

            // Closure structs hold variables of the enclosing scopes, so they are listed before the
            // instance state to keep the count cap prioritizing variables over fields. Every struct
            // is walked even after one of them links to the instance, because each carries the
            // variables of its own scope.
            foreach (object closureFrame in closureFrames)
            {
                if (closureFrame == null)
                {
                    continue;
                }

                object linkedOuterThis = CollectDirectFieldVariables(
                    closureFrame, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                    ref truncated, followOuterThis: true);
                outerThis ??= linkedOuterThis;
            }

            object thisValue = receiver ?? outerThis;
            if (thisValue == null)
            {
                return;
            }

            TryAppendEntry(
                entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount, ref truncated,
                ThisEntryName, UloopCapturedVariableScope.This, thisValue);

            // Why no link following: only compiler-generated holders link outward, and `this` is
            // never one of them.
            CollectDirectFieldVariables(
                thisValue, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
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
            bool isCompilerGeneratedHolder = IsCompilerGeneratedHolder(source.GetType());
            string plainFieldScope = isCompilerGeneratedHolder
                ? UloopCapturedVariableScope.Parameter
                : UloopCapturedVariableScope.InstanceField;

            foreach (FieldInfo field in EnumerateInstanceFields(source.GetType()))
            {
                if (followOuterThis && IsOuterLinkField(field, isCompilerGeneratedHolder))
                {
                    object linked = field.GetValue(source);
                    object linkedOuterThis = FollowOuterLink(
                        field, linked, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                        ref truncated);
                    outerThis ??= linkedOuterThis;
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

        // Returns the outer instance a link field leads to. A link to another compiler-generated
        // holder (an async lambda's state machine pointing at its closure, a nested closure
        // pointing at the enclosing one) is walked so the variables it holds are listed and the
        // real instance behind it is found, instead of showing the holder as "this" or a variable.
        private static object FollowOuterLink(
            FieldInfo field,
            object linked,
            List<UloopPausePointCapturedVariableEntry> entries,
            HashSet<string> capturedNames,
            List<string> truncatedVariableNames,
            ref int truncatedVariableCount,
            ref bool truncated)
        {
            if (linked == null)
            {
                return null;
            }

            if (IsCompilerGeneratedHolder(linked.GetType()))
            {
                return CollectDirectFieldVariables(
                    linked, entries, capturedNames, truncatedVariableNames, ref truncatedVariableCount,
                    ref truncated, followOuterThis: true);
            }

            return EnclosingClosureFieldPattern.IsMatch(field.Name) ? null : linked;
        }

        // Links from a compiler-generated holder outward: "<>4__this" to the instance or the
        // enclosing closure, "CS$<>8__localsN" to the enclosing closure, and the hot-reload shim's
        // receiver parameter, which a shim's holders keep under its own name because the shim is a
        // static method taking the instance as that parameter.
        private static bool IsOuterLinkField(FieldInfo field, bool isCompilerGeneratedHolder)
        {
            if (field.Name == StateMachineOuterThisFieldName)
            {
                return true;
            }

            if (!isCompilerGeneratedHolder)
            {
                return false;
            }

            return field.Name == HotReloadShimMethodLookup.ShimReceiverParameterName
                || EnclosingClosureFieldPattern.IsMatch(field.Name);
        }

        // An async lambda's state machine carries no [CompilerGenerated], so the "<" that no C#
        // identifier can start with also marks a compiler-generated type.
        private static bool IsCompilerGeneratedHolder(Type type)
        {
            return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute))
                || type.Name.StartsWith("<", StringComparison.Ordinal);
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
