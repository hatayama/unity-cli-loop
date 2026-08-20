using System;
using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Selects the caller frames worth reporting from a raw stack captured at a pause point hit.
    /// Pure logic over pre-extracted frame data so the selection rules stay unit-testable.
    /// </summary>
    internal static class SourcePausePointCallerFrameSelector
    {
        // Frames whose declaring type starts with one of these carry no diagnostic value for
        // "how execution reached the marker": runtime async machinery, patching infrastructure,
        // and uloop's own plumbing. Unity engine/editor frames are deliberately kept because an
        // entry point such as EditorApplication.update is itself diagnostic. MonoMod.* is still
        // skipped as infrastructure, except Harmony patch bodies which Select re-includes via
        // TryResolvePatchedCallerDisplay because they are the real application callers.
        private static readonly string[] SkippedTypePrefixes =
        {
            "System.",
            "Microsoft.",
            "Mono.",
            "HarmonyLib.",
            "MonoMod.",
            "io.github.hatayama.UnityCliLoop",
        };

        // rawFrames[0] must be the marker's own frame (the patched method). It is skipped
        // positionally instead of by identity because a hot-reload-patched marker method can
        // appear as a Harmony dynamic method whose display name is not predictable.
        public static List<UloopPausePointCallerFrame> Select(
            IReadOnlyList<SourcePausePointRawStackFrame> rawFrames)
        {
            Debug.Assert(rawFrames != null, "rawFrames must not be null");

            List<UloopPausePointCallerFrame> selected =
                new List<UloopPausePointCallerFrame>(SourcePausePointConstants.MaxCallerFrames);
            for (int i = 1; i < rawFrames.Count; i++)
            {
                if (selected.Count == SourcePausePointConstants.MaxCallerFrames)
                {
                    break;
                }

                SourcePausePointRawStackFrame frame = rawFrames[i];
                string patchedCallerDisplay = TryResolvePatchedCallerDisplay(frame);
                if (patchedCallerDisplay != null)
                {
                    // A dynamic method carries no debug symbols, so the frame is method-only by
                    // construction (File null, Line 0).
                    selected.Add(new UloopPausePointCallerFrame(patchedCallerDisplay, null, 0));
                    continue;
                }

                if (frame.TypeFullName != null && IsSkippedInfrastructureType(frame.TypeFullName))
                {
                    continue;
                }

                string file = NormalizeFilePath(frame.FileName);
                selected.Add(new UloopPausePointCallerFrame(
                    FormatMethodDisplay(frame.TypeFullName, frame.MethodName),
                    file,
                    file == null ? 0 : frame.Line));
            }

            return selected;
        }

        // A hot-reload-patched or pause-point-instrumented body executes as a Harmony dynamic
        // method ("MonoMod.Utils.DynamicMethodDefinition" declaring type, "_Patch{N}" name
        // suffix). Such a frame is a real application caller, so it must survive the
        // infrastructure prefix skip and be reported under its original "{Type}.{Method}" name.
        internal static string TryResolvePatchedCallerDisplay(SourcePausePointRawStackFrame frame)
        {
            if (frame.TypeFullName != SourcePausePointConstants.HarmonyDynamicMethodDeclaringType)
            {
                return null;
            }

            string logicalName = StripHarmonyPatchSuffix(frame.MethodName);
            if (logicalName == null)
            {
                return null;
            }

            // The logical name starts with the original declaring type, so the infrastructure
            // prefix policy still applies: a patched uloop-internal or BCL method must stay
            // hidden exactly like its compiled counterpart would be.
            if (IsSkippedInfrastructureType(logicalName))
            {
                return null;
            }

            // A patched async body surfaces as its state machine ("Ns.Type+<Method>d__N.MoveNext");
            // route the logical name through the same demangling as compiled frames so the payload
            // keeps the documented "logical method name" contract. For ordinary names the round
            // trip is the identity.
            int lastDot = logicalName.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == logicalName.Length - 1)
            {
                return logicalName;
            }

            return FormatMethodDisplay(logicalName.Substring(0, lastDot), logicalName.Substring(lastDot + 1));
        }

        // "Ns.Type.Method_Patch3" -> "Ns.Type.Method"; null when the name does not end with
        // the Harmony patch suffix (then the frame is genuine MonoMod infrastructure, not a
        // patch body, and must stay skipped).
        private static string StripHarmonyPatchSuffix(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            int suffixIndex = methodName.LastIndexOf(
                SourcePausePointConstants.HarmonyPatchNameSuffix, StringComparison.Ordinal);
            if (suffixIndex <= 0)
            {
                return null;
            }

            int digitsStart = suffixIndex + SourcePausePointConstants.HarmonyPatchNameSuffix.Length;
            if (digitsStart >= methodName.Length)
            {
                return null;
            }

            for (int i = digitsStart; i < methodName.Length; i++)
            {
                if (!char.IsDigit(methodName[i]))
                {
                    return null;
                }
            }

            return methodName.Substring(0, suffixIndex);
        }

        internal static bool IsSkippedInfrastructureType(string typeFullName)
        {
            Debug.Assert(typeFullName != null, "typeFullName must not be null");

            foreach (string prefix in SkippedTypePrefixes)
            {
                if (typeFullName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Compiler-generated async state machines surface as "Ns.Type+<Method>d__N.MoveNext";
        // report the logical "Ns.Type.Method" instead. Every other shape (including lambdas and
        // genuine nested types) is reported verbatim as "TypeFullName.MethodName".
        internal static string FormatMethodDisplay(string typeFullName, string methodName)
        {
            if (typeFullName == null)
            {
                return string.IsNullOrEmpty(methodName) ? "(unknown)" : methodName;
            }

            if (methodName == "MoveNext")
            {
                int open = typeFullName.LastIndexOf("+<", StringComparison.Ordinal);
                if (open >= 0)
                {
                    int close = typeFullName.IndexOf(">d__", open, StringComparison.Ordinal);
                    if (close > open + 2)
                    {
                        string logicalMethod = typeFullName.Substring(open + 2, close - (open + 2));
                        return typeFullName.Substring(0, open) + "." + logicalMethod;
                    }
                }
            }

            return typeFullName + "." + (string.IsNullOrEmpty(methodName) ? "(unknown)" : methodName);
        }

        // Mono reports script-assembly sources as "./Packages/..." on macOS, as an absolute
        // project path on some Editors, and may use backslashes on Windows; normalize so the
        // payload is a stable project-relative forward-slash path. A rooted path with no
        // recognizable project segment degrades to null so the payload never carries a
        // machine path.
        internal static string NormalizeFilePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string normalized = fileName.Replace('\\', '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return StripToUnityProjectRelative(normalized);
        }

        // Known Unity project-relative roots. Earliest match wins so a duplicate segment
        // deeper in the path cannot hijack the strip point.
        private static readonly string[] ProjectRelativeRootSegments =
        {
            "/Assets/",
            "/Packages/",
            "/Library/PackageCache/"
        };

        private static string StripToUnityProjectRelative(string normalized)
        {
            if (normalized.Length == 0)
            {
                return null;
            }

            if (ContainsParentDirectorySegment(normalized))
            {
                // A ".." segment can relocate the path outside the root the strip and
                // whitelist below appear to guarantee (e.g. "../Assets/Foo.cs" or
                // "Assets/../External/Foo.cs"), so the result would masquerade as a
                // project path or leak non-project structure; degrade to a method-only
                // frame instead.
                return null;
            }

            int earliest = -1;
            foreach (string segment in ProjectRelativeRootSegments)
            {
                int index = normalized.IndexOf(segment, StringComparison.Ordinal);
                if (index >= 0 && (earliest < 0 || index < earliest))
                {
                    earliest = index;
                }
            }

            if (earliest >= 0)
            {
                return normalized.Substring(earliest + 1);
            }

            foreach (string segment in ProjectRelativeRootSegments)
            {
                // The segment minus its leading slash is the project-relative prefix
                // form ("/Assets/" -> "Assets/").
                if (normalized.StartsWith(segment.Substring(1), StringComparison.Ordinal))
                {
                    return normalized;
                }
            }

            // Anything else — rooted machine paths, ../ escapes, unrecognized relative
            // forms — would leak non-project structure into the payload; degrade to a
            // method-only frame instead. Select already coerces Line to 0 when File is
            // null.
            return null;
        }

        // Detects a ".." path segment anywhere in a forward-slash-normalized path.
        private static bool ContainsParentDirectorySegment(string normalized)
        {
            if (normalized == "..")
            {
                return true;
            }

            return normalized.StartsWith("../", StringComparison.Ordinal)
                || normalized.EndsWith("/..", StringComparison.Ordinal)
                || normalized.Contains("/../");
        }
    }
}
