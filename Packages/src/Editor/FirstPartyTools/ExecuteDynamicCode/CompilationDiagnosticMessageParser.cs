using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parses Compilation Diagnostic Message data into the model used by this module.
    /// </summary>
    public static class CompilationDiagnosticMessageParser
    {
        private static readonly Regex TypeNamePattern = new Regex(@"['""]([^'""]+)['""]", RegexOptions.Compiled);

        public static string ExtractTypeNameFromMessage(string message)
        {
            if (message == null)
            {
                return null;
            }

            Match match = TypeNamePattern.Match(message);
            if (!match.Success)
            {
                return null;
            }

            return NormalizeTypeName(match.Groups[1].Value);
        }

        /// <summary>
        /// Returns the namespace of a CS0234 message, whose second quoted phrase is the namespace
        /// the missing type was looked for in. Null when the message has no second quoted phrase.
        /// </summary>
        public static string ExtractNamespaceNameFromMessage(string message)
        {
            if (message == null)
            {
                return null;
            }

            MatchCollection matches = TypeNamePattern.Matches(message);
            if (matches.Count < 2)
            {
                return null;
            }

            string rawNamespace = matches[1].Groups[1].Value;
            if (string.IsNullOrWhiteSpace(rawNamespace))
            {
                return null;
            }

            return rawNamespace.Trim();
        }

        private static string NormalizeTypeName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return null;
            }

            string normalized = rawName.Trim();
            int genericIndex = normalized.IndexOf('<');
            if (genericIndex > 0)
            {
                normalized = normalized.Substring(0, genericIndex);
            }

            return normalized;
        }
    }
}
