using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Extracts the user-authored snippet region from wrapped execute-dynamic-code sources.
    /// </summary>
    internal static class WrappedDynamicCodeUserSnippetExtractor
    {
        internal static bool TryExtract(string wrappedSource, out string userSnippet)
        {
            userSnippet = string.Empty;
            if (string.IsNullOrEmpty(wrappedSource))
            {
                return false;
            }

            int startIndex = wrappedSource.IndexOf(WrapperTemplate.UserCodeStartMarker, System.StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            int codeStart = wrappedSource.IndexOf('\n', startIndex);
            if (codeStart < 0)
            {
                return false;
            }
            codeStart++;

            int endIndex = wrappedSource.IndexOf(WrapperTemplate.UserCodeEndMarker, codeStart, System.StringComparison.Ordinal);
            if (endIndex < 0)
            {
                userSnippet = wrappedSource.Substring(codeStart);
                return true;
            }

            userSnippet = wrappedSource.Substring(codeStart, endIndex - codeStart);
            return true;
        }

        internal static string[] SplitNormalizedLines(string userSnippet)
        {
            Debug.Assert(userSnippet != null, "userSnippet must not be null");

            if (userSnippet.Length == 0)
            {
                return System.Array.Empty<string>();
            }

            string[] rawLines = userSnippet.Split('\n');
            string[] normalizedLines = new string[rawLines.Length];
            for (int index = 0; index < rawLines.Length; index++)
            {
                normalizedLines[index] = rawLines[index].TrimStart().TrimEnd('\r');
            }

            return normalizedLines;
        }
    }
}
