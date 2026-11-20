// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity, Newtonsoft and Roslyn references.
using System;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The one naming scheme for base-call stand-ins: the worker names a shim-side static method
    /// after the base method a `base.M(args)` call targets, and the Editor parses that name back
    /// to find the real base method when it transplants the shim body.
    /// </summary>
    internal static class HotReloadBaseCallStandInNames
    {
        internal const string Prefix = "__uloopBaseCall__";

        private const string Separator = "__";

        /// <summary>
        /// The stand-in method name for a base call <paramref name="depth"/> types above the
        /// patched method's type.
        /// </summary>
        internal static string Format(int depth, string methodName, int index)
        {
            return Prefix
                + "d" + depth.ToString(CultureInfo.InvariantCulture)
                + Separator + methodName
                + Separator + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Splits a stand-in name back into depth and base method name; false when
        /// <paramref name="name"/> is not a stand-in name.
        /// </summary>
        internal static bool TryParse(string name, out int depth, out string methodName)
        {
            depth = 0;
            methodName = null;
            if (name == null || !name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string rest = name.Substring(Prefix.Length);
            if (rest.Length == 0 || rest[0] != 'd')
            {
                return false;
            }

            // Why the first and last separator: a method name may itself contain "__", but the
            // depth never does and the index is always the final segment.
            int firstSeparator = rest.IndexOf(Separator, StringComparison.Ordinal);
            int lastSeparator = rest.LastIndexOf(Separator, StringComparison.Ordinal);
            // Why the full separator length: in "d1___0" the two separators overlap.
            if (firstSeparator < 0 || lastSeparator < firstSeparator + Separator.Length)
            {
                return false;
            }

            if (!TryParseNumber(rest.Substring(1, firstSeparator - 1), out int parsedDepth) || parsedDepth < 1)
            {
                return false;
            }

            if (!TryParseNumber(rest.Substring(lastSeparator + Separator.Length), out int _))
            {
                return false;
            }

            string parsedName = rest.Substring(firstSeparator + Separator.Length, lastSeparator - firstSeparator - Separator.Length);
            if (parsedName.Length == 0)
            {
                return false;
            }

            depth = parsedDepth;
            methodName = parsedName;
            return true;
        }

        private static bool TryParseNumber(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
