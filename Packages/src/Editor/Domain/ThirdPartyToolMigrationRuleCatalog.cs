using System.Text.RegularExpressions;

using ReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the shared V2-to-V3 migration names, patterns, and replacement tables.
    /// </summary>
    public static class ThirdPartyToolMigrationRuleCatalog
    {
        public const string LegacyNamespace = "io.github.hatayama.uLoopMCP";
        public const string CurrentNamespace = "io.github.hatayama.UnityCliLoop.ToolContracts";
        public const string CurrentApplicationNamespace = "io.github.hatayama.UnityCliLoop.Application";
        public const string CurrentDomainNamespace = "io.github.hatayama.UnityCliLoop.Domain";
        public const string CurrentFirstPartyToolsNamespace = "io.github.hatayama.UnityCliLoop.FirstPartyTools";
        public const string LegacyEditorAssemblyName = "uLoopMCP.Editor";
        public const string LegacyRuntimeAssemblyName = "uLoopMCP.Runtime";
        public const string CurrentApplicationAssemblyName = "UnityCLILoop.Application";
        public const string CurrentDomainAssemblyName = "UnityCLILoop.Domain";
        public const string CurrentFirstPartyToolsScreenshotAssemblyName =
            "UnityCLILoop.FirstPartyTools.Screenshot.Editor";
        public const string CurrentRuntimeAssemblyName = "UnityCLILoop.Runtime";
        public const string CurrentToolContractsAssemblyName = "UnityCLILoop.ToolContracts";
        public const string LegacyEditorAssemblyGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        public const string CurrentApplicationGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        public const string CurrentDomainGuidReference = "GUID:5c4588558a3624eacbce0f50007cf1eb";
        public const string CurrentFirstPartyToolsScreenshotGuidReference =
            "GUID:a0bdbd2c5705643fbb9aef9fac8fd46a";
        public const string CurrentRuntimeGuidReference = "GUID:c956a21f824994ef087b6de566690b3d";
        public const string CurrentToolContractsGuidReference = "GUID:fc3fd32eddbee40e39c2d76dc184957b";
        public const string DescriptionAttributeArgumentName = "Description";
        public const string DisplayDevelopmentOnlyAttributeArgumentName = "DisplayDevelopmentOnly";
        public const string RequiredSecuritySettingAttributeArgumentName = "RequiredSecuritySetting";
        public const string LegacySecuritySettingsTypeName = "SecuritySettings";
        public const string CurrentSecuritySettingTypeName = "UnityCliLoopSecuritySetting";
        public const string LegacyEditorDelayTypeName = "EditorDelay";
        public const string LegacyEditorDelayMethodName = "DelayFrame";
        public const string LegacyTimerDelayTypeName = "TimerDelay";
        public const string LegacyCancellationTokenArgumentName = "cancellationToken";
        public const string CurrentCancellationTokenArgumentName = "ct";
        public const string LegacyPlayerLoopTimingTypeName = "PlayerLoopTiming";
        public const string LegacyTimingArgumentName = "timing";
        public const string LegacyMainThreadSwitcherTypeName = "MainThreadSwitcher";
        public const string LegacyMainThreadSwitcherSwitchMethodName = "SwitchToMainThread";
        public const string CurrentEditorFrameWaiterTypeName = "EditorFrameWaiter";
        public const string CurrentEditorFrameWaiterMethodName = "WaitFramesOrTimeoutAsync";
        public const string LegacyEditorWindowCaptureUtilityTypeName = "EditorWindowCaptureUtility";
        public const string EditorWindowCaptureUtilityCaptureWindowMethodName = "CaptureWindowAsync";
        public const string EditorWindowCaptureUtilityCaptureGameRenderingMethodName =
            "CaptureGameRenderingAsync";
        public const string EditorWindowCaptureUtilityWindowArgumentName = "window";
        public const string EditorWindowCaptureUtilityResolutionScaleArgumentName = "resolutionScale";
        public const string CaptureGameRenderingProjectionTaskVariableName = "__unityCliLoopRenderingTask";
        public const string CurrentConstantsTypeName = "UnityCliLoopConstants";
        public const string CurrentEditorFrameWaitTimeoutMemberName = "EDITOR_FRAME_WAIT_TIMEOUT_MS";
        public const int MinimumRawStringDelimiterQuoteCount = 3;
        public static readonly string[] ExcludedDirectoryNames =
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

        public static readonly string LegacyNamespacePattern =
            $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}(?=\.|;|\s|$)";

        public static readonly Regex LegacyNamespaceRegex =
            new(LegacyNamespacePattern, RegexOptions.Compiled);

        public static readonly Regex CurrentDomainNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentDomainNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        public static readonly Regex CurrentToolContractsNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        public static readonly Regex CurrentApplicationNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        public static readonly Regex CurrentFirstPartyToolsNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        public static readonly Regex CurrentDomainMetadataRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentNamespace)}\.ToolInfo\b",
                RegexOptions.Compiled);

        public static readonly Regex LegacyGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyNamespaceUsingRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentDomainGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentDomainUsingRegex =
            new(
                $@"(?<!global\s)\busing\s+(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentDomainNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentDomainGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentToolContractsGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentApplicationGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentApplicationUsingRegex =
            new(
                $@"(?<!global\s)\busing\s+(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentApplicationNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentApplicationGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentToolContractsUsingRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?:global::)?{Regex.Escape(CurrentNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentFirstPartyToolsGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentFirstPartyToolsUsingRegex =
            new(
                $@"(?<!global\s)\busing\s+(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentFirstPartyToolsNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex CurrentFirstPartyToolsGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyRegistrarRegex =
            new(@"\bCustomToolManager\b", RegexOptions.Compiled);

        public static readonly Regex LegacyQualifiedRegistrarRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                RegexOptions.Compiled);

        public static readonly Regex CurrentRegistrarRegex =
            new(@"\bUnityCliLoopToolRegistrar\b", RegexOptions.Compiled);

        public static readonly Regex LegacyQualifiedRegistrarDomainReturnRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyRegistrarDomainReturnRegex =
            new(@"\bCustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        public static readonly Regex CurrentRegistrarDomainReturnRegex =
            new(@"\bUnityCliLoopToolRegistrar\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        public static readonly Regex LegacyDomainMetadataRegex =
            new(@"\bToolInfo\b", RegexOptions.Compiled);

        public static readonly Regex LegacyBaseTypeUsageRegex =
            new(
                @":\s*[^;{}=]*\b(?:AbstractUnityTool|BaseToolSchema|BaseToolResponse)\b",
                RegexOptions.Compiled);

        public static readonly Regex LegacyAssemblyScopedApiUsageRegex =
            new(
                @"\b(?:IUnityTool\s+[A-Za-z_][A-Za-z0-9_]*|" +
                @"ToolParameterSchemaGenerator\s*\.|" +
                @"new\s+ParameterValidationException\b|" +
                @"CustomToolManager\s*\.|" +
                @"ToolInfo\s*(?:\[\])?\s+[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);

        public static readonly Regex LegacyNamespaceAliasRegex =
            new(
                @"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?" +
                @"io\.github\.hatayama\.uLoopMCP\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyToolAttributeEntryRegex =
            new(
                @"^\s*(?:(?<qualifier>(?:global::)?io\.github\.hatayama\.uLoopMCP\.)|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.)?" +
                @"McpTool(?:Attribute)?\s*(?:\((?<arguments>[\s\S]*)\))?\s*$",
                RegexOptions.Compiled);

        public static readonly Regex LegacyToolInfoConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolInfo|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolInfo|(?<toolInfo>ToolInfo)|(?<typeAlias>[A-Za-z_][A-Za-z0-9_]*))\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyToolInfoTypeAliasRegex =
            new(
                $@"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyGlobalToolInfoTypeAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        public static readonly Regex LegacyToolSettingsCatalogItemConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolSettingsCatalogItem|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolSettingsCatalogItem|(?<toolSettingsCatalogItem>ToolSettingsCatalogItem))\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyEditorDelayFrameRegex =
            new(
                $@"(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorDelayTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorDelayTypeName}|(?<editorDelay>{LegacyEditorDelayTypeName}))\s*\.\s*{LegacyEditorDelayMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyEditorWindowCaptureUtilityCaptureWindowRegex =
            new(
                $@"(?<![\w.])await\s+(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureWindowMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyEditorWindowCaptureUtilityCaptureWindowInvocationRegex =
            new(
                $@"(?<![\w.])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureWindowMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyEditorWindowCaptureUtilityCaptureGameRenderingRegex =
            new(
                $@"(?<![\w.])await\s+(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyEditorWindowCaptureUtilityCaptureGameRenderingInvocationRegex =
            new(
                $@"(?<![\w.])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyTimerDelayInvocationRegex =
            new(
                $@"(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyTimerDelayTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyTimerDelayTypeName}|(?<timerDelay>{LegacyTimerDelayTypeName}))\s*\.\s*(?<method>Wait|WaitThenExecuteOnMainThread)\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex LegacyMainThreadSwitcherSwitchRegex =
            new(
                $@"(?<![A-Za-z0-9_])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentApplicationNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyMainThreadSwitcherTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyMainThreadSwitcherTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyMainThreadSwitcherTypeName}|(?<mainThreadSwitcher>{LegacyMainThreadSwitcherTypeName}))\s*\.\s*{LegacyMainThreadSwitcherSwitchMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex MigratedMainThreadSwitcherSwitchRegex =
            new(
                $@"(?<![\w.])(?:(?:global::)?{Regex.Escape(CurrentNamespace)}\.)?{LegacyMainThreadSwitcherTypeName}\s*\.\s*{LegacyMainThreadSwitcherSwitchMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex CurrentCaptureGameRenderingDeconstructionRegex =
            new(
                $@"\((?<items>[^()]*)\)\s*=\s*await\s+(?:(?<qualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.)|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.)?{LegacyEditorWindowCaptureUtilityTypeName}\.{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        public static readonly Regex TypeDeclarationNameRegex =
            new(
                @"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        public static readonly Regex InterfaceDeclarationNameRegex =
            new(
                @"\binterface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        public static readonly Regex LegacyPlayerLoopTimingDeclarationRegex =
            new(
                $@"(?m)^[ \t]*(?:(?:private|protected|internal|static|readonly)\s+)*{LegacyPlayerLoopTimingTypeName}\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*[^;]+)?;\s*(?:\r?\n)?",
                RegexOptions.Compiled);

        public static readonly Regex NamespaceDeclarationRegex =
            new(
                @"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?<terminator>[{;])",
                RegexOptions.Compiled);

        public static readonly TypeReplacementRule[] ToolContractTypeReplacementRules =
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

        public static readonly TypeReplacementRule[] DomainTypeReplacementRules =
        {
            new("ServiceResult", "ServiceResult"),
            new("ToolSettingsCatalogItem", "ToolSettingsCatalogItem")
        };

        public static readonly TypeReplacementRule[] ApplicationTypeReplacementRules =
        {
            new("MainThreadSwitcher", "MainThreadSwitcher"),
            new("SwitchToMainThreadAwaitable", "SwitchToMainThreadAwaitable")
        };

        public static readonly TypeReplacementRule[] FirstPartyScreenshotTypeReplacementRules =
        {
            new("EditorWindowCaptureUtility", "EditorWindowCaptureUtility"),
            new("WindowMatchMode", "WindowMatchMode"),
            new("CaptureMode", "CaptureMode"),
            new("ScreenshotSchema", "ScreenshotSchema"),
            new("ScreenshotResponse", "ScreenshotResponse"),
            new("ScreenshotInfo", "ScreenshotInfo"),
            new("UIElementInfo", "UIElementInfo")
        };

        public static readonly ReplacementRule[] CSharpReplacementRules =
        {
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                $"{CurrentNamespace}.UnityCliLoopToolRegistrar"),
            new(LegacyNamespacePattern, CurrentNamespace)
        };

        public static readonly ReplacementRule[] RegistrarReplacementRules =
        {
            new(Regex.Escape($"{CurrentDomainNamespace}.ToolInfo"), $"{CurrentNamespace}.ToolInfo"),
            new(
                Regex.Escape($"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar"),
                $"{CurrentNamespace}.UnityCliLoopToolRegistrar")
        };

        public static readonly Regex UnqualifiedToolInfoRegex =
            new(@"(?<![\.:])\bToolInfo\b(?!\s*=)", RegexOptions.Compiled);
    }
}
