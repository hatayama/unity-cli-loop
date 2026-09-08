using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Explains a missing-type diagnostic whose name is a hot-reload introduced type: dynamic
    /// code compiles against on-disk assemblies only, so the type is invisible here by design.
    /// </summary>
    internal static class IntroducedTypeDiagnosticHint
    {
        private const string TypeOrNamespaceNotFoundErrorCode = "CS0246";
        private const string NamespaceMemberNotFoundErrorCode = "CS0234";

        private const string CompileSuggestion =
            "Run 'uloop compile' when the type is final, then reference it directly";

        /// <summary>
        /// Builds the hint and suggestions for a diagnostic that names an active introduced type.
        /// Returns false when the diagnostic is unrelated, which leaves the existing hints in place.
        /// </summary>
        internal static bool TryBuild(
            string errorCode,
            string message,
            IReadOnlyList<string> activeIntroducedTypeNames,
            out string hint,
            out List<string> suggestions)
        {
            hint = string.Empty;
            suggestions = null;

            if (!IsMissingTypeErrorCode(errorCode))
            {
                return false;
            }

            if (activeIntroducedTypeNames == null || activeIntroducedTypeNames.Count == 0)
            {
                return false;
            }

            string name = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(message);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Why the namespace is only read for CS0234: CS0246 reports an unqualified name, so its
            // message carries no namespace to narrow the match with.
            string namespaceName = string.Equals(errorCode, NamespaceMemberNotFoundErrorCode, StringComparison.Ordinal)
                ? CompilationDiagnosticMessageParser.ExtractNamespaceNameFromMessage(message)
                : null;

            List<string> matches = CollectMatches(activeIntroducedTypeNames, name, namespaceName);
            if (matches.Count == 0)
            {
                return false;
            }

            hint = BuildHint(name, matches);
            suggestions = BuildSuggestions(matches);
            return true;
        }

        private static bool IsMissingTypeErrorCode(string errorCode)
        {
            return string.Equals(errorCode, TypeOrNamespaceNotFoundErrorCode, StringComparison.Ordinal)
                || string.Equals(errorCode, NamespaceMemberNotFoundErrorCode, StringComparison.Ordinal);
        }

        private static List<string> CollectMatches(
            IReadOnlyList<string> activeIntroducedTypeNames,
            string name,
            string namespaceName)
        {
            List<string> matches = new List<string>();
            foreach (string metadataName in activeIntroducedTypeNames)
            {
                if (string.IsNullOrEmpty(metadataName))
                {
                    continue;
                }

                if (!string.Equals(ExtractSimpleName(metadataName), name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsInNamespace(metadataName, namespaceName))
                {
                    continue;
                }

                // Hot reload publishes Cecil metadata names ('Outer/Inner'); the reported name has
                // to be the reflection form, because that is what a suggested lookup compares.
                matches.Add(metadataName.Replace('/', '+'));
            }

            return matches;
        }

        private static string ExtractSimpleName(string metadataName)
        {
            // Both nesting separators are accepted: hot reload publishes Cecil metadata names
            // ('Outer/Inner'), while the same name reads as 'Outer+Inner' through reflection.
            int separatorIndex = metadataName.LastIndexOfAny(new[] { '.', '+', '/' });
            return separatorIndex < 0
                ? metadataName
                : metadataName.Substring(separatorIndex + 1);
        }

        private static bool IsInNamespace(string metadataName, string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
            {
                return true;
            }

            return metadataName.StartsWith(namespaceName + ".", StringComparison.Ordinal);
        }

        private static string BuildHint(string name, List<string> matches)
        {
            if (matches.Count == 1)
            {
                return $"'{name}' is a hot-reload introduced type ({matches[0]}). execute-dynamic-code compiles against the compiled assemblies only, so an introduced type is not visible here until it is compiled. Use reflection through the loaded assembly (AppDomain.CurrentDomain.GetAssemblies) while it is active, or run 'uloop compile' to make it a compiled type.";
            }

            string candidateList = string.Join(", ", matches);
            return $"'{name}' matches these hot-reload introduced types: {candidateList}. execute-dynamic-code compiles against the compiled assemblies only, so none of them is visible here until it is compiled. Pick the one you mean and use reflection through the loaded assembly (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type.";
        }

        private static List<string> BuildSuggestions(List<string> matches)
        {
            List<string> suggestions = new List<string>();
            foreach (string metadataName in matches)
            {
                suggestions.Add(
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())"
                    + $".First(t => t.FullName == \"{metadataName}\") and drive it through reflection");
            }

            suggestions.Add(CompileSuggestion);
            return suggestions;
        }
    }
}
