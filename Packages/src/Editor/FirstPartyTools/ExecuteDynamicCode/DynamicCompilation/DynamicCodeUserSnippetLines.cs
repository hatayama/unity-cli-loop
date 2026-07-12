namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Splits user-authored execute-dynamic-code snippets into diagnostic context lines.
    /// </summary>
    internal static class DynamicCodeUserSnippetLines
    {
        internal static string[] Split(string userSnippet)
        {
            if (string.IsNullOrEmpty(userSnippet))
            {
                return System.Array.Empty<string>();
            }

            string[] rawLines = userSnippet.Split('\n');
            string[] lines = new string[rawLines.Length];
            for (int index = 0; index < rawLines.Length; index++)
            {
                lines[index] = rawLines[index].TrimEnd('\r');
            }

            return TrimTrailingEmptyLines(lines);
        }

        internal static string[] TrimTrailingEmptyLines(string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return System.Array.Empty<string>();
            }

            int lastNonEmptyIndex = lines.Length - 1;
            while (lastNonEmptyIndex >= 0 && lines[lastNonEmptyIndex].Length == 0)
            {
                lastNonEmptyIndex--;
            }

            if (lastNonEmptyIndex < 0)
            {
                return System.Array.Empty<string>();
            }

            if (lastNonEmptyIndex == lines.Length - 1)
            {
                return lines;
            }

            string[] trimmedLines = new string[lastNonEmptyIndex + 1];
            for (int index = 0; index <= lastNonEmptyIndex; index++)
            {
                trimmedLines[index] = lines[index];
            }

            return trimmedLines;
        }
    }
}
