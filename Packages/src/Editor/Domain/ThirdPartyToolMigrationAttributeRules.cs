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
    public static class ThirdPartyToolMigrationAttributeRules
    {
        public static bool TryMigrateLegacyToolAttributeList(
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

        public static bool TryMigrateLegacyToolAttributeEntry(
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

        public static string[] GetMigratedSupportedAttributeArguments(string argumentsSource)
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
