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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
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
    public static class ThirdPartyToolMigrationTimingArgumentRules
    {
        public static (string[] Arguments, bool Changed) RemoveLegacyPlayerLoopTimingCallerArguments(
            string[] arguments,
            RemovedLegacyPlayerLoopTimingParameter[] removedParameters,
            string[] legacyNamespaceAliases)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(removedParameters != null, "removedParameters must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            List<string> migratedArguments = new();
            bool changed = false;
            int argumentIndex = 0;
            foreach (string argument in arguments)
            {
                string trimmedArgument = argument.Trim();
                if (trimmedArgument.Length == 0)
                {
                    continue;
                }

                if (ShouldRemoveLegacyPlayerLoopTimingCallerArgument(
                        trimmedArgument,
                        argumentIndex,
                        removedParameters,
                        legacyNamespaceAliases))
                {
                    changed = true;
                    argumentIndex++;
                    continue;
                }

                migratedArguments.Add(trimmedArgument);
                argumentIndex++;
            }

            return (migratedArguments.ToArray(), changed);
        }

        public static bool ShouldRemoveLegacyPlayerLoopTimingCallerArgument(
            string argument,
            int argumentIndex,
            RemovedLegacyPlayerLoopTimingParameter[] removedParameters,
            string[] legacyNamespaceAliases)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(argument), "argument must not be null or whitespace");
            Debug.Assert(argumentIndex >= 0, "argumentIndex must not be negative");
            Debug.Assert(removedParameters != null, "removedParameters must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            foreach (RemovedLegacyPlayerLoopTimingParameter removedParameter in removedParameters)
            {
                string namedArgumentValue = GetNamedArgumentValueOrNull(argument, removedParameter.Name);
                if (namedArgumentValue != null)
                {
                    return IsLegacyPlayerLoopTimingCallerArgument(namedArgumentValue, legacyNamespaceAliases);
                }

                if (argumentIndex == removedParameter.Index &&
                    IsLegacyPlayerLoopTimingCallerArgument(argument, legacyNamespaceAliases))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsLegacyPlayerLoopTimingCallerArgument(string argument, string[] legacyNamespaceAliases)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            return IsLegacyPlayerLoopTimingArgument(argument, legacyNamespaceAliases) ||
                IsLegacyMainThreadSwitcherSingleTimingArgument(argument);
        }

        public static (bool IsMatch, string ParameterName) ReadLegacyPlayerLoopTimingParameter(
            string parameter,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyPlayerLoopTiming)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(parameter), "parameter must not be null or whitespace");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            (bool qualifiedIsMatch, string qualifiedParameterName) =
                MatchesLegacyPlayerLoopTimingParameter(
                    parameter,
                    $@"(?:global::)?{Regex.Escape(LegacyNamespace)}\.{LegacyPlayerLoopTimingTypeName}");
            if (qualifiedIsMatch)
            {
                return (true, qualifiedParameterName);
            }

            foreach (string alias in legacyNamespaceAliases)
            {
                (bool aliasIsMatch, string aliasParameterName) =
                    MatchesLegacyPlayerLoopTimingParameter(
                        parameter,
                        $@"{Regex.Escape(alias)}\.{LegacyPlayerLoopTimingTypeName}");
                if (aliasIsMatch)
                {
                    return (true, aliasParameterName);
                }
            }

            if (canMigrateBareLegacyPlayerLoopTiming)
            {
                return MatchesLegacyPlayerLoopTimingParameter(parameter, LegacyPlayerLoopTimingTypeName);
            }

            return (false, string.Empty);
        }

        public static (bool IsMatch, string ParameterName) MatchesLegacyPlayerLoopTimingParameter(
            string parameter,
            string typePattern)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(parameter), "parameter must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(typePattern), "typePattern must not be null or whitespace");

            Regex parameterRegex = new(
                $@"^(?:\[[^\]]+\]\s*)*(?:(?:this|in|ref|out|params)\s+)*(?:{typePattern})\??\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*=.+)?$",
                RegexOptions.Compiled);
            Match match = parameterRegex.Match(parameter);
            if (!match.Success)
            {
                return (false, string.Empty);
            }

            return (true, match.Groups["name"].Value);
        }

        public static bool ContainsIdentifierInCode(string source, string identifier)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(identifier), "identifier must not be null or empty");

            Regex identifierRegex = new($@"\b{Regex.Escape(identifier)}\b", RegexOptions.Compiled);
            return RegexMatchesCode(source, identifierRegex);
        }

        public static (string[] Arguments, bool Changed) GetMigratedMainThreadSwitcherArguments(
            string[] arguments,
            string[] legacyNamespaceAliases)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            string[] trimmedArguments = arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
            List<string> migratedArguments = new();
            bool changed = false;
            for (int index = 0; index < trimmedArguments.Length; index++)
            {
                string argument = trimmedArguments[index];
                string cancellationTokenValue =
                    GetNamedArgumentValueOrNull(argument, LegacyCancellationTokenArgumentName);
                if (cancellationTokenValue != null)
                {
                    migratedArguments.Add($"{CurrentCancellationTokenArgumentName}: {cancellationTokenValue}");
                    changed = true;
                    continue;
                }

                string timingValue = GetNamedArgumentValueOrNull(argument, LegacyTimingArgumentName);
                if (timingValue != null)
                {
                    changed = true;
                    continue;
                }

                if (ReadNamedArgumentName(argument).Length > 0)
                {
                    migratedArguments.Add(argument);
                    continue;
                }

                if (IsLegacyPlayerLoopTimingArgument(argument, legacyNamespaceAliases) ||
                    IsLegacyMainThreadSwitcherPositionalTimingArgument(trimmedArguments, index))
                {
                    changed = true;
                    continue;
                }

                migratedArguments.Add(argument);
            }

            return (migratedArguments.ToArray(), changed);
        }

        public static bool IsLegacyMainThreadSwitcherPositionalTimingArgument(
            string[] arguments,
            int argumentIndex)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(argumentIndex >= 0, "argumentIndex must not be negative");

            if (argumentIndex != 0)
            {
                return false;
            }

            if (ReadNamedArgumentName(arguments[argumentIndex]).Length > 0)
            {
                return false;
            }

            if (arguments.Length == 2)
            {
                return true;
            }

            return arguments.Length == 1 &&
                IsLegacyMainThreadSwitcherSingleTimingArgument(arguments[0]);
        }

        public static bool IsLegacyMainThreadSwitcherSingleTimingArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string trimmedArgument = argument.Trim();
            if (IsLikelyCancellationTokenArgument(trimmedArgument))
            {
                return false;
            }

            bool containsTimingName =
                trimmedArgument.IndexOf("Timing", StringComparison.OrdinalIgnoreCase) >= 0;
            bool containsPlayerLoopName =
                trimmedArgument.IndexOf("PlayerLoop", StringComparison.OrdinalIgnoreCase) >= 0;
            bool endsWithLoopName =
                trimmedArgument.EndsWith("loop", StringComparison.OrdinalIgnoreCase) ||
                trimmedArgument.EndsWith(".loop", StringComparison.OrdinalIgnoreCase);
            return string.Equals(trimmedArgument, LegacyTimingArgumentName, StringComparison.Ordinal) ||
                string.Equals(trimmedArgument, "default", StringComparison.Ordinal) ||
                string.Equals(trimmedArgument, $"default({LegacyPlayerLoopTimingTypeName})", StringComparison.Ordinal) ||
                containsTimingName ||
                containsPlayerLoopName ||
                endsWithLoopName;
        }

        public static bool IsLikelyCancellationTokenArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            return string.Equals(argument, CurrentCancellationTokenArgumentName, StringComparison.Ordinal) ||
                string.Equals(argument, LegacyCancellationTokenArgumentName, StringComparison.Ordinal) ||
                string.Equals(argument, "token", StringComparison.OrdinalIgnoreCase) ||
                argument.EndsWith("Token", StringComparison.Ordinal) ||
                argument.IndexOf("CancellationToken", StringComparison.Ordinal) >= 0;
        }

        public static bool IsIdentifierLikeExpression(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            if (argument.Length == 0)
            {
                return false;
            }

            if (!IsIdentifierStartCharacter(argument[0]))
            {
                return false;
            }

            for (int i = 1; i < argument.Length; i++)
            {
                if (!IsIdentifierCharacter(argument[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsLegacyPlayerLoopTimingArgument(string argument, string[] legacyNamespaceAliases)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            string trimmedArgument = argument.Trim();
            if (trimmedArgument.StartsWith($"{LegacyPlayerLoopTimingTypeName}.", StringComparison.Ordinal) ||
                trimmedArgument.StartsWith(
                    $"global::{LegacyNamespace}.{LegacyPlayerLoopTimingTypeName}.",
                    StringComparison.Ordinal) ||
                trimmedArgument.StartsWith(
                    $"{LegacyNamespace}.{LegacyPlayerLoopTimingTypeName}.",
                    StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string alias in legacyNamespaceAliases)
            {
                if (trimmedArgument.StartsWith(
                        $"{alias}.{LegacyPlayerLoopTimingTypeName}.",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
