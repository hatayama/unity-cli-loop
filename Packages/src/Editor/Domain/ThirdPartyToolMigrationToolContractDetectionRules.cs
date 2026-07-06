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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationToolContractDetectionRules
    {
        public static bool ContainsLegacyAssemblyScopedTypeReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
                if (ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                if (ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            return ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, "ToolInfo");
        }

        public static bool ContainsLegacyAliasQualifiedAssemblyScopedApi(
            string source,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            foreach (string alias in legacyAssemblyAliases)
            {
                foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                if (ContainsAliasQualifiedName(source, alias, "ToolInfo") ||
                    ContainsAliasQualifiedName(source, alias, "CustomToolManager"))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsLegacyEditorDelayFrameCall(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorDelay)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorDelayFrameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    IsLegacyEditorDelayFrameCallMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyEditorDelay))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
