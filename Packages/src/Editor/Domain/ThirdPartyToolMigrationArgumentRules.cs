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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
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
    public static class ThirdPartyToolMigrationArgumentRules
    {
        public static string[] SplitAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            AttributeArgumentSplitter splitter = new(argumentsSource);
            return splitter.Split();
        }

        private enum AttributeArgumentScanMode
        {
            None,
            RegularString,
            VerbatimString,
            CharLiteral,
            RawString,
            LineComment,
            BlockComment
        }

        /// <summary>
        /// Splits attribute argument text while ignoring commas inside nested syntax and literals.
        /// </summary>
        private sealed class AttributeArgumentSplitter
        {
            private readonly string _source;
            private readonly List<string> _arguments = new();
            private int _argumentStartIndex;
            private int _nestingDepth;
            private AttributeArgumentScanMode _mode;
            private int _rawStringQuoteCount;

            internal AttributeArgumentSplitter(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                _source = source;
            }

            internal string[] Split()
            {
                for (int index = 0; index < _source.Length; index++)
                {
                    index = ConsumeCharacter(index);
                }

                _arguments.Add(_source.Substring(_argumentStartIndex));
                return _arguments.ToArray();
            }

            private int ConsumeCharacter(int index)
            {
                if (_mode != AttributeArgumentScanMode.None)
                {
                    return ConsumeActiveMode(index);
                }

                (bool enteredComment, int commentIndex) = TryEnterComment(index);
                if (enteredComment)
                {
                    return commentIndex;
                }

                (bool enteredLiteral, int nextIndex) = TryEnterLiteral(index);
                if (enteredLiteral)
                {
                    return nextIndex;
                }

                char current = _source[index];
                if (current == '(' || current == '[' || current == '{')
                {
                    _nestingDepth++;
                    return index;
                }

                if (current == ')' || current == ']' || current == '}')
                {
                    _nestingDepth = Math.Max(0, _nestingDepth - 1);
                    return index;
                }

                if (current == ',' && _nestingDepth == 0)
                {
                    _arguments.Add(_source.Substring(_argumentStartIndex, index - _argumentStartIndex));
                    _argumentStartIndex = index + 1;
                }

                return index;
            }

            private int ConsumeActiveMode(int index)
            {
                switch (_mode)
                {
                    case AttributeArgumentScanMode.RegularString:
                        return ConsumeRegularString(index);
                    case AttributeArgumentScanMode.VerbatimString:
                        return ConsumeVerbatimString(index);
                    case AttributeArgumentScanMode.CharLiteral:
                        return ConsumeCharLiteral(index);
                    case AttributeArgumentScanMode.RawString:
                        return ConsumeRawString(index);
                    case AttributeArgumentScanMode.LineComment:
                        return ConsumeLineComment(index);
                    case AttributeArgumentScanMode.BlockComment:
                        return ConsumeBlockComment(index);
                    default:
                        return index;
                }
            }

            private int ConsumeRegularString(int index)
            {
                if (_source[index] == '\\')
                {
                    return index + 1;
                }

                if (_source[index] == '"')
                {
                    _mode = AttributeArgumentScanMode.None;
                }

                return index;
            }

            private int ConsumeVerbatimString(int index)
            {
                if (_source[index] != '"')
                {
                    return index;
                }

                if (index + 1 < _source.Length && _source[index + 1] == '"')
                {
                    return index + 1;
                }

                _mode = AttributeArgumentScanMode.None;
                return index;
            }

            private int ConsumeCharLiteral(int index)
            {
                if (_source[index] == '\\')
                {
                    return index + 1;
                }

                if (_source[index] == '\'')
                {
                    _mode = AttributeArgumentScanMode.None;
                }

                return index;
            }

            private int ConsumeRawString(int index)
            {
                if (HasRepeatedCharacterAt(_source, index, '"', _rawStringQuoteCount))
                {
                    _mode = AttributeArgumentScanMode.None;
                    return index + _rawStringQuoteCount - 1;
                }

                return index;
            }

            private int ConsumeLineComment(int index)
            {
                if (_source[index] == '\n' || _source[index] == '\r')
                {
                    _mode = AttributeArgumentScanMode.None;
                }

                return index;
            }

            private int ConsumeBlockComment(int index)
            {
                if (StartsWith(_source, index, "*/"))
                {
                    _mode = AttributeArgumentScanMode.None;
                    return index + 1;
                }

                return index;
            }

            private (bool EnteredComment, int NextIndex) TryEnterComment(int index)
            {
                if (StartsWith(_source, index, "//"))
                {
                    _mode = AttributeArgumentScanMode.LineComment;
                    return (true, index + 1);
                }

                if (StartsWith(_source, index, "/*"))
                {
                    _mode = AttributeArgumentScanMode.BlockComment;
                    return (true, index + 1);
                }

                return (false, index);
            }

            private (bool EnteredLiteral, int NextIndex) TryEnterLiteral(int index)
            {
                if (IsRawStringStart(_source, index))
                {
                    int dollarCount = CountRepeatedCharacter(_source, index, '$');
                    int quoteIndex = index + dollarCount;
                    _rawStringQuoteCount = CountRepeatedCharacter(_source, quoteIndex, '"');
                    _mode = AttributeArgumentScanMode.RawString;
                    return (true, quoteIndex + _rawStringQuoteCount - 1);
                }

                if (StartsWith(_source, index, "@\"") ||
                    StartsWith(_source, index, "$@\"") ||
                    StartsWith(_source, index, "@$\""))
                {
                    _mode = AttributeArgumentScanMode.VerbatimString;
                    return (true, index + GetStringPrefixLength(_source, index));
                }

                if (StartsWith(_source, index, "$\""))
                {
                    int interpolatedStringEndIndex =
                        ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(
                            _source,
                            index);
                    if (interpolatedStringEndIndex >= 0)
                    {
                        return (true, interpolatedStringEndIndex);
                    }

                    _mode = AttributeArgumentScanMode.RegularString;
                    return (true, index + 1);
                }

                if (_source[index] == '"')
                {
                    _mode = AttributeArgumentScanMode.RegularString;
                    return (true, index);
                }

                if (_source[index] == '\'')
                {
                    _mode = AttributeArgumentScanMode.CharLiteral;
                    return (true, index);
                }

                return (false, index);
            }
        }

        public static string GetNamedArgumentValueOrNull(string argument, string argumentName)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(!string.IsNullOrEmpty(argumentName), "argumentName must not be null or empty");

            int colonIndex = FindNamedArgumentColonIndex(argument);
            if (colonIndex <= 0)
            {
                return null;
            }

            string possibleArgumentName = argument.Substring(0, colonIndex).Trim();
            if (!string.Equals(possibleArgumentName, argumentName, StringComparison.Ordinal))
            {
                return null;
            }

            string value = argument.Substring(colonIndex + 1).Trim();
            return value.Length == 0 ? null : value;
        }

        public static int FindNamedArgumentColonIndex(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            for (int index = 0; index < argument.Length; index++)
            {
                if (argument[index] != ':')
                {
                    continue;
                }

                bool isAliasQualifierColon =
                    (index + 1 < argument.Length && argument[index + 1] == ':') ||
                    (index > 0 && argument[index - 1] == ':');
                if (isAliasQualifierColon)
                {
                    continue;
                }

                return index;
            }

            return -1;
        }

        public static int FindInvocationClosingParenthesisIndex(
            string source,
            CodeTextMask codeTextMask,
            int openParenthesisIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openParenthesisIndex >= 0, "openParenthesisIndex must not be negative");

            int nestedParenthesisDepth = 0;
            for (int i = openParenthesisIndex + 1; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '(')
                {
                    nestedParenthesisDepth++;
                    continue;
                }

                if (source[i] != ')')
                {
                    continue;
                }

                if (nestedParenthesisDepth == 0)
                {
                    return i;
                }

                nestedParenthesisDepth--;
            }

            return -1;
        }

        public static bool IsNamedAttributeArgument(string argument, string argumentName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(argument), "argument must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(argumentName), "argumentName must not be null or whitespace");

            if (!argument.StartsWith(argumentName, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = argumentName.Length; i < argument.Length; i++)
            {
                char current = argument[i];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                return current == '=';
            }

            return false;
        }

    }
}
