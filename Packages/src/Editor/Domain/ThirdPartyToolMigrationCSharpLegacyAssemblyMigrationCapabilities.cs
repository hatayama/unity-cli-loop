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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{

    internal readonly struct CSharpLegacyAssemblyMigrationCapabilities
    {
        private CSharpLegacyAssemblyMigrationCapabilities(
            bool canUseCurrentToolContracts,
            bool canUseBareCurrentToolContracts,
            bool canPreserveBareCurrentToolContractsReferences,
            bool canMigrateBareLegacyToolAttribute,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            bool canUseBareCurrentFirstPartyTools,
            bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
            bool canMigrateBareLegacyFirstPartyScreenshotApi,
            bool canMigrateBareCurrentDomainContractType,
            bool canMigrateBareLegacyApplicationApi,
            bool canMigrateBareLegacyApplicationTypeName,
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor,
            bool shouldApplyContractRenames,
            bool shouldApplyRegistrarRenames,
            bool shouldApplyDomainMetadataRenames)
        {
            CanUseCurrentToolContracts = canUseCurrentToolContracts;
            CanUseBareCurrentToolContracts = canUseBareCurrentToolContracts;
            CanPreserveBareCurrentToolContractsReferences = canPreserveBareCurrentToolContractsReferences;
            CanMigrateBareLegacyToolAttribute = canMigrateBareLegacyToolAttribute;
            CanMigrateBareLegacyEditorWindowCaptureUtility = canMigrateBareLegacyEditorWindowCaptureUtility;
            CanUseBareCurrentFirstPartyTools = canUseBareCurrentFirstPartyTools;
            ShouldQualifyBareEditorWindowCaptureUtilityTimeout =
                shouldQualifyBareEditorWindowCaptureUtilityTimeout;
            CanMigrateBareLegacyFirstPartyScreenshotApi = canMigrateBareLegacyFirstPartyScreenshotApi;
            CanMigrateBareCurrentDomainContractType = canMigrateBareCurrentDomainContractType;
            CanMigrateBareLegacyApplicationApi = canMigrateBareLegacyApplicationApi;
            CanMigrateBareLegacyApplicationTypeName = canMigrateBareLegacyApplicationTypeName;
            CanMigrateAmbiguousBareLegacyToolInfoConstructor =
                canMigrateAmbiguousBareLegacyToolInfoConstructor;
            ShouldApplyContractRenames = shouldApplyContractRenames;
            ShouldApplyRegistrarRenames = shouldApplyRegistrarRenames;
            ShouldApplyDomainMetadataRenames = shouldApplyDomainMetadataRenames;
        }

        public bool CanUseCurrentToolContracts { get; }
        public bool CanUseBareCurrentToolContracts { get; }
        public bool CanPreserveBareCurrentToolContractsReferences { get; }
        public bool CanMigrateBareLegacyToolAttribute { get; }
        public bool CanMigrateBareLegacyEditorWindowCaptureUtility { get; }
        public bool CanUseBareCurrentFirstPartyTools { get; }
        public bool ShouldQualifyBareEditorWindowCaptureUtilityTimeout { get; }
        public bool CanMigrateBareLegacyFirstPartyScreenshotApi { get; }
        public bool CanMigrateBareCurrentDomainContractType { get; }
        public bool CanMigrateBareLegacyApplicationApi { get; }
        public bool CanMigrateBareLegacyApplicationTypeName { get; }
        public bool CanMigrateAmbiguousBareLegacyToolInfoConstructor { get; }
        public bool ShouldApplyContractRenames { get; }
        public bool ShouldApplyRegistrarRenames { get; }
        public bool ShouldApplyDomainMetadataRenames { get; }

        public static CSharpLegacyAssemblyMigrationCapabilities Create(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentApplicationUsing,
            bool hasAssemblyScopedCurrentDomainUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyNamespaceAliases)
        {
            CSharpMigrationSourceUsage usage = DetectMigrationSourceUsage(source);
            bool canUseCurrentToolContracts = CanUseCurrentToolContractsFromUsage(
                usage,
                hasAssemblyScopedCurrentToolContractsUsing);
            bool canUseBareCurrentToolContracts = CanUseBareCurrentToolContractsFromUsage(
                usage,
                hasLegacyAssemblySource,
                hasAssemblyScopedCurrentToolContractsUsing);
            bool canMigrateBareLegacyToolAttribute = CanMigrateBareLegacyToolAttributeFromUsage(
                usage,
                hasLegacyAssemblySource,
                legacyNamespaceAliases);
            bool canMigrateBareLegacyApplicationApi = CanMigrateBareLegacyApplicationApiFromUsage(
                usage,
                canMigrateBareLegacyToolAttribute,
                canUseCurrentToolContracts,
                hasAssemblyScopedCurrentApplicationUsing);
            bool canMigrateBareLegacyFirstPartyApi = CanMigrateBareLegacyFirstPartyApiFromUsage(
                usage,
                canMigrateBareLegacyToolAttribute,
                canUseCurrentToolContracts,
                hasAssemblyScopedCurrentFirstPartyToolsUsing);
            bool canUseBareCurrentFirstPartyTools = CanUseBareCurrentFirstPartyToolsFromUsage(
                usage,
                hasAssemblyScopedCurrentFirstPartyToolsUsing);
            bool canMigrateBareCurrentDomainContractType =
                CanMigrateBareCurrentDomainContractTypeFromUsage(usage, hasAssemblyScopedCurrentDomainUsing);
            bool hasLocalLegacyMarker = ContainsLegacyToolMigrationMarker(source);
            bool shouldApplyContractRenames = hasLegacyAssemblySource || hasLocalLegacyMarker;
            return new CSharpLegacyAssemblyMigrationCapabilities(
                canUseCurrentToolContracts,
                canUseBareCurrentToolContracts,
                CanPreserveBareCurrentToolContractsReferencesFromUsage(
                    usage,
                    hasAssemblyScopedCurrentToolContractsUsing),
                canMigrateBareLegacyToolAttribute,
                canMigrateBareLegacyFirstPartyApi,
                canUseBareCurrentFirstPartyTools,
                !canUseBareCurrentToolContracts,
                canMigrateBareLegacyFirstPartyApi,
                canMigrateBareCurrentDomainContractType,
                canMigrateBareLegacyApplicationApi,
                canMigrateBareLegacyApplicationApi,
                CanMigrateAmbiguousBareLegacyToolInfoConstructorFromUsage(
                    usage,
                    canMigrateBareLegacyToolAttribute),
                shouldApplyContractRenames,
                ShouldApplyRegistrarRenamesFromUsage(source, shouldApplyContractRenames),
                ShouldApplyDomainMetadataRenamesFromUsage(source, shouldApplyContractRenames));
        }

        private static CSharpMigrationSourceUsage DetectMigrationSourceUsage(string source)
        {
            bool hasCurrentDomainUsingDirective =
                RegexMatchesCode(source, CurrentDomainUsingRegex) ||
                RegexMatchesCode(source, CurrentDomainGlobalUsingRegex);
            bool hasCurrentFirstPartyToolsUsingDirective =
                RegexMatchesCode(source, CurrentFirstPartyToolsUsingRegex) ||
                RegexMatchesCode(source, CurrentFirstPartyToolsGlobalUsingRegex);

            return new CSharpMigrationSourceUsage(
                RegexMatchesCode(source, LegacyNamespaceRegex),
                RegexMatchesCode(source, LegacyNamespaceUsingRegex),
                RegexMatchesCode(source, CurrentApplicationNamespaceRegex),
                RegexMatchesCode(source, CurrentDomainNamespaceRegex),
                hasCurrentDomainUsingDirective,
                RegexMatchesCode(source, CurrentToolContractsNamespaceRegex),
                RegexMatchesCode(source, CurrentToolContractsUsingRegex),
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex),
                hasCurrentFirstPartyToolsUsingDirective);
        }

        private static bool CanUseCurrentToolContractsFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasAssemblyScopedCurrentToolContractsUsing)
        {
            return usage.HasCurrentToolContractsNamespaceUsage || hasAssemblyScopedCurrentToolContractsUsing;
        }

        private static bool CanUseBareCurrentToolContractsFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing)
        {
            return hasLegacyAssemblySource ||
                usage.HasLegacyNamespaceUsingDirective ||
                usage.HasCurrentToolContractsUsingDirective ||
                hasAssemblyScopedCurrentToolContractsUsing;
        }

        private static bool CanPreserveBareCurrentToolContractsReferencesFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasAssemblyScopedCurrentToolContractsUsing)
        {
            return usage.HasCurrentToolContractsUsingDirective || hasAssemblyScopedCurrentToolContractsUsing;
        }

        private static bool CanMigrateBareLegacyToolAttributeFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasLegacyAssemblySource,
            string[] legacyNamespaceAliases)
        {
            return hasLegacyAssemblySource ||
                usage.HasLegacyNamespaceUsage ||
                legacyNamespaceAliases.Length > 0;
        }

        private static bool CanMigrateBareLegacyApplicationApiFromUsage(
            CSharpMigrationSourceUsage usage,
            bool canMigrateBareLegacyToolAttribute,
            bool canUseCurrentToolContracts,
            bool hasAssemblyScopedCurrentApplicationUsing)
        {
            return canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasAssemblyScopedCurrentApplicationUsing ||
                usage.HasCurrentApplicationNamespaceUsage;
        }

        private static bool CanMigrateBareLegacyFirstPartyApiFromUsage(
            CSharpMigrationSourceUsage usage,
            bool canMigrateBareLegacyToolAttribute,
            bool canUseCurrentToolContracts,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing)
        {
            return canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                usage.HasCurrentFirstPartyToolsNamespaceUsage;
        }

        private static bool CanUseBareCurrentFirstPartyToolsFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing)
        {
            return hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                usage.HasCurrentFirstPartyToolsUsingDirective;
        }

        private static bool CanMigrateBareCurrentDomainContractTypeFromUsage(
            CSharpMigrationSourceUsage usage,
            bool hasAssemblyScopedCurrentDomainUsing)
        {
            return hasAssemblyScopedCurrentDomainUsing || usage.HasCurrentDomainUsingDirective;
        }

        private static bool CanMigrateAmbiguousBareLegacyToolInfoConstructorFromUsage(
            CSharpMigrationSourceUsage usage,
            bool canMigrateBareLegacyToolAttribute)
        {
            return canMigrateBareLegacyToolAttribute && !usage.HasCurrentDomainNamespaceUsage;
        }

        private static bool ShouldApplyRegistrarRenamesFromUsage(
            string source,
            bool shouldApplyContractRenames)
        {
            return shouldApplyContractRenames && RegexMatchesCode(source, LegacyRegistrarRegex);
        }

        private static bool ShouldApplyDomainMetadataRenamesFromUsage(
            string source,
            bool shouldApplyContractRenames)
        {
            return shouldApplyContractRenames && RegexMatchesCode(source, LegacyDomainMetadataRegex);
        }

        private readonly struct CSharpMigrationSourceUsage
        {
            public CSharpMigrationSourceUsage(
                bool hasLegacyNamespaceUsage,
                bool hasLegacyNamespaceUsingDirective,
                bool hasCurrentApplicationNamespaceUsage,
                bool hasCurrentDomainNamespaceUsage,
                bool hasCurrentDomainUsingDirective,
                bool hasCurrentToolContractsNamespaceUsage,
                bool hasCurrentToolContractsUsingDirective,
                bool hasCurrentFirstPartyToolsNamespaceUsage,
                bool hasCurrentFirstPartyToolsUsingDirective)
            {
                HasLegacyNamespaceUsage = hasLegacyNamespaceUsage;
                HasLegacyNamespaceUsingDirective = hasLegacyNamespaceUsingDirective;
                HasCurrentApplicationNamespaceUsage = hasCurrentApplicationNamespaceUsage;
                HasCurrentDomainNamespaceUsage = hasCurrentDomainNamespaceUsage;
                HasCurrentDomainUsingDirective = hasCurrentDomainUsingDirective;
                HasCurrentToolContractsNamespaceUsage = hasCurrentToolContractsNamespaceUsage;
                HasCurrentToolContractsUsingDirective = hasCurrentToolContractsUsingDirective;
                HasCurrentFirstPartyToolsNamespaceUsage = hasCurrentFirstPartyToolsNamespaceUsage;
                HasCurrentFirstPartyToolsUsingDirective = hasCurrentFirstPartyToolsUsingDirective;
            }

            public bool HasLegacyNamespaceUsage { get; }
            public bool HasLegacyNamespaceUsingDirective { get; }
            public bool HasCurrentApplicationNamespaceUsage { get; }
            public bool HasCurrentDomainNamespaceUsage { get; }
            public bool HasCurrentDomainUsingDirective { get; }
            public bool HasCurrentToolContractsNamespaceUsage { get; }
            public bool HasCurrentToolContractsUsingDirective { get; }
            public bool HasCurrentFirstPartyToolsNamespaceUsage { get; }
            public bool HasCurrentFirstPartyToolsUsingDirective { get; }
        }
    }
}
