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
    internal static class ThirdPartyToolMigrationConstructorArgumentRules
    {
        internal static string[] GetMigratedToolInfoConstructorArguments(string[] arguments)
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

        internal static string[] GetMigratedToolSettingsCatalogItemConstructorArguments(string[] arguments)
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

        internal static int FindNamedConstructorArgumentIndex(string[] arguments, string argumentName)
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

        internal static bool IsNamedConstructorArgument(string argument, string argumentName)
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

        internal static string[] RemoveArgumentAt(string[] arguments, int removeIndex)
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
