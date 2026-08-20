using System.Globalization;
using System.Text.RegularExpressions;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Best-effort File/Line extraction from a failed-test stack trace.
    /// </summary>
    internal static class FailedTestStackLocationParser
    {
        // Spec form used in some traces: "(at Assets/Foo.cs:12)".
        private static readonly Regex ParenthesizedAtPathLineRegex = new Regex(
            @"\(at (?<file>.+?):(?<line>\d+)\)",
            RegexOptions.CultureInvariant);

        // Why a second pattern: Unity Test Runner stack traces write
        // "at Type.Method () [0x00000] in Assets/Foo.cs:12" without parentheses around the path.
        private static readonly Regex InPathLineRegex = new Regex(
            @"\bin (?<file>.+?):(?<line>\d+)",
            RegexOptions.CultureInvariant);

        internal static (string File, int? Line) TryParse(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return (null, null);
            }

            (string file, int? line) parenthesized = TryMatch(ParenthesizedAtPathLineRegex, stackTrace);
            if (parenthesized.file != null)
            {
                return parenthesized;
            }

            return TryMatch(InPathLineRegex, stackTrace);
        }

        private static (string File, int? Line) TryMatch(Regex regex, string stackTrace)
        {
            Match match = regex.Match(stackTrace);
            if (!match.Success)
            {
                return (null, null);
            }

            int line = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            Debug.Assert(line >= 0, "parsed stack line must be non-negative.");
            return (match.Groups["file"].Value, line);
        }
    }
}
