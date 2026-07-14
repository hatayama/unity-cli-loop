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
    /// <summary>
    /// Carries the derived migration context while applying C# source rewrite phases.
    /// </summary>
    internal sealed class CSharpLegacyAssemblyMigrationContext
    {
        private readonly string[] _legacyNamespaceAliases;
        private readonly string[] _legacyAssemblyToolInfoAliases;
        private readonly string[] _currentApplicationNamespaceAliases;
        private readonly string[] _currentDomainNamespaceAliases;
        private readonly string[] _currentFirstPartyToolsNamespaceAliases;
        private readonly string[] _assemblyDeclaredTypeNames;
        private readonly bool _canUseCurrentToolContracts;
        private readonly bool _canUseBareCurrentToolContracts;
        private readonly bool _canPreserveBareCurrentToolContractsReferences;
        private readonly bool _canMigrateBareLegacyToolAttribute;
        private readonly bool _canMigrateBareLegacyEditorWindowCaptureUtility;
        private readonly bool _canUseBareCurrentFirstPartyTools;
        private readonly bool _shouldQualifyBareEditorWindowCaptureUtilityTimeout;
        private readonly bool _canMigrateBareLegacyFirstPartyScreenshotApi;
        private readonly bool _canMigrateBareCurrentDomainContractType;
        private readonly bool _canMigrateBareLegacyApplicationApi;
        private readonly bool _canMigrateBareLegacyApplicationTypeName;
        private readonly bool _canMigrateAmbiguousBareLegacyToolInfoConstructor;
        private readonly bool _shouldApplyContractRenames;
        private readonly bool _shouldApplyRegistrarRenames;
        private readonly bool _shouldApplyDomainMetadataRenames;
        private readonly List<RemovedLegacyPlayerLoopTimingSignature> _removedPlayerLoopTimingSignatures =
            new();
        private string _migratedContent;
        private int _replacementCount;

        private CSharpLegacyAssemblyMigrationContext(
            string source,
            string[] legacyNamespaceAliases,
            string[] legacyAssemblyToolInfoAliases,
            string[] currentApplicationNamespaceAliases,
            string[] currentDomainNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames,
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
            _migratedContent = source;
            _legacyNamespaceAliases = legacyNamespaceAliases;
            _legacyAssemblyToolInfoAliases = legacyAssemblyToolInfoAliases;
            _currentApplicationNamespaceAliases = currentApplicationNamespaceAliases;
            _currentDomainNamespaceAliases = currentDomainNamespaceAliases;
            _currentFirstPartyToolsNamespaceAliases = currentFirstPartyToolsNamespaceAliases;
            _assemblyDeclaredTypeNames = assemblyDeclaredTypeNames;
            _canUseCurrentToolContracts = canUseCurrentToolContracts;
            _canUseBareCurrentToolContracts = canUseBareCurrentToolContracts;
            _canPreserveBareCurrentToolContractsReferences = canPreserveBareCurrentToolContractsReferences;
            _canMigrateBareLegacyToolAttribute = canMigrateBareLegacyToolAttribute;
            _canMigrateBareLegacyEditorWindowCaptureUtility =
                canMigrateBareLegacyEditorWindowCaptureUtility;
            _canUseBareCurrentFirstPartyTools = canUseBareCurrentFirstPartyTools;
            _shouldQualifyBareEditorWindowCaptureUtilityTimeout =
                shouldQualifyBareEditorWindowCaptureUtilityTimeout;
            _canMigrateBareLegacyFirstPartyScreenshotApi = canMigrateBareLegacyFirstPartyScreenshotApi;
            _canMigrateBareCurrentDomainContractType = canMigrateBareCurrentDomainContractType;
            _canMigrateBareLegacyApplicationApi = canMigrateBareLegacyApplicationApi;
            _canMigrateBareLegacyApplicationTypeName = canMigrateBareLegacyApplicationTypeName;
            _canMigrateAmbiguousBareLegacyToolInfoConstructor =
                canMigrateAmbiguousBareLegacyToolInfoConstructor;
            _shouldApplyContractRenames = shouldApplyContractRenames;
            _shouldApplyRegistrarRenames = shouldApplyRegistrarRenames;
            _shouldApplyDomainMetadataRenames = shouldApplyDomainMetadataRenames;
        }

        public static CSharpLegacyAssemblyMigrationContext Create(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentApplicationUsing,
            bool hasAssemblyScopedCurrentDomainUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] legacyAssemblyToolInfoAliases,
            string[] currentApplicationAssemblyAliases,
            string[] currentDomainAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            string[] currentApplicationNamespaceAliases = GetCombinedCurrentApplicationNamespaceAliases(
                source,
                currentApplicationAssemblyAliases);
            string[] currentDomainNamespaceAliases = GetCombinedCurrentDomainNamespaceAliases(
                source,
                currentDomainAssemblyAliases);
            string[] currentFirstPartyToolsNamespaceAliases =
                GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                    source,
                    currentFirstPartyToolsAssemblyAliases);
            CSharpLegacyAssemblyMigrationCapabilities capabilities =
                CSharpLegacyAssemblyMigrationCapabilities.Create(
                    source,
                    hasLegacyAssemblySource,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentApplicationUsing,
                    hasAssemblyScopedCurrentDomainUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyNamespaceAliases);
            return new CSharpLegacyAssemblyMigrationContext(
                source,
                legacyNamespaceAliases,
                legacyAssemblyToolInfoAliases,
                currentApplicationNamespaceAliases,
                currentDomainNamespaceAliases,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames,
                capabilities.CanUseCurrentToolContracts,
                capabilities.CanUseBareCurrentToolContracts,
                capabilities.CanPreserveBareCurrentToolContractsReferences,
                capabilities.CanMigrateBareLegacyToolAttribute,
                capabilities.CanMigrateBareLegacyEditorWindowCaptureUtility,
                capabilities.CanUseBareCurrentFirstPartyTools,
                capabilities.ShouldQualifyBareEditorWindowCaptureUtilityTimeout,
                capabilities.CanMigrateBareLegacyFirstPartyScreenshotApi,
                capabilities.CanMigrateBareCurrentDomainContractType,
                capabilities.CanMigrateBareLegacyApplicationApi,
                capabilities.CanMigrateBareLegacyApplicationTypeName,
                capabilities.CanMigrateAmbiguousBareLegacyToolInfoConstructor,
                capabilities.ShouldApplyContractRenames,
                capabilities.ShouldApplyRegistrarRenames,
                capabilities.ShouldApplyDomainMetadataRenames);
        }

        public void ApplyToolMetadataReplacements()
        {
            _migratedContent = ReplaceLegacyToolAttributesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _canMigrateBareLegacyToolAttribute,
                ref _replacementCount);
            _migratedContent = ReplaceLegacyToolInfoConstructorsInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _canMigrateBareLegacyToolAttribute,
                _canMigrateAmbiguousBareLegacyToolInfoConstructor,
                _legacyAssemblyToolInfoAliases,
                ref _replacementCount);
            _migratedContent = ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _canMigrateBareLegacyToolAttribute,
                ref _replacementCount);
        }

        public void ApplyDelayReplacements()
        {
            (string editorDelayMigratedContent, int editorDelayReplacementCount) =
                ReplaceLegacyEditorDelayFrameCallsInCode(
                    _migratedContent,
                    _legacyNamespaceAliases,
                    _canMigrateBareLegacyToolAttribute || _canUseCurrentToolContracts,
                    !_canUseBareCurrentToolContracts);
            ApplyReplacementResult(editorDelayMigratedContent, editorDelayReplacementCount);
            (string timerDelayMigratedContent, int timerDelayReplacementCount) =
                ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    _migratedContent,
                    _legacyNamespaceAliases,
                    _canMigrateBareLegacyToolAttribute || _canUseCurrentToolContracts);
            ApplyReplacementResult(timerDelayMigratedContent, timerDelayReplacementCount);
        }

        public void ApplyMainThreadSwitcherReplacements()
        {
            (string migratedContent, int replacementCount) =
                ReplaceLegacyMainThreadSwitcherCallsInCode(
                    _migratedContent,
                    _legacyNamespaceAliases,
                    _currentApplicationNamespaceAliases,
                    _canMigrateBareLegacyApplicationApi,
                    _assemblyDeclaredTypeNames);
            ApplyReplacementResult(migratedContent, replacementCount);
            if (replacementCount == 0 && !ContainsMigratedMainThreadSwitcherSwitchCall(_migratedContent))
            {
                return;
            }

            RemovePlayerLoopTimingParameters();
            RemoveUnusedPlayerLoopTimingDeclarations();
        }

        public void ApplyEditorWindowCaptureAndTypeReplacements()
        {
            (string editorWindowCaptureMigratedContent, int editorWindowCaptureReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                    _migratedContent,
                    _legacyNamespaceAliases,
                    _currentFirstPartyToolsNamespaceAliases,
                    _canMigrateBareLegacyEditorWindowCaptureUtility,
                    _shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    _canPreserveBareCurrentToolContractsReferences,
                    _canUseBareCurrentFirstPartyTools,
                    _assemblyDeclaredTypeNames);
            ApplyReplacementResult(editorWindowCaptureMigratedContent, editorWindowCaptureReplacementCount);
            _migratedContent = ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _currentFirstPartyToolsNamespaceAliases,
                _canMigrateBareLegacyFirstPartyScreenshotApi,
                _canPreserveBareCurrentToolContractsReferences,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
            _migratedContent = ReplaceCurrentDomainContractTypeNamesInCode(
                _migratedContent,
                _currentDomainNamespaceAliases,
                _canMigrateBareCurrentDomainContractType,
                _canPreserveBareCurrentToolContractsReferences,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
            _migratedContent = ReplaceLegacyApplicationTypeNamesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _currentApplicationNamespaceAliases,
                _canMigrateBareLegacyApplicationTypeName,
                _canPreserveBareCurrentToolContractsReferences,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
            _migratedContent = ReplaceLegacyRegistrarAliasesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                ref _replacementCount);
        }

        public void ApplyContractRenames()
        {
            if (!_shouldApplyContractRenames)
            {
                return;
            }

            _migratedContent = ReplaceLegacyDomainTypeNamesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
            _migratedContent = ReplaceLegacyContractTypeNamesInCode(
                _migratedContent,
                _legacyNamespaceAliases,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
            ApplyCSharpReplacementRules();
        }

        public void ApplyRegistrarRenames()
        {
            if (!_shouldApplyRegistrarRenames && !_shouldApplyDomainMetadataRenames)
            {
                return;
            }

            if (_shouldApplyRegistrarRenames)
            {
                _migratedContent = ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
                    _migratedContent,
                    ref _replacementCount);
            }

            ApplyRegistrarReplacementRules();
            _migratedContent = ReplaceLegacyToolInfoTypeReferencesInCode(
                _migratedContent,
                _assemblyDeclaredTypeNames,
                ref _replacementCount);
        }

        public void ApplyCurrentPublicContractNamespaceReplacements()
        {
            _migratedContent = ReplaceCurrentPublicContractNamespacesInCode(
                _migratedContent,
                ref _replacementCount);
        }

        public ThirdPartyToolMigrationContentResult CreateResult()
        {
            return new ThirdPartyToolMigrationContentResult(
                _migratedContent,
                _replacementCount,
                _removedPlayerLoopTimingSignatures.ToArray());
        }

        private void RemovePlayerLoopTimingParameters()
        {
            string[] migratedCalleeMethodNames = Array.Empty<string>();
            while (true)
            {
                (
                    string migratedContent,
                    int replacementCount,
                    RemovedLegacyPlayerLoopTimingSignature[] localRemovedTimingSignatures) =
                    RemoveLegacyPlayerLoopTimingParametersInCode(
                        _migratedContent,
                        _legacyNamespaceAliases,
                        _canMigrateBareLegacyApplicationApi,
                        migratedCalleeMethodNames);
                if (replacementCount == 0)
                {
                    return;
                }

                ApplyReplacementResult(migratedContent, replacementCount);
                _removedPlayerLoopTimingSignatures.AddRange(localRemovedTimingSignatures);
                (string timingCallerMigratedContent, int timingCallerReplacementCount) =
                    RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                        _migratedContent,
                        localRemovedTimingSignatures,
                        _legacyNamespaceAliases);
                ApplyReplacementResult(timingCallerMigratedContent, timingCallerReplacementCount);
                migratedCalleeMethodNames = localRemovedTimingSignatures
                    .Select(signature => signature.MethodName)
                    .ToArray();
            }
        }

        private void RemoveUnusedPlayerLoopTimingDeclarations()
        {
            (string migratedContent, int replacementCount) =
                RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(_migratedContent);
            ApplyReplacementResult(migratedContent, replacementCount);
        }

        private void ApplyCSharpReplacementRules()
        {
            foreach (ReplacementRule rule in CSharpReplacementRules)
            {
                _migratedContent = ReplaceRegexInCode(
                    _migratedContent,
                    rule.PatternRegex,
                    _ => rule.Replacement,
                    ref _replacementCount);
            }
        }

        private void ApplyRegistrarReplacementRules()
        {
            foreach (ReplacementRule rule in RegistrarReplacementRules)
            {
                _migratedContent = ReplaceRegexInCode(
                    _migratedContent,
                    rule.PatternRegex,
                    _ => rule.Replacement,
                    ref _replacementCount);
            }
        }

        private void ApplyReplacementResult(string migratedContent, int replacementCount)
        {
            _migratedContent = migratedContent;
            _replacementCount += replacementCount;
        }
    }
}
