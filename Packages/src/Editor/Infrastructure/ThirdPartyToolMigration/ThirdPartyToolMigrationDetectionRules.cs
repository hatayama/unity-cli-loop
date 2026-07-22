using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;

using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationDetectionRules
    {
        private const string AttributeSuffix = "Attribute";

        /// <summary>
        /// Compile-error-message tokens that identify V2 legacy API usage. Derived from
        /// ThirdPartyToolMigrationRuleCatalog (the single source for legacy/current names) rather than
        /// duplicated here, and limited to names that actually changed between V2 and V3 so unrelated
        /// V3-only compile errors do not false-positive on names that stayed the same (e.g. ServiceResult).
        /// </summary>
        internal static readonly string[] LegacyApiTokens = BuildLegacyApiTokens();

        private static string[] BuildLegacyApiTokens()
        {
            List<string> tokens = new List<string>
            {
                LegacyNamespace.Split('.')[^1]
            };

            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
                if (string.Equals(rule.LegacyName, rule.CurrentName, StringComparison.Ordinal))
                {
                    continue;
                }

                tokens.Add(rule.LegacyName);

                if (rule.LegacyName.EndsWith(AttributeSuffix, StringComparison.Ordinal))
                {
                    tokens.Add(rule.LegacyName[..^AttributeSuffix.Length]);
                }
            }

            return tokens.ToArray();
        }

        /// <summary>
        /// Checks whether a compile-error message body contains any V2 legacy API token at an
        /// identifier boundary. This is a fast, non-scanning shortcut for the auto-scan trigger only;
        /// false positives are harmless because the assembly-scoped scan that follows is the source of
        /// truth and simply finds zero targets when a match turns out to be unrelated.
        /// </summary>
        internal static bool ContainsLegacyApiToken(string message)
        {
            Debug.Assert(message != null, "message must not be null");

            foreach (string token in LegacyApiTokens)
            {
                if (ContainsTokenAtIdentifierBoundary(message, token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTokenAtIdentifierBoundary(string text, string token)
        {
            int searchStart = 0;
            while (true)
            {
                int matchIndex = text.IndexOf(token, searchStart, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    return false;
                }

                bool hasLeadingBoundary = matchIndex == 0 || !IsIdentifierCharacter(text[matchIndex - 1]);
                int afterMatchIndex = matchIndex + token.Length;
                bool hasTrailingBoundary = afterMatchIndex == text.Length || !IsIdentifierCharacter(text[afterMatchIndex]);

                if (hasLeadingBoundary && hasTrailingBoundary)
                {
                    return true;
                }

                searchStart = matchIndex + 1;
            }
        }

        private static bool IsIdentifierCharacter(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_';
        }

        internal static bool ContainsLegacyAsmdefNameReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, LegacyEditorAssemblyName) &&
                !ContainsTextFragment(source, LegacyRuntimeAssemblyName))
            {
                return false;
            }

            JObject asmdef = JObject.Parse(source);
            if (asmdef["references"] is not JArray references)
            {
                return false;
            }

            foreach (JToken reference in references)
            {
                string referenceValue = reference.Value<string>() ?? string.Empty;
                if (string.Equals(referenceValue, LegacyEditorAssemblyName, StringComparison.Ordinal) ||
                    string.Equals(referenceValue, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
