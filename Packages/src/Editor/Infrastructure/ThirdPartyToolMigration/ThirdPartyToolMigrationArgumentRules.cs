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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
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
    internal static class ThirdPartyToolMigrationArgumentRules
    {
        internal static string[] SplitAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            List<string> arguments = new();
            int argumentStartIndex = 0;
            int nestingDepth = 0;
            bool isInRegularString = false;
            bool isInVerbatimString = false;
            bool isInCharLiteral = false;
            bool isInRawString = false;
            int rawStringQuoteCount = 0;
            for (int i = 0; i < argumentsSource.Length; i++)
            {
                char current = argumentsSource[i];
                if (isInRegularString)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"')
                    {
                        isInRegularString = false;
                    }

                    continue;
                }

                if (isInVerbatimString)
                {
                    if (current != '"')
                    {
                        continue;
                    }

                    if (i + 1 < argumentsSource.Length && argumentsSource[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    isInVerbatimString = false;
                    continue;
                }

                if (isInRawString)
                {
                    if (HasRepeatedCharacterAt(argumentsSource, i, '"', rawStringQuoteCount))
                    {
                        i += rawStringQuoteCount - 1;
                        isInRawString = false;
                    }

                    continue;
                }

                if (isInCharLiteral)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '\'')
                    {
                        isInCharLiteral = false;
                    }

                    continue;
                }

                if (IsRawStringStart(argumentsSource, i))
                {
                    int dollarCount = CountRepeatedCharacter(argumentsSource, i, '$');
                    int quoteIndex = i + dollarCount;
                    rawStringQuoteCount = CountRepeatedCharacter(argumentsSource, quoteIndex, '"');
                    isInRawString = true;
                    i = quoteIndex + rawStringQuoteCount - 1;
                    continue;
                }

                if (StartsWith(argumentsSource, i, "@\"") ||
                    StartsWith(argumentsSource, i, "$@\"") ||
                    StartsWith(argumentsSource, i, "@$\""))
                {
                    isInVerbatimString = true;
                    i += GetStringPrefixLength(argumentsSource, i);
                    continue;
                }

                if (StartsWith(argumentsSource, i, "$\""))
                {
                    int interpolatedStringEndIndex =
                        ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(
                            argumentsSource,
                            i);
                    if (interpolatedStringEndIndex >= 0)
                    {
                        i = interpolatedStringEndIndex;
                        continue;
                    }

                    isInRegularString = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    isInRegularString = true;
                    continue;
                }

                if (current == '\'')
                {
                    isInCharLiteral = true;
                    continue;
                }

                if (current == '(' || current == '[' || current == '{')
                {
                    nestingDepth++;
                    continue;
                }

                if (current == ')' || current == ']' || current == '}')
                {
                    nestingDepth = Math.Max(0, nestingDepth - 1);
                    continue;
                }

                if (current != ',' || nestingDepth != 0)
                {
                    continue;
                }

                arguments.Add(argumentsSource.Substring(argumentStartIndex, i - argumentStartIndex));
                argumentStartIndex = i + 1;
            }

            arguments.Add(argumentsSource.Substring(argumentStartIndex));
            return arguments.ToArray();
        }

        internal static string GetNamedArgumentValueOrNull(string argument, string argumentName)
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

        internal static int FindNamedArgumentColonIndex(string argument)
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

        internal static int FindInvocationClosingParenthesisIndex(
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

        internal static bool IsNamedAttributeArgument(string argument, string argumentName)
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
