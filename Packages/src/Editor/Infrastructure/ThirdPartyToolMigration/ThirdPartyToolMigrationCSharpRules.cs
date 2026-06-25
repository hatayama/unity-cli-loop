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
    internal static class ThirdPartyToolMigrationCSharpRules
    {
        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return MigrateCSharpSourceForLegacyAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                hasAssemblyScopedCurrentToolContractsUsing: false,
                hasAssemblyScopedCurrentApplicationUsing: false,
                hasAssemblyScopedCurrentDomainUsing: false,
                hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                legacyAssemblyAliases: Array.Empty<string>(),
                legacyAssemblyToolInfoAliases: Array.Empty<string>(),
                currentApplicationAssemblyAliases: Array.Empty<string>(),
                currentDomainAssemblyAliases: Array.Empty<string>(),
                currentFirstPartyToolsAssemblyAliases: Array.Empty<string>(),
                assemblyDeclaredTypeNames: Array.Empty<string>());
        }

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSourceForLegacyAssembly(
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
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");
            Debug.Assert(
                currentDomainAssemblyAliases != null,
                "currentDomainAssemblyAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CSharpLegacyAssemblyMigrationContext migrationContext =
                CSharpLegacyAssemblyMigrationContext.Create(
                    source,
                    hasLegacyAssemblySource,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentApplicationUsing,
                    hasAssemblyScopedCurrentDomainUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyAssemblyAliases,
                    legacyAssemblyToolInfoAliases,
                    currentApplicationAssemblyAliases,
                    currentDomainAssemblyAliases,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            migrationContext.ApplyToolMetadataReplacements();
            migrationContext.ApplyDelayReplacements();
            migrationContext.ApplyMainThreadSwitcherReplacements();
            migrationContext.ApplyEditorWindowCaptureAndTypeReplacements();
            migrationContext.ApplyContractRenames();
            migrationContext.ApplyRegistrarRenames();
            migrationContext.ApplyCurrentPublicContractNamespaceReplacements();
            return migrationContext.CreateResult();
        }

        /// <summary>
        /// Carries the derived migration context while applying C# source rewrite phases.
        /// </summary>
        private sealed class CSharpLegacyAssemblyMigrationContext
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
                    ref _replacementCount);
                _migratedContent = ReplaceLegacyContractTypeNamesInCode(
                    _migratedContent,
                    _legacyNamespaceAliases,
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

        private readonly struct CSharpLegacyAssemblyMigrationCapabilities
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
                bool hasLegacyNamespaceUsage = RegexMatchesCode(source, LegacyNamespaceRegex);
                bool hasLegacyNamespaceUsingDirective = RegexMatchesCode(source, LegacyNamespaceUsingRegex);
                bool hasCurrentApplicationNamespaceUsage =
                    RegexMatchesCode(source, CurrentApplicationNamespaceRegex);
                bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
                bool hasCurrentDomainUsingDirective =
                    RegexMatchesCode(source, CurrentDomainUsingRegex) ||
                    RegexMatchesCode(source, CurrentDomainGlobalUsingRegex);
                bool hasCurrentToolContractsNamespaceUsage =
                    RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
                bool hasCurrentToolContractsUsingDirective =
                    RegexMatchesCode(source, CurrentToolContractsUsingRegex);
                bool hasCurrentFirstPartyToolsNamespaceUsage =
                    RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
                bool hasCurrentFirstPartyToolsUsingDirective =
                    RegexMatchesCode(source, CurrentFirstPartyToolsUsingRegex) ||
                    RegexMatchesCode(source, CurrentFirstPartyToolsGlobalUsingRegex);
                bool canUseCurrentToolContracts =
                    hasCurrentToolContractsNamespaceUsage ||
                    hasAssemblyScopedCurrentToolContractsUsing;
                bool canUseBareCurrentToolContracts =
                    hasLegacyAssemblySource ||
                    hasLegacyNamespaceUsingDirective ||
                    hasCurrentToolContractsUsingDirective ||
                    hasAssemblyScopedCurrentToolContractsUsing;
                bool canMigrateBareLegacyToolAttribute =
                    hasLegacyAssemblySource ||
                    hasLegacyNamespaceUsage ||
                    legacyNamespaceAliases.Length > 0;
                bool canMigrateBareLegacyApplicationApi =
                    canMigrateBareLegacyToolAttribute ||
                    canUseCurrentToolContracts ||
                    hasAssemblyScopedCurrentApplicationUsing ||
                    hasCurrentApplicationNamespaceUsage;
                bool hasLocalLegacyMarker = ContainsLegacyToolMigrationMarker(source);
                bool shouldApplyContractRenames = hasLegacyAssemblySource || hasLocalLegacyMarker;
                return new CSharpLegacyAssemblyMigrationCapabilities(
                    canUseCurrentToolContracts,
                    canUseBareCurrentToolContracts,
                    hasCurrentToolContractsUsingDirective || hasAssemblyScopedCurrentToolContractsUsing,
                    canMigrateBareLegacyToolAttribute,
                    canMigrateBareLegacyToolAttribute ||
                    canUseCurrentToolContracts ||
                    hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                    hasCurrentFirstPartyToolsNamespaceUsage,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing || hasCurrentFirstPartyToolsUsingDirective,
                    !canUseBareCurrentToolContracts,
                    canMigrateBareLegacyToolAttribute ||
                    canUseCurrentToolContracts ||
                    hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                    hasCurrentFirstPartyToolsNamespaceUsage,
                    hasAssemblyScopedCurrentDomainUsing || hasCurrentDomainUsingDirective,
                    canMigrateBareLegacyApplicationApi,
                    canMigrateBareLegacyApplicationApi,
                    canMigrateBareLegacyToolAttribute && !hasCurrentDomainNamespaceUsage,
                    shouldApplyContractRenames,
                    shouldApplyContractRenames && RegexMatchesCode(source, LegacyRegistrarRegex),
                    shouldApplyContractRenames && RegexMatchesCode(source, LegacyDomainMetadataRegex));
            }
        }
    }
}
