using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationParsingRules
    {
        internal static readonly ConditionalWeakTable<string, CodeTextMaskHolder> CodeTextMaskCache = new();

        internal static bool IsLegacyAssemblyScopedTypeDeclaration(string source, int typeNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(typeNameIndex >= 0, "typeNameIndex must not be negative");

            string previousIdentifier = ReadPreviousIdentifier(source, typeNameIndex);
            return string.Equals(previousIdentifier, "class", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "struct", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "interface", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "enum", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "using", StringComparison.Ordinal);
        }

        internal static string ReadPreviousIdentifier(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = startIndex - 1;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            int identifierEndIndex = index + 1;
            while (index >= 0 && IsIdentifierCharacter(source[index]))
            {
                index--;
            }

            return source.Substring(index + 1, identifierEndIndex - index - 1);
        }

        internal static int ReadNextNonWhitespaceIndex(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex; index < source.Length; index++)
            {
                if (char.IsWhiteSpace(source[index]))
                {
                    continue;
                }

                return index;
            }

            return source.Length;
        }

        internal static char ReadNextNonWhitespaceCharacter(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex; index < source.Length; index++)
            {
                if (char.IsWhiteSpace(source[index]))
                {
                    continue;
                }

                return source[index];
            }

            return '\0';
        }

        internal static char ReadPreviousNonWhitespaceCharacter(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex - 1; index >= 0; index--)
            {
                if (char.IsWhiteSpace(source[index]))
                {
                    continue;
                }

                return source[index];
            }

            return '\0';
        }

        internal static bool IsDeclarationIdentifierTerminator(char value)
        {
            return value == '{' ||
                value == ';' ||
                value == '=' ||
                value == ')' ||
                value == ',';
        }

        internal static bool PreviousCodeTokenEquals(string source, int endIndex, string token)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(endIndex >= 0, "endIndex must not be negative");
            Debug.Assert(!string.IsNullOrEmpty(token), "token must not be null or empty");

            int tokenEndIndex = endIndex - 1;
            while (tokenEndIndex >= 0 && char.IsWhiteSpace(source[tokenEndIndex]))
            {
                tokenEndIndex--;
            }

            int tokenStartIndex = tokenEndIndex;
            while (tokenStartIndex >= 0 && IsIdentifierCharacter(source[tokenStartIndex]))
            {
                tokenStartIndex--;
            }

            int tokenLength = tokenEndIndex - tokenStartIndex;
            if (tokenLength <= 0)
            {
                return false;
            }

            string previousToken = source.Substring(tokenStartIndex + 1, tokenLength);
            return string.Equals(previousToken, token, StringComparison.Ordinal);
        }

        internal static bool PreviousCodeTokenIsArrow(string source, int endIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(endIndex >= 0, "endIndex must not be negative");

            int tokenEndIndex = endIndex - 1;
            while (tokenEndIndex >= 0 && char.IsWhiteSpace(source[tokenEndIndex]))
            {
                tokenEndIndex--;
            }

            return tokenEndIndex > 0 &&
                source[tokenEndIndex] == '>' &&
                source[tokenEndIndex - 1] == '=';
        }

        internal static bool CanPrecedeDeclarationIdentifier(char value)
        {
            return IsIdentifierCharacter(value) ||
                value == ']' ||
                value == '>';
        }

        internal static bool IsIdentifierCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        internal static bool IsIdentifierStartCharacter(char value)
        {
            return char.IsLetter(value) || value == '_';
        }

        internal static bool ContainsLegacyToolMigrationMarker(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (RegexMatchesCode(source, LegacyNamespaceRegex)) return true;

            return ContainsLegacyToolAttributeList(
                source,
                Array.Empty<string>(),
                canMigrateBareLegacyToolAttribute: false);
        }

        internal static bool ContainsLegacyToolAttributeList(
            string source,
            string[] legacyAssemblyAliases,
            bool canMigrateBareLegacyToolAttribute)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            int index = 0;
            while (index < source.Length)
            {
                if (source[index] != '[' || !codeTextMask.IsCodeAt(index))
                {
                    index++;
                    continue;
                }

                int closingBracketIndex = FindAttributeListClosingBracketIndex(
                    source,
                    codeTextMask,
                    index + 1);
                if (closingBracketIndex < 0)
                {
                    index++;
                    continue;
                }

                string attributesSource = source.Substring(index + 1, closingBracketIndex - index - 1);
                if (TryMigrateLegacyToolAttributeList(
                        attributesSource,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyToolAttribute,
                        out _))
                {
                    return true;
                }

                index = closingBracketIndex + 1;
            }

            return false;
        }

        internal static bool StartsWith(string source, int startIndex, string value)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(value != null, "value must not be null");

            if (startIndex + value.Length > source.Length)
            {
                return false;
            }

            return string.CompareOrdinal(source, startIndex, value, 0, value.Length) == 0;
        }

        internal static bool ContainsTextFragment(string source, string text)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(text), "text must not be null or empty");

            return source.IndexOf(text, StringComparison.Ordinal) >= 0;
        }

        internal static bool IsRawStringStart(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
            int quoteIndex = startIndex + dollarCount;
            return CountRepeatedCharacter(source, quoteIndex, '"') >= MinimumRawStringDelimiterQuoteCount;
        }

        internal static int CountRepeatedCharacter(string source, int startIndex, char character)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = startIndex;
            while (index < source.Length && source[index] == character)
            {
                index++;
            }

            return index - startIndex;
        }

        internal static bool HasRepeatedCharacterAt(string source, int startIndex, char character, int count)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(count > 0, "count must be positive");

            if (startIndex + count > source.Length)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (source[startIndex + i] != character)
                {
                    return false;
                }
            }

            return true;
        }

        internal static int GetStringPrefixLength(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return 2;
            }

            return 1;
        }

        internal readonly struct ReplacementRule
        {
            public ReplacementRule(string pattern, string replacement)
            {
                Debug.Assert(!string.IsNullOrEmpty(pattern), "pattern must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(replacement), "replacement must not be null or empty");

                PatternRegex = new Regex(pattern, RegexOptions.Compiled);
                Replacement = replacement;
            }

            public Regex PatternRegex { get; }
            public string Replacement { get; }
        }

        internal readonly struct TypeReplacementRule
        {
            public TypeReplacementRule(string legacyName, string currentName)
            {
                Debug.Assert(!string.IsNullOrEmpty(legacyName), "legacyName must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(currentName), "currentName must not be null or empty");

                LegacyName = legacyName;
                CurrentName = currentName;
            }

            public string LegacyName { get; }
            public string CurrentName { get; }
        }

        internal readonly struct CodeTextMask
        {
            private readonly bool[] _codeCharacters;

            private CodeTextMask(bool[] codeCharacters)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");

                _codeCharacters = codeCharacters;
            }

            public static CodeTextMask Create(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                return CodeTextMaskCache.GetValue(source, CreateCodeTextMaskHolder).Mask;
            }

            internal static CodeTextMask CreateUncached(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);
                return new CodeTextMask(codeCharacters);
            }

            public bool IsCodeAt(int index)
            {
                if (index < 0 || index >= _codeCharacters.Length)
                {
                    return false;
                }

                return _codeCharacters[index];
            }

        }

        internal sealed class CodeTextMaskHolder
        {
            public CodeTextMaskHolder(CodeTextMask mask)
            {
                Mask = mask;
            }

            public CodeTextMask Mask { get; }
        }

        internal static CodeTextMaskHolder CreateCodeTextMaskHolder(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return new CodeTextMaskHolder(CodeTextMask.CreateUncached(source));
        }
    }
}
