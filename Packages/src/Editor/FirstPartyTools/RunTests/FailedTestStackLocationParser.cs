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
        // Why 9 not Int32.MaxValue's 10 digits: 9 ASCII digits never overflow int.Parse.
        private const int MaxSafeAsciiLineDigitCount = 9;

        // Spec form used in some traces: "(at Assets/Foo.cs:12)".
        // Why [0-9] not \d: \d matches Unicode Nd digits that int.Parse rejects with FormatException.
        private static readonly Regex ParenthesizedAtPathLineRegex = new Regex(
            @"\(at (?<file>.+?):(?<line>[0-9]+)\)",
            RegexOptions.CultureInvariant);

        // Why a second pattern: Unity Test Runner stack traces write
        // "at Type.Method () [0x00000] in Assets/Foo.cs:12" without parentheses around the path.
        private static readonly Regex InPathLineRegex = new Regex(
            @"\bin (?<file>.+?):(?<line>[0-9]+)",
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

            string lineText = match.Groups["line"].Value;
            // Why not {1,9} in the regex: InPathLineRegex has no trailing anchor, so the
            // engine would backtrack and match the first 9 digits of a longer number.
            if (lineText.Length > MaxSafeAsciiLineDigitCount)
            {
                return (null, null);
            }

            int line = int.Parse(lineText, CultureInfo.InvariantCulture);
            Debug.Assert(line >= 0, "parsed stack line must be non-negative.");
            return (match.Groups["file"].Value, line);
        }
    }
}
