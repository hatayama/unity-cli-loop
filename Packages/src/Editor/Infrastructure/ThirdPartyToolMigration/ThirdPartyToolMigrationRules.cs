using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Applies deterministic source rewrites for V2 custom tools that need the V3 public contract API.
    /// </summary>
    internal static class ThirdPartyToolMigrationRules
    {
        internal const string LegacyNamespace = "io.github.hatayama.uLoopMCP";
        internal const string CurrentNamespace = "io.github.hatayama.UnityCliLoop.ToolContracts";
        internal const string CurrentApplicationNamespace = "io.github.hatayama.UnityCliLoop.Application";
        internal const string CurrentDomainNamespace = "io.github.hatayama.UnityCliLoop.Domain";
        internal const string CurrentFirstPartyToolsNamespace = "io.github.hatayama.UnityCliLoop.FirstPartyTools";
        internal const string LegacyEditorAssemblyName = "uLoopMCP.Editor";
        internal const string LegacyRuntimeAssemblyName = "uLoopMCP.Runtime";
        private const string CurrentApplicationAssemblyName = "UnityCLILoop.Application";
        private const string CurrentDomainAssemblyName = "UnityCLILoop.Domain";
        private const string CurrentRuntimeAssemblyName = "UnityCLILoop.Runtime";
        private const string CurrentToolContractsAssemblyName = "UnityCLILoop.ToolContracts";
        internal const string LegacyEditorAssemblyGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentApplicationGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentDomainGuidReference = "GUID:5c4588558a3624eacbce0f50007cf1eb";
        internal const string CurrentRuntimeGuidReference = "GUID:c956a21f824994ef087b6de566690b3d";
        internal const string CurrentToolContractsGuidReference = "GUID:fc3fd32eddbee40e39c2d76dc184957b";
        private const string DescriptionAttributeArgumentName = "Description";
        private const string DisplayDevelopmentOnlyAttributeArgumentName = "DisplayDevelopmentOnly";
        private const string RequiredSecuritySettingAttributeArgumentName = "RequiredSecuritySetting";
        private const string LegacySecuritySettingsTypeName = "SecuritySettings";
        private const string CurrentSecuritySettingTypeName = "UnityCliLoopSecuritySetting";
        private const string LegacyEditorDelayTypeName = "EditorDelay";
        private const string LegacyEditorDelayMethodName = "DelayFrame";
        private const string CurrentEditorFrameWaiterTypeName = "EditorFrameWaiter";
        private const string CurrentEditorFrameWaiterMethodName = "WaitFramesOrTimeoutAsync";
        private const string LegacyEditorWindowCaptureUtilityTypeName = "EditorWindowCaptureUtility";
        private const string EditorWindowCaptureUtilityCaptureWindowMethodName = "CaptureWindowAsync";
        private const string CurrentConstantsTypeName = "UnityCliLoopConstants";
        private const string CurrentEditorFrameWaitTimeoutMemberName = "EDITOR_FRAME_WAIT_TIMEOUT_MS";
        private const int MinimumRawStringDelimiterQuoteCount = 3;
        private static readonly ConditionalWeakTable<string, CodeTextMaskHolder> CodeTextMaskCache = new();

        private static readonly string[] ExcludedDirectoryNames =
        {
            ".git",
            "Library",
            "Temp",
            "Logs",
            "obj",
            "bin",
            "Build",
            "Builds"
        };

        private static readonly string LegacyNamespacePattern =
            $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}(?=\.|;|\s|$)";

        private static readonly Regex LegacyNamespaceRegex =
            new(LegacyNamespacePattern, RegexOptions.Compiled);

        private static readonly Regex CurrentDomainNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentDomainNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        private static readonly Regex CurrentToolContractsNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        private static readonly Regex CurrentDomainMetadataRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\.ToolInfo\b",
                RegexOptions.Compiled);

        private static readonly Regex LegacyGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        private static readonly Regex CurrentDomainGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        private static readonly Regex LegacyGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        private static readonly Regex LegacyRegistrarRegex =
            new(@"\bCustomToolManager\b", RegexOptions.Compiled);

        private static readonly Regex LegacyQualifiedRegistrarRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                RegexOptions.Compiled);

        private static readonly Regex CurrentRegistrarRegex =
            new(@"\bUnityCliLoopToolRegistrar\b", RegexOptions.Compiled);

        private static readonly Regex LegacyQualifiedRegistrarDomainReturnRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex LegacyRegistrarDomainReturnRegex =
            new(@"\bCustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        private static readonly Regex CurrentRegistrarDomainReturnRegex =
            new(@"\bUnityCliLoopToolRegistrar\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        private static readonly Regex LegacyDomainMetadataRegex =
            new(@"\bToolInfo\b", RegexOptions.Compiled);

        private static readonly Regex LegacyBaseTypeUsageRegex =
            new(
                @":\s*[^;{}=]*\b(?:AbstractUnityTool|BaseToolSchema|BaseToolResponse)\b",
                RegexOptions.Compiled);

        private static readonly Regex LegacyAssemblyScopedApiUsageRegex =
            new(
                @"\b(?:IUnityTool\s+[A-Za-z_][A-Za-z0-9_]*|" +
                @"ToolParameterSchemaGenerator\s*\.|" +
                @"new\s+ParameterValidationException\b|" +
                @"CustomToolManager\s*\.|" +
                @"ToolInfo\s*(?:\[\])?\s+[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);

        private static readonly Regex LegacyNamespaceAliasRegex =
            new(
                @"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?" +
                @"io\.github\.hatayama\.uLoopMCP\s*;",
                RegexOptions.Compiled);

        private static readonly Regex LegacyToolAttributeEntryRegex =
            new(
                @"^\s*(?:(?<qualifier>(?:global::)?io\.github\.hatayama\.uLoopMCP\.)|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.)?" +
                @"McpTool(?:Attribute)?\s*(?:\((?<arguments>[\s\S]*)\))?\s*$",
                RegexOptions.Compiled);

        private static readonly Regex LegacyToolInfoConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolInfo|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolInfo|(?<toolInfo>ToolInfo)|(?<typeAlias>[A-Za-z_][A-Za-z0-9_]*))\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex LegacyToolInfoTypeAliasRegex =
            new(
                $@"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        private static readonly Regex LegacyGlobalToolInfoTypeAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        private static readonly Regex LegacyToolSettingsCatalogItemConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolSettingsCatalogItem|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolSettingsCatalogItem|(?<toolSettingsCatalogItem>ToolSettingsCatalogItem))\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex LegacyEditorDelayFrameRegex =
            new(
                $@"(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorDelayTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorDelayTypeName}|(?<editorDelay>{LegacyEditorDelayTypeName}))\s*\.\s*{LegacyEditorDelayMethodName}\s*\(",
                RegexOptions.Compiled);

        private static readonly Regex LegacyEditorWindowCaptureUtilityCaptureWindowRegex =
            new(
                $@"await\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureWindowMethodName}\s*\(",
                RegexOptions.Compiled);

        private static readonly TypeReplacementRule[] ToolContractTypeReplacementRules =
        {
            new("ToolParameterSchemaGenerator", "UnityCliLoopToolParameterSchemaGenerator"),
            new("ParameterValidationException", "UnityCliLoopToolParameterValidationException"),
            new("Mcp" + "Constants", CurrentConstantsTypeName),
            new("McpToolAttribute", "UnityCliLoopToolAttribute"),
            new("IUnityTool", "IUnityCliLoopTool"),
            new("AbstractUnityTool", "UnityCliLoopTool"),
            new("BaseToolSchema", "UnityCliLoopToolSchema"),
            new("BaseToolResponse", "UnityCliLoopToolResponse"),
            new("SecuritySettings", CurrentSecuritySettingTypeName)
        };

        private static readonly TypeReplacementRule[] DomainTypeReplacementRules =
        {
            new("ServiceResult", "ServiceResult"),
            new("ToolSettingsCatalogItem", "ToolSettingsCatalogItem")
        };

        private static readonly ReplacementRule[] CSharpReplacementRules =
        {
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                $"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar"),
            new(LegacyNamespacePattern, CurrentNamespace)
        };

        private static readonly ReplacementRule[] RegistrarReplacementRules =
        {
            new(Regex.Escape($"{CurrentNamespace}.ToolInfo"), $"{CurrentDomainNamespace}.ToolInfo")
        };

        private static readonly Regex UnqualifiedToolInfoRegex =
            new(@"(?<![\.:])\bToolInfo\b(?!\s*=)", RegexOptions.Compiled);

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return MigrateCSharpSourceForLegacyAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>(),
                legacyAssemblyToolInfoAliases: Array.Empty<string>());
        }

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSourceForLegacyAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases,
            string[] legacyAssemblyToolInfoAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");

            string migratedContent = source;
            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            bool hasLegacyNamespaceUsage = RegexMatchesCode(source, LegacyNamespaceRegex);
            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            bool hasCurrentToolContractsNamespaceUsage = RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
            bool canMigrateBareLegacyToolAttribute =
                hasLegacyAssemblySource ||
                hasLegacyNamespaceUsage ||
                legacyNamespaceAliases.Length > 0;
            bool canMigrateBareLegacyEditorWindowCaptureUtility =
                canMigrateBareLegacyToolAttribute ||
                hasCurrentToolContractsNamespaceUsage;
            bool canMigrateBareLegacyToolInfoConstructor =
                canMigrateBareLegacyToolAttribute;
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor =
                canMigrateBareLegacyToolAttribute &&
                !hasCurrentDomainNamespaceUsage;
            bool hasLocalLegacyMarker = ContainsLegacyToolMigrationMarker(source);
            bool shouldApplyContractRenames = hasLegacyAssemblySource || hasLocalLegacyMarker;
            bool shouldApplyRegistrarRenames = shouldApplyContractRenames &&
                RegexMatchesCode(source, LegacyRegistrarRegex);
            bool shouldApplyDomainMetadataRenames = shouldApplyContractRenames &&
                RegexMatchesCode(source, LegacyDomainMetadataRegex);
            int replacementCount = 0;
            migratedContent = ReplaceLegacyToolAttributesInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolAttribute,
                ref replacementCount);
            migratedContent = ReplaceLegacyToolInfoConstructorsInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolInfoConstructor,
                canMigrateAmbiguousBareLegacyToolInfoConstructor,
                legacyAssemblyToolInfoAliases,
                ref replacementCount);
            migratedContent = ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolAttribute,
                ref replacementCount);
            (string editorDelayMigratedContent, int editorDelayReplacementCount) =
                ReplaceLegacyEditorDelayFrameCallsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    canMigrateBareLegacyToolAttribute);
            migratedContent = editorDelayMigratedContent;
            replacementCount += editorDelayReplacementCount;
            (string editorWindowCaptureMigratedContent, int editorWindowCaptureReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility);
            migratedContent = editorWindowCaptureMigratedContent;
            replacementCount += editorWindowCaptureReplacementCount;
            migratedContent = ReplaceLegacyRegistrarAliasesInCode(
                migratedContent,
                legacyNamespaceAliases,
                ref replacementCount);

            if (shouldApplyContractRenames)
            {
                migratedContent = ReplaceLegacyDomainTypeNamesInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    ref replacementCount);

                migratedContent = ReplaceLegacyContractTypeNamesInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    ref replacementCount);

                foreach (ReplacementRule rule in CSharpReplacementRules)
                {
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        rule.PatternRegex,
                        _ => rule.Replacement,
                    ref replacementCount);
                }
            }

            if (shouldApplyRegistrarRenames || shouldApplyDomainMetadataRenames)
            {
                if (shouldApplyRegistrarRenames)
                {
                    migratedContent = ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
                        migratedContent,
                        ref replacementCount);
                }

                foreach (ReplacementRule rule in RegistrarReplacementRules)
                {
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        rule.PatternRegex,
                        _ => rule.Replacement,
                        ref replacementCount);
                }

                migratedContent = ReplaceLegacyToolInfoTypeReferencesInCode(
                    migratedContent,
                    ref replacementCount);
            }

            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount);
        }

        internal static ThirdPartyToolMigrationContentResult MigrateAsmdefSource(
            string source,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference)
        {
            Debug.Assert(source != null, "source must not be null");

            JObject asmdef = JObject.Parse(source);
            JToken referencesToken = asmdef["references"];
            JArray references = referencesToken == null ? new JArray() : referencesToken as JArray;
            if (referencesToken != null && references == null)
            {
                return new ThirdPartyToolMigrationContentResult(source, 0);
            }

            int replacementCount = 0;
            HashSet<string> addedReferences = new(StringComparer.Ordinal);
            JArray migratedReferences = new();
            foreach (JToken referenceToken in references)
            {
                string reference = referenceToken.Value<string>() ?? string.Empty;
                string[] migratedReferenceItems = GetMigratedAsmdefReferences(
                    reference,
                    hasLegacyCSharpSource,
                    requiresToolContractsReference,
                    requiresApplicationReference,
                    requiresDomainReference);
                bool referenceChanged = migratedReferenceItems.Length != 1 ||
                    !string.Equals(migratedReferenceItems[0], reference, StringComparison.Ordinal);
                if (referenceChanged)
                {
                    replacementCount++;
                }

                foreach (string migratedReference in migratedReferenceItems)
                {
                    string migratedReferenceKey = GetCurrentAsmdefReferenceKey(migratedReference);
                    if (!addedReferences.Add(migratedReferenceKey))
                    {
                        continue;
                    }

                    migratedReferences.Add(migratedReference);
                }
            }

            AddRequiredCurrentAsmdefReferences(
                migratedReferences,
                addedReferences,
                hasLegacyCSharpSource || requiresToolContractsReference,
                requiresApplicationReference,
                requiresDomainReference,
                ref replacementCount);

            if (replacementCount == 0)
            {
                return new ThirdPartyToolMigrationContentResult(source, 0);
            }

            asmdef["references"] = migratedReferences;
            return new ThirdPartyToolMigrationContentResult(
                asmdef.ToString(Formatting.Indented),
                replacementCount);
        }

        internal static bool ContainsLegacyCSharpApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyToolMigrationMarker(source);
        }

        internal static bool ContainsMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (ContainsTextFragment(source, LegacyNamespace) ||
                ContainsTextFragment(source, CurrentNamespace) ||
                ContainsTextFragment(source, CurrentApplicationNamespace) ||
                ContainsTextFragment(source, CurrentDomainNamespace) ||
                ContainsTextFragment(source, "McpTool") ||
                ContainsTextFragment(source, "CustomToolManager") ||
                ContainsTextFragment(source, LegacyEditorDelayTypeName) ||
                ContainsTextFragment(source, LegacyEditorWindowCaptureUtilityTypeName) ||
                ContainsTextFragment(source, "UnityCliLoopToolRegistrar") ||
                ContainsTextFragment(source, "ToolInfo"))
            {
                return true;
            }

            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsLegacyMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsTextFragment(source, LegacyNamespace) ||
                ContainsTextFragment(source, LegacyEditorAssemblyName) ||
                ContainsTextFragment(source, LegacyRuntimeAssemblyName);
        }

        internal static bool ContainsLegacyAsmdefNameReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, LegacyEditorAssemblyName) &&
                !ContainsTextFragment(source, LegacyRuntimeAssemblyName))
            {
                return false;
            }

            JObject asmdef = JObject.Parse(source);
            if (asmdef["references"] is not JArray references)
            {
                return false;
            }

            foreach (JToken reference in references)
            {
                string referenceValue = reference.Value<string>() ?? string.Empty;
                if (string.Equals(referenceValue, LegacyEditorAssemblyName, StringComparison.Ordinal) ||
                    string.Equals(referenceValue, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsLegacyRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyRegistrarApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsLegacyRegistrarApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            bool canMigrateBareLegacyRegistrar =
                hasLegacyAssemblySource ||
                ContainsLegacyToolMigrationMarker(source) ||
                legacyNamespaceAliases.Length > 0;
            return ContainsLegacyRegistrarReference(
                source,
                canMigrateBareLegacyRegistrar,
                legacyNamespaceAliases);
        }

        internal static bool ContainsCurrentRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentRegistrarRegex);
        }

        internal static bool ContainsRegistrarDomainReturnApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsRegistrarDomainReturnApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsRegistrarDomainReturnApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            if (RegexMatchesCode(source, CurrentRegistrarDomainReturnRegex))
            {
                return true;
            }

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            bool canMigrateBareLegacyRegistrar =
                hasLegacyAssemblySource ||
                ContainsLegacyToolMigrationMarker(source) ||
                legacyNamespaceAliases.Length > 0;
            return ContainsLegacyRegistrarDomainReturnReference(
                source,
                canMigrateBareLegacyRegistrar,
                legacyNamespaceAliases);
        }

        internal static bool ContainsCurrentToolContractsApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
        }

        internal static bool ContainsLegacyDomainMetadataApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyDomainMetadataRegex) ||
                ContainsLegacyDomainHelperApiForAssembly(
                    source,
                    hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                    legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsLegacyDomainHelperApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            return ContainsLegacyDomainHelperReference(
                source,
                hasLegacyAssemblySource,
                legacyNamespaceAliases);
        }

        internal static bool ContainsCurrentDomainMetadataApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsCurrentDomainMetadataApiForAssembly(
                source,
                hasAssemblyScopedCurrentDomainUsing: false);
        }

        internal static bool ContainsCurrentDomainMetadataApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentDomainUsing)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            bool canUseBareCurrentDomainType = hasAssemblyScopedCurrentDomainUsing || hasCurrentDomainNamespaceUsage;
            return RegexMatchesCode(source, CurrentDomainMetadataRegex) ||
                ContainsCurrentDomainHelperApiForAssembly(source, canUseBareCurrentDomainType) ||
                (canUseBareCurrentDomainType && RegexMatchesCode(source, LegacyDomainMetadataRegex));
        }

        internal static bool ContainsLegacyAssemblyScopedApi(string source, string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            return RegexMatchesCode(source, LegacyBaseTypeUsageRegex) ||
                RegexMatchesCode(source, LegacyAssemblyScopedApiUsageRegex) ||
                ContainsLegacyAssemblyScopedTypeReference(source) ||
                ContainsLegacyAliasQualifiedAssemblyScopedApi(source, legacyAssemblyAliases) ||
                ContainsLegacyEditorDelayFrameCall(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyEditorDelay: true) ||
                ContainsLegacyEditorWindowCaptureUtilityCall(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility: true) ||
                ContainsLegacyToolAttributeList(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyToolAttribute: true);
        }

        internal static bool ContainsLegacyGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalUsingRegex);
        }

        internal static bool ContainsCurrentDomainGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainGlobalUsingRegex);
        }

        internal static string[] GetLegacyGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalNamespaceAliasRegex, "alias");
        }

        internal static string[] GetLegacyGlobalToolInfoTypeAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalToolInfoTypeAliasRegex, "alias");
        }

        internal static bool ContainsLegacyGlobalToolInfoTypeAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalToolInfoTypeAliasRegex);
        }

        internal static bool ContainsLegacyTypeAliasReference(string source, string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (string alias in aliases)
            {
                Regex aliasRegex = new($@"(?<![\w.]){Regex.Escape(alias)}\b", RegexOptions.Compiled);
                MatchCollection matches = aliasRegex.Matches(source);
                foreach (Match match in matches)
                {
                    if (codeTextMask.IsCodeAt(match.Index))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool IsExcludedDirectoryName(string directoryName)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryName), "directoryName must not be null or empty");

            return ExcludedDirectoryNames.Any(
                excludedDirectoryName => string.Equals(
                    excludedDirectoryName,
                    directoryName,
                    StringComparison.OrdinalIgnoreCase));
        }

        internal static string[] GetExcludedDirectoryNames()
        {
            string[] names = new string[ExcludedDirectoryNames.Length];
            Array.Copy(ExcludedDirectoryNames, names, ExcludedDirectoryNames.Length);
            return names;
        }

        private static string[] GetMigratedAsmdefReferences(
            string reference,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference)
        {
            if (string.Equals(reference, LegacyEditorAssemblyName, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(requiresApplicationReference, requiresDomainReference);
            }

            if (string.Equals(reference, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
            {
                return new[] { CurrentRuntimeGuidReference };
            }

            if (hasLegacyCSharpSource &&
                string.Equals(reference, LegacyEditorAssemblyGuidReference, StringComparison.Ordinal))
            {
                return GetMigratedLegacyEditorReferences(requiresApplicationReference, requiresDomainReference);
            }

            return new[] { reference };
        }

        private static string[] GetMigratedLegacyEditorReferences(
            bool requiresApplicationReference,
            bool requiresDomainReference)
        {
            List<string> references = new()
            {
                CurrentToolContractsGuidReference
            };

            if (requiresApplicationReference)
            {
                references.Add(CurrentApplicationGuidReference);
            }

            if (requiresDomainReference)
            {
                references.Add(CurrentDomainGuidReference);
            }

            return references.ToArray();
        }

        private static void AddRequiredCurrentAsmdefReferences(
            JArray references,
            HashSet<string> addedReferences,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            ref int replacementCount)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");

            if (requiresToolContractsReference || requiresApplicationReference || requiresDomainReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentToolContractsGuidReference,
                    ref replacementCount);
            }

            if (requiresApplicationReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentApplicationGuidReference,
                    ref replacementCount);
            }

            if (requiresDomainReference)
            {
                AddRequiredCurrentAsmdefReference(
                    references,
                    addedReferences,
                    CurrentDomainGuidReference,
                    ref replacementCount);
            }
        }

        private static void AddRequiredCurrentAsmdefReference(
            JArray references,
            HashSet<string> addedReferences,
            string reference,
            ref int replacementCount)
        {
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(addedReferences != null, "addedReferences must not be null");
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            string referenceKey = GetCurrentAsmdefReferenceKey(reference);
            if (!addedReferences.Add(referenceKey))
            {
                return;
            }

            references.Add(reference);
            replacementCount++;
        }

        private static string GetCurrentAsmdefReferenceKey(string reference)
        {
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentRuntimeAssemblyName,
                    CurrentRuntimeGuidReference))
            {
                return CurrentRuntimeAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentToolContractsAssemblyName,
                    CurrentToolContractsGuidReference))
            {
                return CurrentToolContractsAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentApplicationAssemblyName,
                    CurrentApplicationGuidReference))
            {
                return CurrentApplicationAssemblyName;
            }

            if (IsCurrentAsmdefReference(
                    reference,
                    CurrentDomainAssemblyName,
                    CurrentDomainGuidReference))
            {
                return CurrentDomainAssemblyName;
            }

            return reference;
        }

        private static bool IsCurrentAsmdefReference(
            string reference,
            string assemblyName,
            string guidReference)
        {
            Debug.Assert(!string.IsNullOrEmpty(reference), "reference must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(guidReference), "guidReference must not be null or empty");

            return string.Equals(reference, assemblyName, StringComparison.Ordinal) ||
                string.Equals(reference, guidReference, StringComparison.Ordinal);
        }

        private static bool TryMigrateLegacyToolAttributeList(
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

        private static bool TryMigrateLegacyToolAttributeEntry(
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

        private static string[] GetMigratedSupportedAttributeArguments(string argumentsSource)
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

        private static string[] SplitAttributeArguments(string argumentsSource)
        {
            Debug.Assert(argumentsSource != null, "argumentsSource must not be null");

            List<string> arguments = new();
            int argumentStartIndex = 0;
            int nestingDepth = 0;
            bool isInRegularString = false;
            bool isInVerbatimString = false;
            bool isInCharLiteral = false;
            bool isInRawString = false;
            int rawStringQuoteCount = 0;
            for (int i = 0; i < argumentsSource.Length; i++)
            {
                char current = argumentsSource[i];
                if (isInRegularString)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"')
                    {
                        isInRegularString = false;
                    }

                    continue;
                }

                if (isInVerbatimString)
                {
                    if (current != '"')
                    {
                        continue;
                    }

                    if (i + 1 < argumentsSource.Length && argumentsSource[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    isInVerbatimString = false;
                    continue;
                }

                if (isInRawString)
                {
                    if (HasRepeatedCharacterAt(argumentsSource, i, '"', rawStringQuoteCount))
                    {
                        i += rawStringQuoteCount - 1;
                        isInRawString = false;
                    }

                    continue;
                }

                if (isInCharLiteral)
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == '\'')
                    {
                        isInCharLiteral = false;
                    }

                    continue;
                }

                if (IsRawStringStart(argumentsSource, i))
                {
                    int dollarCount = CountRepeatedCharacter(argumentsSource, i, '$');
                    int quoteIndex = i + dollarCount;
                    rawStringQuoteCount = CountRepeatedCharacter(argumentsSource, quoteIndex, '"');
                    isInRawString = true;
                    i = quoteIndex + rawStringQuoteCount - 1;
                    continue;
                }

                if (StartsWith(argumentsSource, i, "@\"") ||
                    StartsWith(argumentsSource, i, "$@\"") ||
                    StartsWith(argumentsSource, i, "@$\""))
                {
                    isInVerbatimString = true;
                    i += GetStringPrefixLength(argumentsSource, i);
                    continue;
                }

                if (StartsWith(argumentsSource, i, "$\""))
                {
                    isInRegularString = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    isInRegularString = true;
                    continue;
                }

                if (current == '\'')
                {
                    isInCharLiteral = true;
                    continue;
                }

                if (current == '(' || current == '[' || current == '{')
                {
                    nestingDepth++;
                    continue;
                }

                if (current == ')' || current == ']' || current == '}')
                {
                    nestingDepth = Math.Max(0, nestingDepth - 1);
                    continue;
                }

                if (current != ',' || nestingDepth != 0)
                {
                    continue;
                }

                arguments.Add(argumentsSource.Substring(argumentStartIndex, i - argumentStartIndex));
                argumentStartIndex = i + 1;
            }

            arguments.Add(argumentsSource.Substring(argumentStartIndex));
            return arguments.ToArray();
        }

        private static string[] GetLegacyNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyNamespaceAliasRegex, "alias");
        }

        private static string[] GetLegacyToolInfoTypeAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyToolInfoTypeAliasRegex, "alias");
        }

        private static string[] GetCombinedLegacyNamespaceAliases(
            string source,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            return GetLegacyNamespaceAliases(source)
                .Concat(legacyAssemblyAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetCombinedLegacyToolInfoTypeAliases(
            string source,
            string[] legacyAssemblyToolInfoAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");

            return GetLegacyToolInfoTypeAliases(source)
                .Concat(legacyAssemblyToolInfoAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetRegexGroupValuesInCode(string source, Regex regex, string groupName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(!string.IsNullOrEmpty(groupName), "groupName must not be null or empty");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            List<string> values = new();
            MatchCollection matches = regex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                values.Add(match.Groups[groupName].Value);
            }

            return values.ToArray();
        }

        private static string ReplaceLegacyRegistrarAliasesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (string alias in aliases)
            {
                Regex aliasRegistrarRegex = new(
                    $@"(?<!\w){Regex.Escape(alias)}\.CustomToolManager\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    aliasRegistrarRegex,
                    _ => $"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar",
                    ref replacementCount);

                Regex aliasToolInfoRegex = new(
                    $@"(?<!\w){Regex.Escape(alias)}\.ToolInfo\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    aliasToolInfoRegex,
                    _ => $"{CurrentDomainNamespace}.ToolInfo",
                    ref replacementCount);
            }

            return migratedContent;
        }

        private static string ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
            string source,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            Regex unqualifiedRegistrarRegex =
                new(@"(?<![\.:])\bCustomToolManager\b(?!\s*=)", RegexOptions.Compiled);
            return ReplaceRegexInCode(
                source,
                unqualifiedRegistrarRegex,
                match => ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index)
                    ? $"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar"
                    : match.Value,
                ref replacementCount);
        }

        private static string ReplaceLegacyContractTypeNamesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    fullyQualifiedRegex,
                    _ => $"{CurrentNamespace}.{rule.CurrentName}",
                    ref replacementCount);

                foreach (string alias in aliases)
                {
                    Regex aliasRegex = new(
                        $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.LegacyName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        aliasRegex,
                        _ => $"{alias}.{rule.CurrentName}",
                        ref replacementCount);
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? rule.CurrentName
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        private static string ReplaceLegacyDomainTypeNamesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    fullyQualifiedRegex,
                    _ => $"{CurrentDomainNamespace}.{rule.CurrentName}",
                    ref replacementCount);

                foreach (string alias in aliases)
                {
                    Regex aliasRegex = new(
                        $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.LegacyName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        aliasRegex,
                        _ => $"{CurrentDomainNamespace}.{rule.CurrentName}",
                        ref replacementCount);
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? $"{CurrentDomainNamespace}.{rule.CurrentName}"
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        private static string ReplaceLegacyToolInfoTypeReferencesInCode(string source, ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            return ReplaceRegexInCode(
                source,
                UnqualifiedToolInfoRegex,
                match => ShouldMigrateLegacyToolInfoTypeReference(source, match.Index)
                    ? $"{CurrentDomainNamespace}.ToolInfo"
                    : match.Value,
                ref replacementCount);
        }

        private static bool ShouldMigrateLegacyToolInfoTypeReference(string source, int toolInfoIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(toolInfoIndex >= 0, "toolInfoIndex must not be negative");

            return ShouldMigrateLegacyTypeReference(source, "ToolInfo", toolInfoIndex);
        }

        private static bool ShouldMigrateLegacyTypeReference(string source, string typeName, int typeNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be empty");
            Debug.Assert(typeNameIndex >= 0, "typeNameIndex must not be negative");

            if (IsLegacyAssemblyScopedTypeDeclaration(source, typeNameIndex))
            {
                return false;
            }

            char nextCharacter = ReadNextNonWhitespaceCharacter(source, typeNameIndex + typeName.Length);
            char previousCharacter = ReadPreviousNonWhitespaceCharacter(source, typeNameIndex);
            if (nextCharacter == '(' && !PreviousCodeTokenEquals(source, typeNameIndex, "new"))
            {
                return false;
            }

            return !IsDeclarationIdentifierTerminator(nextCharacter) ||
                !CanPrecedeDeclarationIdentifier(previousCharacter);
        }

        private static string ReplaceLegacyToolInfoConstructorsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolInfoConstructor,
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor,
            string[] legacyAssemblyToolInfoAliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            string[] legacyToolInfoTypeAliases =
                GetCombinedLegacyToolInfoTypeAliases(source, legacyAssemblyToolInfoAliases);
            MatchCollection matches = LegacyToolInfoConstructorRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int localReplacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyToolInfoConstructorMatch(
                        match,
                        legacyNamespaceAliases,
                        legacyToolInfoTypeAliases,
                        canMigrateBareLegacyToolInfoConstructor))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string argumentsSource = source.Substring(
                    openParenthesisIndex + 1,
                    closingParenthesisIndex - openParenthesisIndex - 1);
                string[] arguments = SplitAttributeArguments(argumentsSource);
                if (!ShouldMigrateLegacyToolInfoConstructorArguments(
                        match,
                        arguments,
                        canMigrateAmbiguousBareLegacyToolInfoConstructor))
                {
                    continue;
                }

                string[] migratedArguments = GetMigratedToolInfoConstructorArguments(arguments);
                if (migratedArguments.Length == arguments.Length)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append($"new {CurrentDomainNamespace}.ToolInfo(");
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                localReplacementCount++;
            }

            if (localReplacementCount == 0)
            {
                return source;
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        private static string ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyConstructor,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyToolSettingsCatalogItemConstructorRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int localReplacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyToolSettingsCatalogItemConstructorMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyConstructor))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string argumentsSource = source.Substring(
                    openParenthesisIndex + 1,
                    closingParenthesisIndex - openParenthesisIndex - 1);
                string[] arguments = SplitAttributeArguments(argumentsSource);
                string[] migratedArguments = GetMigratedToolSettingsCatalogItemConstructorArguments(arguments);
                if (migratedArguments.Length == arguments.Length)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append($"new {CurrentDomainNamespace}.ToolSettingsCatalogItem(");
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                localReplacementCount++;
            }

            if (localReplacementCount == 0)
            {
                return source;
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        private static bool IsLegacyToolSettingsCatalogItemConstructorMatch(
            Match match,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyConstructor)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return legacyNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            return match.Groups["toolSettingsCatalogItem"].Success && canMigrateBareLegacyConstructor;
        }

        private static (string Content, int ReplacementCount) ReplaceLegacyEditorDelayFrameCallsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorDelay)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorDelayFrameRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorDelayFrameCallMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyEditorDelay))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string argumentsSource = source.Substring(
                    openParenthesisIndex + 1,
                    closingParenthesisIndex - openParenthesisIndex - 1);
                string[] arguments = SplitAttributeArguments(argumentsSource);
                string[] migratedArguments = GetMigratedEditorDelayFrameArguments(
                    arguments,
                    GetMigratedEditorFrameWaitTimeoutExpression(match));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append(GetMigratedEditorFrameWaiterInvocationTarget(match));
                builder.Append('.');
                builder.Append(CurrentEditorFrameWaiterMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        private static bool IsLegacyEditorDelayFrameCallMatch(
            Match match,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorDelay)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return legacyNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            return match.Groups["editorDelay"].Success && canMigrateBareLegacyEditorDelay;
        }

        private static string GetMigratedEditorFrameWaiterInvocationTarget(Match match)
        {
            Debug.Assert(match != null, "match must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return $"{CurrentNamespace}.{CurrentEditorFrameWaiterTypeName}";
            }

            if (match.Groups["alias"].Success)
            {
                return $"{match.Groups["alias"].Value}.{CurrentEditorFrameWaiterTypeName}";
            }

            return CurrentEditorFrameWaiterTypeName;
        }

        private static string GetMigratedEditorFrameWaitTimeoutExpression(Match match)
        {
            Debug.Assert(match != null, "match must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            if (match.Groups["alias"].Success)
            {
                return $"{match.Groups["alias"].Value}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            return $"{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
        }

        private static string[] GetMigratedEditorDelayFrameArguments(
            string[] arguments,
            string timeoutExpression)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(!string.IsNullOrEmpty(timeoutExpression), "timeoutExpression must not be null or empty");

            string[] trimmedArguments = arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
            if (trimmedArguments.Length > 2)
            {
                return Array.Empty<string>();
            }

            string frameCountArgument = "1";
            string cancellationTokenArgument = null;
            bool hasFrameCountArgument = false;
            bool hasCancellationTokenArgument = false;
            for (int i = 0; i < trimmedArguments.Length; i++)
            {
                string argument = trimmedArguments[i];
                string namedFrameCountValue = GetNamedArgumentValueOrNull(argument, "frameCount");
                if (namedFrameCountValue != null)
                {
                    if (hasFrameCountArgument)
                    {
                        return Array.Empty<string>();
                    }

                    frameCountArgument = namedFrameCountValue;
                    hasFrameCountArgument = true;
                    continue;
                }

                string namedCancellationTokenValue = GetNamedArgumentValueOrNull(argument, "cancellationToken");
                if (namedCancellationTokenValue != null)
                {
                    if (hasCancellationTokenArgument)
                    {
                        return Array.Empty<string>();
                    }

                    cancellationTokenArgument = namedCancellationTokenValue;
                    hasCancellationTokenArgument = true;
                    continue;
                }

                if (!hasFrameCountArgument)
                {
                    frameCountArgument = argument;
                    hasFrameCountArgument = true;
                    continue;
                }

                if (!hasCancellationTokenArgument)
                {
                    cancellationTokenArgument = argument;
                    hasCancellationTokenArgument = true;
                    continue;
                }

                return Array.Empty<string>();
            }

            List<string> migratedArguments = new()
            {
                frameCountArgument,
                timeoutExpression
            };
            if (cancellationTokenArgument != null)
            {
                migratedArguments.Add(cancellationTokenArgument);
            }

            return migratedArguments.ToArray();
        }

        private static string GetNamedArgumentValueOrNull(string argument, string argumentName)
        {
            Debug.Assert(argument != null, "argument must not be null");
            Debug.Assert(!string.IsNullOrEmpty(argumentName), "argumentName must not be null or empty");

            int colonIndex = argument.IndexOf(':');
            if (colonIndex <= 0)
            {
                return null;
            }

            string possibleArgumentName = argument.Substring(0, colonIndex).Trim();
            if (!string.Equals(possibleArgumentName, argumentName, StringComparison.Ordinal))
            {
                return null;
            }

            string value = argument.Substring(colonIndex + 1).Trim();
            return value.Length == 0 ? null : value;
        }

        private static (string Content, int ReplacementCount) ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureWindowRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string argumentsSource = source.Substring(
                    openParenthesisIndex + 1,
                    closingParenthesisIndex - openParenthesisIndex - 1);
                string[] migratedArguments = GetMigratedEditorWindowCaptureUtilityArguments(
                    SplitAttributeArguments(argumentsSource));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append("(await ");
                builder.Append(CurrentFirstPartyToolsNamespace);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                builder.Append('.');
                builder.Append(EditorWindowCaptureUtilityCaptureWindowMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(")).texture");
                sourceCopyIndex = closingParenthesisIndex + 1;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        private static bool IsLegacyEditorWindowCaptureUtilityCallMatch(
            Match match,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return legacyNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            return match.Groups["editorWindowCaptureUtility"].Success &&
                canMigrateBareLegacyEditorWindowCaptureUtility;
        }

        private static string[] GetMigratedEditorWindowCaptureUtilityArguments(string[] arguments)
        {
            Debug.Assert(arguments != null, "arguments must not be null");

            string[] trimmedArguments = arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
            if (trimmedArguments.Length != 3)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                trimmedArguments[0],
                trimmedArguments[1],
                $"{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}",
                trimmedArguments[2]
            };
        }

        private static bool ShouldMigrateLegacyToolInfoConstructorArguments(
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

        private static bool HasUnambiguousLegacyToolInfoConstructorArguments(string[] arguments)
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

        private static bool IsLikelyLegacyDescriptionArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            string trimmedArgument = argument.Trim();
            return IsStringLiteralArgument(trimmedArgument) ||
                string.Equals(trimmedArgument, "description", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStringLiteralArgument(string argument)
        {
            Debug.Assert(argument != null, "argument must not be null");

            return argument.StartsWith("\"", StringComparison.Ordinal) ||
                argument.StartsWith("@\"", StringComparison.Ordinal) ||
                argument.StartsWith("$\"", StringComparison.Ordinal) ||
                argument.StartsWith("$@\"", StringComparison.Ordinal) ||
                argument.StartsWith("@$\"", StringComparison.Ordinal);
        }

        private static bool IsLegacyToolInfoConstructorMatch(
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

        private static string[] GetMigratedToolInfoConstructorArguments(string[] arguments)
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

        private static string[] GetMigratedToolSettingsCatalogItemConstructorArguments(string[] arguments)
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

        private static int FindNamedConstructorArgumentIndex(string[] arguments, string argumentName)
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

        private static bool IsNamedConstructorArgument(string argument, string argumentName)
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

        private static string[] RemoveArgumentAt(string[] arguments, int removeIndex)
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

        private static int FindInvocationClosingParenthesisIndex(
            string source,
            CodeTextMask codeTextMask,
            int openParenthesisIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openParenthesisIndex >= 0, "openParenthesisIndex must not be negative");

            int nestedParenthesisDepth = 0;
            for (int i = openParenthesisIndex + 1; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '(')
                {
                    nestedParenthesisDepth++;
                    continue;
                }

                if (source[i] != ')')
                {
                    continue;
                }

                if (nestedParenthesisDepth == 0)
                {
                    return i;
                }

                nestedParenthesisDepth--;
            }

            return -1;
        }

        private static bool IsNamedAttributeArgument(string argument, string argumentName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(argument), "argument must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(argumentName), "argumentName must not be null or whitespace");

            if (!argument.StartsWith(argumentName, StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = argumentName.Length; i < argument.Length; i++)
            {
                char current = argument[i];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                return current == '=';
            }

            return false;
        }

        private static string ReplaceRegexInCode(
            string source,
            Regex regex,
            Func<Match, string> replacementFactory,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(replacementFactory != null, "replacementFactory must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            int localReplacementCount = 0;
            string migrated = regex.Replace(
                source,
                match =>
                {
                    if (!codeTextMask.IsCodeAt(match.Index))
                    {
                        return match.Value;
                    }

                    string replacement = replacementFactory(match);
                    if (string.Equals(match.Value, replacement, StringComparison.Ordinal))
                    {
                        return match.Value;
                    }

                    localReplacementCount++;
                    return replacement;
                });
            replacementCount += localReplacementCount;
            return migrated;
        }

        private static string ReplaceLegacyToolAttributesInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolAttribute,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            StringBuilder builder = new(source.Length);
            int localReplacementCount = 0;
            int index = 0;
            while (index < source.Length)
            {
                if (source[index] != '[' || !codeTextMask.IsCodeAt(index))
                {
                    builder.Append(source[index]);
                    index++;
                    continue;
                }

                int closingBracketIndex = FindAttributeListClosingBracketIndex(
                    source,
                    codeTextMask,
                    index + 1);
                if (closingBracketIndex < 0)
                {
                    builder.Append(source[index]);
                    index++;
                    continue;
                }

                string attributesSource = source.Substring(index + 1, closingBracketIndex - index - 1);
                if (!TryMigrateLegacyToolAttributeList(
                        attributesSource,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyToolAttribute,
                        out string migratedAttributes))
                {
                    builder.Append(source, index, closingBracketIndex - index + 1);
                    index = closingBracketIndex + 1;
                    continue;
                }

                builder.Append('[');
                builder.Append(migratedAttributes);
                builder.Append(']');
                localReplacementCount++;
                index = closingBracketIndex + 1;
            }

            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        private static int FindAttributeListClosingBracketIndex(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int nestedBracketDepth = 0;
            for (int i = startIndex; i < source.Length; i++)
            {
                if (!codeTextMask.IsCodeAt(i))
                {
                    continue;
                }

                if (source[i] == '[')
                {
                    nestedBracketDepth++;
                    continue;
                }

                if (source[i] != ']')
                {
                    continue;
                }

                if (nestedBracketDepth == 0)
                {
                    return i;
                }

                nestedBracketDepth--;
            }

            return -1;
        }

        private static bool RegexMatchesCode(string source, Regex regex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");

            MatchCollection matches = regex.Matches(source);
            if (matches.Count == 0)
            {
                return false;
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyAssemblyScopedTypeReference(string source)
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

        private static bool ContainsLegacyAliasQualifiedAssemblyScopedApi(
            string source,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            foreach (string alias in legacyAssemblyAliases)
            {
                foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
                {
                    if (ContainsLegacyAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
                {
                    if (ContainsLegacyAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                if (ContainsLegacyAliasQualifiedName(source, alias, "ToolInfo") ||
                    ContainsLegacyAliasQualifiedName(source, alias, "CustomToolManager"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyEditorDelayFrameCall(
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

        private static bool ContainsLegacyEditorWindowCaptureUtilityCall(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureWindowRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyAliasQualifiedName(string source, string alias, string typeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(alias), "alias must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");

            Regex aliasQualifiedRegex = new(
                $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(typeName)}\b",
                RegexOptions.Compiled);
            return RegexMatchesCode(source, aliasQualifiedRegex);
        }

        private static bool ContainsLegacyRegistrarReference(
            string source,
            bool canMigrateBareLegacyRegistrar,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            if (RegexMatchesCode(source, LegacyQualifiedRegistrarRegex))
            {
                return true;
            }

            foreach (string alias in aliases)
            {
                if (ContainsLegacyAliasQualifiedName(source, alias, "CustomToolManager"))
                {
                    return true;
                }
            }

            return canMigrateBareLegacyRegistrar && ContainsMigratableUnqualifiedLegacyRegistrarReference(source);
        }

        private static bool ContainsLegacyRegistrarDomainReturnReference(
            string source,
            bool canMigrateBareLegacyRegistrar,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            if (RegexMatchesCode(source, LegacyQualifiedRegistrarDomainReturnRegex))
            {
                return true;
            }

            foreach (string alias in aliases)
            {
                if (ContainsLegacyAliasQualifiedRegistrarDomainReturn(source, alias))
                {
                    return true;
                }
            }

            return canMigrateBareLegacyRegistrar &&
                ContainsMigratableUnqualifiedLegacyRegistrarDomainReturn(source);
        }

        private static bool ContainsLegacyAliasQualifiedRegistrarDomainReturn(string source, string alias)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(alias), "alias must not be null or empty");

            Regex aliasQualifiedRegex = new(
                $@"(?<!\w){Regex.Escape(alias)}\.CustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(",
                RegexOptions.Compiled);
            return RegexMatchesCode(source, aliasQualifiedRegex);
        }

        private static bool ContainsMigratableUnqualifiedLegacyRegistrarReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyRegistrarRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMigratableUnqualifiedLegacyRegistrarDomainReturn(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyRegistrarDomainReturnRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyDomainHelperReference(
            string source,
            bool canMigrateBareLegacyDomainHelper,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                foreach (string alias in aliases)
                {
                    if (ContainsLegacyAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                if (canMigrateBareLegacyDomainHelper &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsCurrentDomainHelperApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            return ContainsCurrentDomainHelperApiForAssembly(source, hasCurrentDomainNamespaceUsage);
        }

        private static bool ContainsCurrentDomainHelperApiForAssembly(
            string source,
            bool canUseBareCurrentDomainType)
        {
            Debug.Assert(source != null, "source must not be null");

            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (canUseBareCurrentDomainType && RegexMatchesCode(source, unqualifiedRegex))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLegacyAssemblyScopedTypeName(
            string source,
            CodeTextMask codeTextMask,
            string typeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");

            Regex typeNameRegex = new($@"(?<![\.:])\b{Regex.Escape(typeName)}\b", RegexOptions.Compiled);
            MatchCollection matches = typeNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                if (IsLegacyAssemblyScopedTypeDeclaration(source, match.Index))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsLegacyAssemblyScopedTypeDeclaration(string source, int typeNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(typeNameIndex >= 0, "typeNameIndex must not be negative");

            string previousIdentifier = ReadPreviousIdentifier(source, typeNameIndex);
            return string.Equals(previousIdentifier, "class", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "struct", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "interface", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "enum", StringComparison.Ordinal) ||
                string.Equals(previousIdentifier, "using", StringComparison.Ordinal);
        }

        private static string ReadPreviousIdentifier(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = startIndex - 1;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            int identifierEndIndex = index + 1;
            while (index >= 0 && IsIdentifierCharacter(source[index]))
            {
                index--;
            }

            return source.Substring(index + 1, identifierEndIndex - index - 1);
        }

        private static char ReadNextNonWhitespaceCharacter(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex; index < source.Length; index++)
            {
                if (char.IsWhiteSpace(source[index]))
                {
                    continue;
                }

                return source[index];
            }

            return '\0';
        }

        private static char ReadPreviousNonWhitespaceCharacter(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            for (int index = startIndex - 1; index >= 0; index--)
            {
                if (char.IsWhiteSpace(source[index]))
                {
                    continue;
                }

                return source[index];
            }

            return '\0';
        }

        private static bool IsDeclarationIdentifierTerminator(char value)
        {
            return value == '{' ||
                value == ';' ||
                value == '=' ||
                value == ')' ||
                value == ',';
        }

        private static bool PreviousCodeTokenEquals(string source, int endIndex, string token)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(endIndex >= 0, "endIndex must not be negative");
            Debug.Assert(!string.IsNullOrEmpty(token), "token must not be null or empty");

            int tokenEndIndex = endIndex - 1;
            while (tokenEndIndex >= 0 && char.IsWhiteSpace(source[tokenEndIndex]))
            {
                tokenEndIndex--;
            }

            int tokenStartIndex = tokenEndIndex;
            while (tokenStartIndex >= 0 && IsIdentifierCharacter(source[tokenStartIndex]))
            {
                tokenStartIndex--;
            }

            int tokenLength = tokenEndIndex - tokenStartIndex;
            if (tokenLength <= 0)
            {
                return false;
            }

            string previousToken = source.Substring(tokenStartIndex + 1, tokenLength);
            return string.Equals(previousToken, token, StringComparison.Ordinal);
        }

        private static bool CanPrecedeDeclarationIdentifier(char value)
        {
            return IsIdentifierCharacter(value) ||
                value == ']' ||
                value == '>';
        }

        private static bool IsIdentifierCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static bool ContainsLegacyToolMigrationMarker(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (RegexMatchesCode(source, LegacyNamespaceRegex)) return true;

            return ContainsLegacyToolAttributeList(
                source,
                Array.Empty<string>(),
                canMigrateBareLegacyToolAttribute: false);
        }

        private static bool ContainsLegacyToolAttributeList(
            string source,
            string[] legacyAssemblyAliases,
            bool canMigrateBareLegacyToolAttribute)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            int index = 0;
            while (index < source.Length)
            {
                if (source[index] != '[' || !codeTextMask.IsCodeAt(index))
                {
                    index++;
                    continue;
                }

                int closingBracketIndex = FindAttributeListClosingBracketIndex(
                    source,
                    codeTextMask,
                    index + 1);
                if (closingBracketIndex < 0)
                {
                    index++;
                    continue;
                }

                string attributesSource = source.Substring(index + 1, closingBracketIndex - index - 1);
                if (TryMigrateLegacyToolAttributeList(
                        attributesSource,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyToolAttribute,
                        out _))
                {
                    return true;
                }

                index = closingBracketIndex + 1;
            }

            return false;
        }

        private static bool StartsWith(string source, int startIndex, string value)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(value != null, "value must not be null");

            if (startIndex + value.Length > source.Length)
            {
                return false;
            }

            return string.CompareOrdinal(source, startIndex, value, 0, value.Length) == 0;
        }

        private static bool ContainsTextFragment(string source, string text)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(text), "text must not be null or empty");

            return source.IndexOf(text, StringComparison.Ordinal) >= 0;
        }

        private static bool IsRawStringStart(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
            int quoteIndex = startIndex + dollarCount;
            return CountRepeatedCharacter(source, quoteIndex, '"') >= MinimumRawStringDelimiterQuoteCount;
        }

        private static int CountRepeatedCharacter(string source, int startIndex, char character)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int index = startIndex;
            while (index < source.Length && source[index] == character)
            {
                index++;
            }

            return index - startIndex;
        }

        private static bool HasRepeatedCharacterAt(string source, int startIndex, char character, int count)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(count > 0, "count must be positive");

            if (startIndex + count > source.Length)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (source[startIndex + i] != character)
                {
                    return false;
                }
            }

            return true;
        }

        private static int GetStringPrefixLength(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return 2;
            }

            return 1;
        }

        private readonly struct ReplacementRule
        {
            public ReplacementRule(string pattern, string replacement)
            {
                Debug.Assert(!string.IsNullOrEmpty(pattern), "pattern must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(replacement), "replacement must not be null or empty");

                PatternRegex = new Regex(pattern, RegexOptions.Compiled);
                Replacement = replacement;
            }

            public Regex PatternRegex { get; }
            public string Replacement { get; }
        }

        private readonly struct TypeReplacementRule
        {
            public TypeReplacementRule(string legacyName, string currentName)
            {
                Debug.Assert(!string.IsNullOrEmpty(legacyName), "legacyName must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(currentName), "currentName must not be null or empty");

                LegacyName = legacyName;
                CurrentName = currentName;
            }

            public string LegacyName { get; }
            public string CurrentName { get; }
        }

        private readonly struct CodeTextMask
        {
            private readonly bool[] _codeCharacters;

            private CodeTextMask(bool[] codeCharacters)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");

                _codeCharacters = codeCharacters;
            }

            public static CodeTextMask Create(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                return CodeTextMaskCache.GetValue(source, CreateCodeTextMaskHolder).Mask;
            }

            internal static CodeTextMask CreateUncached(string source)
            {
                Debug.Assert(source != null, "source must not be null");

                bool[] codeCharacters = new bool[source.Length];
                for (int i = 0; i < codeCharacters.Length; i++)
                {
                    codeCharacters[i] = true;
                }

                int index = 0;
                while (index < source.Length)
                {
                    if (IsInterpolatedRawStringStart(source, index))
                    {
                        index = MarkInterpolatedRawStringAsIgnored(codeCharacters, source, index);
                        continue;
                    }

                    if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
                    {
                        index = MarkInterpolatedVerbatimStringAsIgnored(codeCharacters, source, index);
                        continue;
                    }

                    if (StartsWith(source, index, "$\"") && !StartsWith(source, index, "$\"\"\""))
                    {
                        index = MarkInterpolatedRegularStringAsIgnored(codeCharacters, source, index);
                        continue;
                    }

                    int ignoredTextEndIndex = GetIgnoredTextEndIndex(source, index);
                    if (ignoredTextEndIndex == index)
                    {
                        index++;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, index, ignoredTextEndIndex);
                    index = ignoredTextEndIndex;
                }

                return new CodeTextMask(codeCharacters);
            }

            public bool IsCodeAt(int index)
            {
                if (index < 0 || index >= _codeCharacters.Length)
                {
                    return false;
                }

                return _codeCharacters[index];
            }

            private static int GetIgnoredTextEndIndex(string source, int startIndex)
            {
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");

                if (StartsWith(source, startIndex, "//"))
                {
                    return FindLineCommentEndIndex(source, startIndex);
                }

                if (StartsWith(source, startIndex, "/*"))
                {
                    return FindBlockCommentEndIndex(source, startIndex);
                }

                if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
                {
                    return FindVerbatimStringEndIndex(source, startIndex + 2);
                }

                if (StartsWith(source, startIndex, "@\""))
                {
                    return FindVerbatimStringEndIndex(source, startIndex + 1);
                }

                if (IsInterpolatedRawStringStart(source, startIndex))
                {
                    int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
                    return FindRawStringEndIndex(source, startIndex + dollarCount);
                }

                if (StartsWith(source, startIndex, "$\""))
                {
                    return FindRegularStringEndIndex(source, startIndex + 1);
                }

                if (StartsWith(source, startIndex, "\"\"\""))
                {
                    return FindRawStringEndIndex(source, startIndex);
                }

                if (source[startIndex] == '"')
                {
                    return FindRegularStringEndIndex(source, startIndex);
                }

                if (source[startIndex] == '\'')
                {
                    return FindCharLiteralEndIndex(source, startIndex);
                }

                return startIndex;
            }

            private static int FindLineCommentEndIndex(string source, int startIndex)
            {
                int index = startIndex;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                return index;
            }

            private static int FindBlockCommentEndIndex(string source, int startIndex)
            {
                int index = startIndex + 2;
                while (index + 1 < source.Length)
                {
                    if (source[index] == '*' && source[index + 1] == '/')
                    {
                        return index + 2;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int FindRegularStringEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '"')
                    {
                        return index + 1;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int FindVerbatimStringEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] != '"')
                    {
                        index++;
                        continue;
                    }

                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                return source.Length;
            }

            private static int FindRawStringEndIndex(string source, int quoteIndex)
            {
                int quoteCount = CountRepeatedCharacter(source, quoteIndex, '"');
                Debug.Assert(
                    quoteCount >= MinimumRawStringDelimiterQuoteCount,
                    "quoteIndex must point to a raw string delimiter");

                int index = quoteIndex + quoteCount;
                while (index + quoteCount <= source.Length)
                {
                    if (HasRepeatedCharacterAt(source, index, '"', quoteCount))
                    {
                        return index + quoteCount;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int MarkInterpolatedRawStringAsIgnored(
                bool[] codeCharacters,
                string source,
                int dollarIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(dollarIndex >= 0, "dollarIndex must not be negative");

                int dollarCount = CountRepeatedCharacter(source, dollarIndex, '$');
                int quoteIndex = dollarIndex + dollarCount;
                int quoteCount = CountRepeatedCharacter(source, quoteIndex, '"');
                Debug.Assert(dollarCount > 0, "dollarIndex must point to an interpolated raw string prefix");
                Debug.Assert(
                    quoteCount >= MinimumRawStringDelimiterQuoteCount,
                    "quoteIndex must point to a raw string delimiter");

                int literalStartIndex = dollarIndex;
                int index = quoteIndex + quoteCount;
                while (index < source.Length)
                {
                    if (HasRepeatedCharacterAt(source, index, '"', quoteCount))
                    {
                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + quoteCount);
                        return index + quoteCount;
                    }

                    if (source[index] == '{')
                    {
                        int braceCount = CountRepeatedCharacter(source, index, '{');
                        if (braceCount < dollarCount)
                        {
                            index += braceCount;
                            continue;
                        }

                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                        index = MarkRawInterpolationHoleNestedTextAsIgnored(
                            codeCharacters,
                            source,
                            index,
                            dollarCount);
                        literalStartIndex = index;
                        continue;
                    }

                    if (source[index] == '}')
                    {
                        int braceCount = CountRepeatedCharacter(source, index, '}');
                        index += braceCount;
                        continue;
                    }

                    index++;
                }

                MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
                return source.Length;
            }

            private static int MarkInterpolatedRegularStringAsIgnored(
                bool[] codeCharacters,
                string source,
                int dollarIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(dollarIndex >= 0, "dollarIndex must not be negative");

                int quoteIndex = dollarIndex + 1;
                int literalStartIndex = dollarIndex;
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '{')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '{')
                        {
                            index += 2;
                            continue;
                        }

                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                        index = MarkInterpolationHoleNestedTextAsIgnored(codeCharacters, source, index);
                        literalStartIndex = index;
                        continue;
                    }

                    if (source[index] == '"')
                    {
                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + 1);
                        return index + 1;
                    }

                    index++;
                }

                MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
                return source.Length;
            }

            private static int MarkInterpolatedVerbatimStringAsIgnored(
                bool[] codeCharacters,
                string source,
                int prefixIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(prefixIndex >= 0, "prefixIndex must not be negative");

                int quoteIndex = prefixIndex + 2;
                int literalStartIndex = prefixIndex;
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '"')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '"')
                        {
                            index += 2;
                            continue;
                        }

                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + 1);
                        return index + 1;
                    }

                    if (source[index] == '{')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '{')
                        {
                            index += 2;
                            continue;
                        }

                        MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                        index = MarkInterpolationHoleNestedTextAsIgnored(codeCharacters, source, index);
                        literalStartIndex = index;
                        continue;
                    }

                    if (source[index] == '}' && index + 1 < source.Length && source[index + 1] == '}')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                }

                MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
                return source.Length;
            }

            private static int MarkInterpolationHoleNestedTextAsIgnored(
                bool[] codeCharacters,
                string source,
                int openBraceIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(openBraceIndex >= 0, "openBraceIndex must not be negative");

                int nestedBraceDepth = 0;
                int index = openBraceIndex + 1;
                while (index < source.Length)
                {
                    int ignoredTextEndIndex = MarkIgnoredTextInInterpolationHole(
                        codeCharacters,
                        source,
                        index);
                    if (ignoredTextEndIndex != index)
                    {
                        index = ignoredTextEndIndex;
                        continue;
                    }

                    if (source[index] == '{')
                    {
                        nestedBraceDepth++;
                        index++;
                        continue;
                    }

                    if (source[index] == '}')
                    {
                        if (nestedBraceDepth == 0)
                        {
                            return index + 1;
                        }

                        nestedBraceDepth--;
                    }

                    index++;
                }

                return source.Length;
            }

            private static int MarkRawInterpolationHoleNestedTextAsIgnored(
                bool[] codeCharacters,
                string source,
                int openBraceIndex,
                int interpolationBraceCount)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(openBraceIndex >= 0, "openBraceIndex must not be negative");
                Debug.Assert(interpolationBraceCount > 0, "interpolationBraceCount must be positive");

                int nestedBraceDepth = 0;
                int index = openBraceIndex + interpolationBraceCount;
                while (index < source.Length)
                {
                    int ignoredTextEndIndex = MarkIgnoredTextInInterpolationHole(
                        codeCharacters,
                        source,
                        index);
                    if (ignoredTextEndIndex != index)
                    {
                        index = ignoredTextEndIndex;
                        continue;
                    }

                    if (source[index] == '{')
                    {
                        nestedBraceDepth++;
                        index++;
                        continue;
                    }

                    if (source[index] == '}')
                    {
                        bool isClosingInterpolation =
                            nestedBraceDepth == 0 &&
                            HasRepeatedCharacterAt(source, index, '}', interpolationBraceCount);
                        if (isClosingInterpolation)
                        {
                            return index + interpolationBraceCount;
                        }

                        if (nestedBraceDepth > 0)
                        {
                            nestedBraceDepth--;
                        }
                    }

                    index++;
                }

                return source.Length;
            }

            private static int MarkIgnoredTextInInterpolationHole(
                bool[] codeCharacters,
                string source,
                int startIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");

                if (IsInterpolatedRawStringStart(source, startIndex))
                {
                    return MarkInterpolatedRawStringAsIgnored(codeCharacters, source, startIndex);
                }

                if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
                {
                    return MarkInterpolatedVerbatimStringAsIgnored(codeCharacters, source, startIndex);
                }

                if (StartsWith(source, startIndex, "$\"") && !StartsWith(source, startIndex, "$\"\"\""))
                {
                    return MarkInterpolatedRegularStringAsIgnored(codeCharacters, source, startIndex);
                }

                int ignoredTextEndIndex = GetIgnoredTextEndIndex(source, startIndex);
                if (ignoredTextEndIndex == startIndex)
                {
                    return startIndex;
                }

                MarkRangeAsIgnored(codeCharacters, startIndex, ignoredTextEndIndex);
                return ignoredTextEndIndex;
            }

            private static int FindCharLiteralEndIndex(string source, int quoteIndex)
            {
                int index = quoteIndex + 1;
                while (index < source.Length)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '\'')
                    {
                        return index + 1;
                    }

                    index++;
                }

                return source.Length;
            }

            private static void MarkRangeAsIgnored(bool[] codeCharacters, int startIndex, int endIndex)
            {
                Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");
                Debug.Assert(endIndex >= startIndex, "endIndex must not be less than startIndex");

                for (int i = startIndex; i < endIndex && i < codeCharacters.Length; i++)
                {
                    codeCharacters[i] = false;
                }
            }

            private static bool IsInterpolatedRawStringStart(string source, int startIndex)
            {
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");

                if (startIndex >= source.Length || source[startIndex] != '$')
                {
                    return false;
                }

                int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
                int quoteIndex = startIndex + dollarCount;
                return CountRepeatedCharacter(source, quoteIndex, '"') >= MinimumRawStringDelimiterQuoteCount;
            }

            private static int CountRepeatedCharacter(string source, int startIndex, char character)
            {
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");

                int index = startIndex;
                while (index < source.Length && source[index] == character)
                {
                    index++;
                }

                return index - startIndex;
            }

            private static bool HasRepeatedCharacterAt(string source, int startIndex, char character, int count)
            {
                Debug.Assert(source != null, "source must not be null");
                Debug.Assert(startIndex >= 0, "startIndex must not be negative");
                Debug.Assert(count > 0, "count must be positive");

                if (startIndex + count > source.Length)
                {
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    if (source[startIndex + i] != character)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class CodeTextMaskHolder
        {
            public CodeTextMaskHolder(CodeTextMask mask)
            {
                Mask = mask;
            }

            public CodeTextMask Mask { get; }
        }

        private static CodeTextMaskHolder CreateCodeTextMaskHolder(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return new CodeTextMaskHolder(CodeTextMask.CreateUncached(source));
        }
    }

    internal readonly struct ThirdPartyToolMigrationContentResult
    {
        public ThirdPartyToolMigrationContentResult(string content, int replacementCount)
        {
            Debug.Assert(content != null, "content must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

            Content = content ?? string.Empty;
            ReplacementCount = replacementCount;
        }

        public string Content { get; }
        public int ReplacementCount { get; }
        public bool Changed => ReplacementCount > 0;
    }
}
