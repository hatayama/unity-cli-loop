using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;


using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Domain.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationParsingRules
    {
        public static readonly ConditionalWeakTable<string, CodeTextMaskHolder> CodeTextMaskCache = new();

        public static bool IsLegacyAssemblyScopedTypeDeclaration(string source, int typeNameIndex)
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

        public static string ReadPreviousIdentifier(string source, int startIndex)
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

        public static int ReadNextNonWhitespaceIndex(string source, int startIndex)
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

        public static char ReadNextNonWhitespaceCharacter(string source, int startIndex)
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

        public static char ReadPreviousNonWhitespaceCharacter(string source, int startIndex)
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

        public static bool IsDeclarationIdentifierTerminator(char value)
        {
            return value == '{' ||
                value == ';' ||
                value == '=' ||
                value == ')' ||
                value == ',';
        }

        public static bool PreviousCodeTokenEquals(string source, int endIndex, string token)
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

        public static bool PreviousCodeTokenIsArrow(string source, int endIndex)
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

        public static bool CanPrecedeDeclarationIdentifier(char value)
        {
            return IsIdentifierCharacter(value) ||
                value == ']' ||
                value == '>';
        }

        public static bool IsIdentifierCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        public static bool IsIdentifierStartCharacter(char value)
        {
            return char.IsLetter(value) || value == '_';
        }

        public static bool ContainsLegacyToolMigrationMarker(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (RegexMatchesCode(source, LegacyNamespaceRegex)) return true;

            return ContainsLegacyToolAttributeList(
                source,
                Array.Empty<string>(),
                canMigrateBareLegacyToolAttribute: false);
        }

        public static bool ContainsLegacyToolAttributeList(
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

        public static bool StartsWith(string source, int startIndex, string value)
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

        public static bool ContainsTextFragment(string source, string text)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(text), "text must not be null or empty");

            return source.IndexOf(text, StringComparison.Ordinal) >= 0;
        }

        public static bool IsRawStringStart(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
            int quoteIndex = startIndex + dollarCount;
            return CountRepeatedCharacter(source, quoteIndex, '"') >= MinimumRawStringDelimiterQuoteCount;
        }

        public static int CountRepeatedCharacter(string source, int startIndex, char character)
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

        public static bool HasRepeatedCharacterAt(string source, int startIndex, char character, int count)
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

        public static int GetStringPrefixLength(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return 2;
            }

            return 1;
        }

        public readonly struct ReplacementRule
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

        public readonly struct TypeReplacementRule
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

        public readonly struct CodeTextMask
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

            public static CodeTextMask CreateUncached(string source)
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

        public sealed class CodeTextMaskHolder
        {
            public CodeTextMaskHolder(CodeTextMask mask)
            {
                Mask = mask;
            }

            public CodeTextMask Mask { get; }
        }

        public static CodeTextMaskHolder CreateCodeTextMaskHolder(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return new CodeTextMaskHolder(CodeTextMask.CreateUncached(source));
        }
    }
}
