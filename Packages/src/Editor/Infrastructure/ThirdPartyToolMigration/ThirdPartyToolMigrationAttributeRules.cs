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
    internal static class ThirdPartyToolMigrationAttributeRules
    {
        internal static bool TryMigrateLegacyToolAttributeList(
            string attributesSource,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolAttribute,
            out string migratedAttributes)
        {
            Debug.Assert(attributesSource != null, "attributesSource must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            string[] attributes = SplitAttributeArguments(attributesSource);
            List<string> migratedAttributeItems = new();
            bool changed = false;
            foreach (string attribute in attributes)
            {
                string trimmedAttribute = attribute.Trim();
                if (TryMigrateLegacyToolAttributeEntry(
                        trimmedAttribute,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyToolAttribute,
                        out string migratedAttribute))
                {
                    migratedAttributeItems.Add(migratedAttribute);
                    changed = true;
                    continue;
                }

                migratedAttributeItems.Add(trimmedAttribute);
            }

            if (!changed)
            {
                migratedAttributes = string.Empty;
                return false;
            }

            migratedAttributes = string.Join(", ", migratedAttributeItems);
            return true;
        }

        internal static bool TryMigrateLegacyToolAttributeEntry(
            string attribute,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolAttribute,
            out string migratedAttribute)
        {
            Debug.Assert(attribute != null, "attribute must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            Match match = LegacyToolAttributeEntryRegex.Match(attribute);
            if (!match.Success)
            {
                migratedAttribute = string.Empty;
                return false;
            }

            bool hasQualifier = match.Groups["qualifier"].Success;
            bool hasAlias = match.Groups["alias"].Success;
            if (!hasQualifier && !hasAlias && !canMigrateBareLegacyToolAttribute)
            {
                migratedAttribute = string.Empty;
                return false;
            }

            if (hasAlias && !legacyNamespaceAliases.Contains(match.Groups["alias"].Value))
            {
                migratedAttribute = string.Empty;
                return false;
            }

            string argumentsSource = match.Groups["arguments"].Value;
            string[] migratedArguments = GetMigratedSupportedAttributeArguments(argumentsSource);
            string attributeName = hasQualifier || hasAlias
                ? $"{CurrentNamespace}.UnityCliLoopTool"
                : "UnityCliLoopTool";
            migratedAttribute = migratedArguments.Length == 0
                ? attributeName
                : $"{attributeName}({string.Join(", ", migratedArguments)})";
            return true;
        }

        internal static string[] GetMigratedSupportedAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            List<string> migratedArguments = new();
            string[] arguments = SplitAttributeArguments(argumentsSource);
            foreach (string argument in arguments)
            {
                string trimmedArgument = argument.Trim();
                if (trimmedArgument.Length == 0)
                {
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, DescriptionAttributeArgumentName))
                {
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, DisplayDevelopmentOnlyAttributeArgumentName))
                {
                    migratedArguments.Add(trimmedArgument);
                    continue;
                }

                if (IsNamedAttributeArgument(trimmedArgument, RequiredSecuritySettingAttributeArgumentName))
                {
                    migratedArguments.Add(trimmedArgument);
                }
            }

            return migratedArguments.ToArray();
        }

    }
}
