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
