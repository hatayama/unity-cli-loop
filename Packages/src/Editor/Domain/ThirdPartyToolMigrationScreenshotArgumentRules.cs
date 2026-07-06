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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
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
    public static class ThirdPartyToolMigrationScreenshotArgumentRules
    {
        public static string[] GetMigratedEditorWindowCaptureUtilityArguments(
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

        public static string[] GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(
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

        public static string[] GetTrimmedInvocationArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            return arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
        }

        public static string[] GetOrderedEditorWindowCaptureUtilityArguments(string[] arguments)
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

        public static string[] GetOrderedEditorWindowCaptureUtilityGameRenderingArguments(string[] arguments)
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

        public static (int Position, string Argument) GetEditorWindowCaptureUtilityNamedArgument(string argument)
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

        public static (int Position, string Argument)
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

        public static (int Position, string Argument)
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

        public static bool TryAssignEditorWindowCaptureUtilityArgument(
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

        public static int GetNextUnassignedEditorWindowCaptureUtilityArgumentPosition(
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

        public static string GetArgumentWithMigratedCancellationTokenName(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string cancellationTokenValue = GetNamedArgumentValueOrNull(argument, LegacyCancellationTokenArgumentName);
            if (cancellationTokenValue == null)
            {
                return argument;
            }

            return $"{CurrentCancellationTokenArgumentName}: {cancellationTokenValue}";
        }

        public static bool ShouldMigrateLegacyToolInfoConstructorArguments(
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

        public static bool HasUnambiguousLegacyToolInfoConstructorArguments(string[] arguments)
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

        public static bool IsLikelyLegacyDescriptionArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string trimmedArgument = argument.Trim();
            return IsStringLiteralArgument(trimmedArgument) ||
                string.Equals(trimmedArgument, "description", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsStringLiteralArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            return argument.StartsWith("\"", StringComparison.Ordinal) ||
                argument.StartsWith("@\"", StringComparison.Ordinal) ||
                argument.StartsWith("$\"", StringComparison.Ordinal) ||
                argument.StartsWith("$@\"", StringComparison.Ordinal) ||
                argument.StartsWith("@$\"", StringComparison.Ordinal);
        }

        public static bool IsLegacyToolInfoConstructorMatch(
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
