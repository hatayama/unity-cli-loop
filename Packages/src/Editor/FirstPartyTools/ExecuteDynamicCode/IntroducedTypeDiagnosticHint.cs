using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Explains a missing-type diagnostic whose name is a hot-reload introduced type: the type is
    /// active and its assembly is referenced, so the name the snippet used is what the compiler
    /// could not resolve.
    /// </summary>
    internal static class IntroducedTypeDiagnosticHint
    {
        private const string TypeOrNamespaceNotFoundErrorCode = "CS0246";
        private const string NamespaceMemberNotFoundErrorCode = "CS0234";

        // An introduced type referred to by its simple name is reported as an unknown identifier
        // rather than an unknown type, because the compiler has no such name in scope at all.
        private const string NameNotInContextErrorCode = "CS0103";

        private const string CompileSuggestion =
            "Run 'uloop compile' when the type is final, then reference it as an ordinary compiled type";

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
                || string.Equals(errorCode, NamespaceMemberNotFoundErrorCode, StringComparison.Ordinal)
                || string.Equals(errorCode, NameNotInContextErrorCode, StringComparison.Ordinal);
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

                if (!MatchesSimpleName(metadataName, name, namespaceName)
                    && !MatchesNamespaceSegment(metadataName, name, namespaceName))
                {
                    continue;
                }

                // The metadata name is kept as hot reload published it ('Outer/Inner'); the two
                // spellings the hint needs are derived from it where they are used.
                matches.Add(metadataName);
            }

            return matches;
        }

        private static bool MatchesSimpleName(string metadataName, string name, string namespaceName)
        {
            return string.Equals(ExtractSimpleName(metadataName), name, StringComparison.Ordinal)
                && IsInNamespace(metadataName, namespaceName);
        }

        // Why a namespace segment counts as a match: a qualified reference to an introduced type
        // fails on the first segment the compiler cannot resolve, so the reported name is one of
        // the type's namespaces rather than the type itself.
        private static bool MatchesNamespaceSegment(string metadataName, string name, string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
            {
                return false;
            }

            return metadataName.StartsWith(namespaceName + "." + name + ".", StringComparison.Ordinal);
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
                return $"'{name}' is a hot-reload introduced type ({ToSourceName(matches[0])}), and the assembly holding it is referenced by this compilation while it stays active, so the name is what did not resolve. {DescribeHowToName(matches[0])}. Members that hot reload added to it are separate: those are not visible here at all, and not through reflection either; only code edited in the same reload sees them. If the name still does not resolve, reach the type through reflection (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type.";
            }

            List<string> sourceNames = new List<string>();
            foreach (string metadataName in matches)
            {
                sourceNames.Add(ToSourceName(metadataName));
            }

            string candidateList = string.Join(", ", sourceNames);
            return $"'{name}' matches these hot-reload introduced types: {candidateList}. The assembly holding each one is referenced by this compilation while it stays active, so pick the one you mean and write its full name as spelled here. Members that hot reload added to them are separate: those are not visible here at all, and not through reflection either; only code edited in the same reload sees them. If the name still does not resolve, reach the type through reflection (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type.";
        }

        private static List<string> BuildSuggestions(List<string> matches)
        {
            List<string> suggestions = new List<string>();
            foreach (string metadataName in matches)
            {
                suggestions.Add(DescribeHowToName(metadataName));
                suggestions.Add(
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())"
                    + $".First(t => t.FullName == \"{ToReflectionName(metadataName)}\") and drive it through reflection");
            }

            suggestions.Add(CompileSuggestion);
            return suggestions;
        }

        // Why the shape of the name decides the wording: a using directive shortens a namespace
        // only, so it can stand in for the namespace of a top-level type, for nothing of a nested
        // one, and for nothing at all when the type sits in the global namespace.
        private static string DescribeHowToName(string metadataName)
        {
            string sourceName = ToSourceName(metadataName);
            if (IsNested(metadataName))
            {
                return $"Write it as {sourceName}; a using does not bring the simple name of a nested type into scope";
            }

            if (!HasNamespace(metadataName))
            {
                return $"Write it as {sourceName}, which is already its full name";
            }

            return $"Write it as {sourceName}, or add a using for its namespace";
        }

        // C# source nests with '.', so this is the spelling a snippet has to use.
        private static string ToSourceName(string metadataName)
        {
            return metadataName.Replace('/', '.');
        }

        // A reflection FullName nests with '+', so this is the spelling a lookup compares against.
        private static string ToReflectionName(string metadataName)
        {
            return metadataName.Replace('/', '+');
        }

        private static bool IsNested(string metadataName)
        {
            return metadataName.IndexOf('/') >= 0;
        }

        private static bool HasNamespace(string metadataName)
        {
            int nestingIndex = metadataName.IndexOf('/');
            string topLevelName = nestingIndex < 0 ? metadataName : metadataName.Substring(0, nestingIndex);
            return topLevelName.IndexOf('.') >= 0;
        }
    }
}
