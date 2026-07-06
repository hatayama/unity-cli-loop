using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Detects C# source text matches that are outside comments and string literals.
    /// </summary>
    public static class ThirdPartyToolMigrationCodeTextDetectionRules
    {
        public static string[] GetDeclaredTypeNames(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            HashSet<string> typeNames = new(StringComparer.Ordinal);
            MatchCollection matches = TypeDeclarationNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                typeNames.Add(match.Groups["name"].Value);
            }

            return typeNames
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToArray();
        }

        public static bool RegexMatchesCode(string source, Regex regex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");

            MatchCollection matches = regex.Matches(source);
            if (matches.Count == 0)
            {
                return false;
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
