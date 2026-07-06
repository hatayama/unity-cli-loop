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
    public static class ThirdPartyToolMigrationConstructorArgumentRules
    {
        public static string[] GetMigratedToolInfoConstructorArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            int namedDescriptionArgumentIndex = FindNamedConstructorArgumentIndex(
                arguments,
                DescriptionAttributeArgumentName.ToLowerInvariant());
            if (namedDescriptionArgumentIndex >= 0)
            {
                return RemoveArgumentAt(arguments, namedDescriptionArgumentIndex);
            }

            if (arguments.Length == 4)
            {
                return new[]
                {
                    arguments[0].Trim(),
                    arguments[2].Trim(),
                    arguments[3].Trim()
                };
            }

            if (arguments.Length == 3)
            {
                return new[]
                {
                    arguments[0].Trim(),
                    arguments[2].Trim()
                };
            }

            return arguments;
        }

        public static string[] GetMigratedToolSettingsCatalogItemConstructorArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            int namedDescriptionArgumentIndex = FindNamedConstructorArgumentIndex(
                arguments,
                DescriptionAttributeArgumentName.ToLowerInvariant());
            if (namedDescriptionArgumentIndex >= 0)
            {
                return RemoveArgumentAt(arguments, namedDescriptionArgumentIndex);
            }

            if (arguments.Length == 4)
            {
                return new[]
                {
                    arguments[0].Trim(),
                    arguments[2].Trim(),
                    arguments[3].Trim()
                };
            }

            return arguments;
        }

        public static int FindNamedConstructorArgumentIndex(string[] arguments, string argumentName)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(!string.IsNullOrEmpty(argumentName), "argumentName must not be null or empty");

            for (int i = 0; i < arguments.Length; i++)
            {
                if (IsNamedConstructorArgument(arguments[i].Trim(), argumentName))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsNamedConstructorArgument(string argument, string argumentName)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(!string.IsNullOrEmpty(argumentName), "argumentName must not be null or empty");

            int colonIndex = argument.IndexOf(':');
            if (colonIndex <= 0)
            {
                return false;
            }

            string possibleArgumentName = argument.Substring(0, colonIndex).Trim();
            return string.Equals(possibleArgumentName, argumentName, StringComparison.Ordinal);
        }

        public static string[] RemoveArgumentAt(string[] arguments, int removeIndex)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(removeIndex >= 0, "removeIndex must not be negative");
            Debug.Assert(removeIndex < arguments.Length, "removeIndex must be within arguments");

            List<string> migratedArguments = new();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i == removeIndex)
                {
                    continue;
                }

                migratedArguments.Add(arguments[i].Trim());
            }

            return migratedArguments.ToArray();
        }
    }
}
