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
        // entry point such as EditorApplication.update is itself diagnostic.
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

            if (IsRootedPath(normalized))
            {
                // A rooted path with no recognizable project segment would leak a machine
                // path into the payload; degrade to a method-only frame instead. Select
                // already coerces Line to 0 when File is null.
                return null;
            }

            return normalized;
        }

        // Detects absolute paths on both platforms: POSIX (and UNC after backslash
        // normalization) via the leading slash, Windows drive paths via "X:/".
        private static bool IsRootedPath(string normalized)
        {
            if (normalized[0] == '/')
            {
                return true;
            }

            return normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '/';
        }
    }
}
