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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
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
    internal static class ThirdPartyToolMigrationScreenshotArgumentRules
    {
        internal static string[] GetMigratedEditorWindowCaptureUtilityArguments(
            string[] arguments,
            string timeoutExpression)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(!string.IsNullOrEmpty(timeoutExpression), "timeoutExpression must not be null or empty");

            string[] trimmedArguments = GetTrimmedInvocationArguments(arguments);
            if (trimmedArguments.Length != 3)
            {
                return Array.Empty<string>();
            }

            string[] orderedArguments = GetOrderedEditorWindowCaptureUtilityArguments(trimmedArguments);
            if (orderedArguments.Length == 0)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                orderedArguments[0],
                orderedArguments[1],
                timeoutExpression,
                orderedArguments[2]
            };
        }

        internal static string[] GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(
            string[] arguments,
            string timeoutExpression)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(!string.IsNullOrEmpty(timeoutExpression), "timeoutExpression must not be null or empty");

            string[] trimmedArguments = GetTrimmedInvocationArguments(arguments);
            if (trimmedArguments.Length == 3)
            {
                return Array.Empty<string>();
            }

            if (trimmedArguments.Length != 2)
            {
                return Array.Empty<string>();
            }

            string[] orderedArguments = GetOrderedEditorWindowCaptureUtilityGameRenderingArguments(trimmedArguments);
            if (orderedArguments.Length == 0)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                orderedArguments[0],
                timeoutExpression,
                orderedArguments[1]
            };
        }

        internal static string[] GetTrimmedInvocationArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            return arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
        }

        internal static string[] GetOrderedEditorWindowCaptureUtilityArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            string[] orderedArguments = new string[3];
            int nextPosition = 0;
            foreach (string argument in arguments)
            {
                (int position, string namedArgument) = GetEditorWindowCaptureUtilityNamedArgument(argument);
                if (position >= 0)
                {
                    if (!TryAssignEditorWindowCaptureUtilityArgument(orderedArguments, position, namedArgument))
                    {
                        return Array.Empty<string>();
                    }

                    continue;
                }

                nextPosition = GetNextUnassignedEditorWindowCaptureUtilityArgumentPosition(
                    orderedArguments,
                    nextPosition);
                if (nextPosition >= orderedArguments.Length)
                {
                    return Array.Empty<string>();
                }

                orderedArguments[nextPosition] = nextPosition == 2
                    ? GetArgumentWithMigratedCancellationTokenName(argument)
                    : argument;
                nextPosition++;
            }

            return orderedArguments.Any(argument => argument == null)
                ? Array.Empty<string>()
                : orderedArguments;
        }

        internal static string[] GetOrderedEditorWindowCaptureUtilityGameRenderingArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            string[] orderedArguments = new string[2];
            int nextPosition = 0;
            foreach (string argument in arguments)
            {
                (int position, string namedArgument) =
                    GetEditorWindowCaptureUtilityGameRenderingNamedArgument(argument);
                if (position >= 0)
                {
                    if (!TryAssignEditorWindowCaptureUtilityArgument(orderedArguments, position, namedArgument))
                    {
                        return Array.Empty<string>();
                    }

                    continue;
                }

                nextPosition = GetNextUnassignedEditorWindowCaptureUtilityArgumentPosition(
                    orderedArguments,
                    nextPosition);
                if (nextPosition >= orderedArguments.Length)
                {
                    return Array.Empty<string>();
                }

                orderedArguments[nextPosition] = nextPosition == 1
                    ? GetArgumentWithMigratedCancellationTokenName(argument)
                    : argument;
                nextPosition++;
            }

            return orderedArguments.Any(argument => argument == null)
                ? Array.Empty<string>()
                : orderedArguments;
        }

        internal static (int Position, string Argument) GetEditorWindowCaptureUtilityNamedArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string windowValue =
                GetNamedArgumentValueOrNull(argument, EditorWindowCaptureUtilityWindowArgumentName);
            if (windowValue != null)
            {
                return (0, windowValue);
            }

            string resolutionScaleValue =
                GetNamedArgumentValueOrNull(argument, EditorWindowCaptureUtilityResolutionScaleArgumentName);
            if (resolutionScaleValue != null)
            {
                return (1, resolutionScaleValue);
            }

            return GetEditorWindowCaptureUtilityCancellationTokenNamedArgument(argument, 2);
        }

        internal static (int Position, string Argument)
            GetEditorWindowCaptureUtilityGameRenderingNamedArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string resolutionScaleValue =
                GetNamedArgumentValueOrNull(argument, EditorWindowCaptureUtilityResolutionScaleArgumentName);
            if (resolutionScaleValue != null)
            {
                return (0, resolutionScaleValue);
            }

            return GetEditorWindowCaptureUtilityCancellationTokenNamedArgument(argument, 1);
        }

        internal static (int Position, string Argument)
            GetEditorWindowCaptureUtilityCancellationTokenNamedArgument(string argument, int position)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(position >= 0, "position must not be negative");

            string legacyCancellationTokenValue =
                GetNamedArgumentValueOrNull(argument, LegacyCancellationTokenArgumentName);
            if (legacyCancellationTokenValue != null)
            {
                return (position, $"{CurrentCancellationTokenArgumentName}: {legacyCancellationTokenValue}");
            }

            string currentCancellationTokenValue =
                GetNamedArgumentValueOrNull(argument, CurrentCancellationTokenArgumentName);
            if (currentCancellationTokenValue != null)
            {
                return (position, $"{CurrentCancellationTokenArgumentName}: {currentCancellationTokenValue}");
            }

            return (-1, string.Empty);
        }

        internal static bool TryAssignEditorWindowCaptureUtilityArgument(
            string[] orderedArguments,
            int position,
            string argument)
        {
            Debug.Assert(orderedArguments != null, "orderedArguments must not be null");
            Debug.Assert(position >= 0, "position must not be negative");
            Debug.Assert(argument != null, "argument must not be null");

            if (position >= orderedArguments.Length || orderedArguments[position] != null)
            {
                return false;
            }

            orderedArguments[position] = argument;
            return true;
        }

        internal static int GetNextUnassignedEditorWindowCaptureUtilityArgumentPosition(
            string[] orderedArguments,
            int startPosition)
        {
            Debug.Assert(orderedArguments != null, "orderedArguments must not be null");
            Debug.Assert(startPosition >= 0, "startPosition must not be negative");

            int position = startPosition;
            while (position < orderedArguments.Length && orderedArguments[position] != null)
            {
                position++;
            }

            return position;
        }

        internal static string GetArgumentWithMigratedCancellationTokenName(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string cancellationTokenValue = GetNamedArgumentValueOrNull(argument, LegacyCancellationTokenArgumentName);
            if (cancellationTokenValue == null)
            {
                return argument;
            }

            return $"{CurrentCancellationTokenArgumentName}: {cancellationTokenValue}";
        }

        internal static bool ShouldMigrateLegacyToolInfoConstructorArguments(
            Match match,
            string[] arguments,
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(arguments != null, "arguments must not be null");

            if (!match.Groups["toolInfo"].Success)
            {
                return true;
            }

            return canMigrateAmbiguousBareLegacyToolInfoConstructor ||
                HasUnambiguousLegacyToolInfoConstructorArguments(arguments);
        }

        internal static bool HasUnambiguousLegacyToolInfoConstructorArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            if (arguments.Length == 4)
            {
                return true;
            }

            if (arguments.Length == 3 && IsLikelyLegacyDescriptionArgument(arguments[1]))
            {
                return true;
            }

            return FindNamedConstructorArgumentIndex(
                arguments,
                DescriptionAttributeArgumentName.ToLowerInvariant()) >= 0;
        }

        internal static bool IsLikelyLegacyDescriptionArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string trimmedArgument = argument.Trim();
            return IsStringLiteralArgument(trimmedArgument) ||
                string.Equals(trimmedArgument, "description", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsStringLiteralArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            return argument.StartsWith("\"", StringComparison.Ordinal) ||
                argument.StartsWith("@\"", StringComparison.Ordinal) ||
                argument.StartsWith("$\"", StringComparison.Ordinal) ||
                argument.StartsWith("$@\"", StringComparison.Ordinal) ||
                argument.StartsWith("@$\"", StringComparison.Ordinal);
        }

        internal static bool IsLegacyToolInfoConstructorMatch(
            Match match,
            string[] legacyNamespaceAliases,
            string[] legacyToolInfoTypeAliases,
            bool canMigrateBareLegacyToolInfoConstructor)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(legacyToolInfoTypeAliases != null, "legacyToolInfoTypeAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return legacyNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            if (match.Groups["typeAlias"].Success)
            {
                return legacyToolInfoTypeAliases.Contains(match.Groups["typeAlias"].Value);
            }

            if (match.Groups["toolInfo"].Success)
            {
                return canMigrateBareLegacyToolInfoConstructor;
            }

            return false;
        }
    }
}
