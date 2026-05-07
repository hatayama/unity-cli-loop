
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects whether top-level code contains a return statement.
    /// Excludes returns inside lambdas and local functions by tracking brace depth.
    /// Delegates literal/comment skipping to SourceShaper.AdvanceOneToken to avoid duplication.
    /// </summary>
    internal static class TopLevelReturnDetector
    {
        public static bool HasTopLevelReturn(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;

            int length = body.Length;
            int pos = 0;
            int braceDepth = 0;

            while (pos < length)
            {
                char c = body[pos];

                if (c == '{') { braceDepth++; pos++; continue; }
                if (c == '}') { braceDepth--; pos++; continue; }

                if (braceDepth == 0 && c == 'r' && StartsWithReturn(body, pos, length))
                {
                    return true;
                }

                // Delegate all literal/comment skipping to shared scanner
                int next = SourceShaper.AdvanceOneTokenPublic(body, pos);
                if (next == pos) { pos++; continue; }
                pos = next;
            }

            return false;
        }

        private static bool StartsWithReturn(string s, int pos, int length)
        {
            // "return" must not be part of a longer identifier (e.g. "myreturn", "returnValue")
            if (pos > 0 && (char.IsLetterOrDigit(s[pos - 1]) || s[pos - 1] == '_'))
            {
                return false;
            }
            if (pos + 6 > length) return false;
            if (s[pos + 1] != 'e' || s[pos + 2] != 't' || s[pos + 3] != 'u' ||
                s[pos + 4] != 'r' || s[pos + 5] != 'n')
            {
                return false;
            }
            int afterReturn = pos + 6;
            if (afterReturn < length && (char.IsLetterOrDigit(s[afterReturn]) || s[afterReturn] == '_'))
            {
                return false;
            }
            return true;
        }
    }
}
