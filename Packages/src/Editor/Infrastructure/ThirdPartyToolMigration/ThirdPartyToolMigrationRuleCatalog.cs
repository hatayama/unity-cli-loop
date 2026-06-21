using System.Text.RegularExpressions;

using ReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Defines the shared V2-to-V3 migration names, patterns, and replacement tables.
    /// </summary>
    internal static class ThirdPartyToolMigrationRuleCatalog
    {
        internal const string LegacyNamespace = "io.github.hatayama.uLoopMCP";
        internal const string CurrentNamespace = "io.github.hatayama.UnityCliLoop.ToolContracts";
        internal const string CurrentApplicationNamespace = "io.github.hatayama.UnityCliLoop.Application";
        internal const string CurrentDomainNamespace = "io.github.hatayama.UnityCliLoop.Domain";
        internal const string CurrentFirstPartyToolsNamespace = "io.github.hatayama.UnityCliLoop.FirstPartyTools";
        internal const string LegacyEditorAssemblyName = "uLoopMCP.Editor";
        internal const string LegacyRuntimeAssemblyName = "uLoopMCP.Runtime";
        internal const string CurrentApplicationAssemblyName = "UnityCLILoop.Application";
        internal const string CurrentDomainAssemblyName = "UnityCLILoop.Domain";
        internal const string CurrentFirstPartyToolsScreenshotAssemblyName =
            "UnityCLILoop.FirstPartyTools.Screenshot.Editor";
        internal const string CurrentRuntimeAssemblyName = "UnityCLILoop.Runtime";
        internal const string CurrentToolContractsAssemblyName = "UnityCLILoop.ToolContracts";
        internal const string LegacyEditorAssemblyGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentApplicationGuidReference = "GUID:214998e563c124e8a88199b2dd1f522d";
        internal const string CurrentDomainGuidReference = "GUID:5c4588558a3624eacbce0f50007cf1eb";
        internal const string CurrentFirstPartyToolsScreenshotGuidReference =
            "GUID:a0bdbd2c5705643fbb9aef9fac8fd46a";
        internal const string CurrentRuntimeGuidReference = "GUID:c956a21f824994ef087b6de566690b3d";
        internal const string CurrentToolContractsGuidReference = "GUID:fc3fd32eddbee40e39c2d76dc184957b";
        internal const string DescriptionAttributeArgumentName = "Description";
        internal const string DisplayDevelopmentOnlyAttributeArgumentName = "DisplayDevelopmentOnly";
        internal const string RequiredSecuritySettingAttributeArgumentName = "RequiredSecuritySetting";
        internal const string LegacySecuritySettingsTypeName = "SecuritySettings";
        internal const string CurrentSecuritySettingTypeName = "UnityCliLoopSecuritySetting";
        internal const string LegacyEditorDelayTypeName = "EditorDelay";
        internal const string LegacyEditorDelayMethodName = "DelayFrame";
        internal const string LegacyTimerDelayTypeName = "TimerDelay";
        internal const string LegacyCancellationTokenArgumentName = "cancellationToken";
        internal const string CurrentCancellationTokenArgumentName = "ct";
        internal const string LegacyPlayerLoopTimingTypeName = "PlayerLoopTiming";
        internal const string LegacyTimingArgumentName = "timing";
        internal const string LegacyMainThreadSwitcherTypeName = "MainThreadSwitcher";
        internal const string LegacyMainThreadSwitcherSwitchMethodName = "SwitchToMainThread";
        internal const string CurrentEditorFrameWaiterTypeName = "EditorFrameWaiter";
        internal const string CurrentEditorFrameWaiterMethodName = "WaitFramesOrTimeoutAsync";
        internal const string LegacyEditorWindowCaptureUtilityTypeName = "EditorWindowCaptureUtility";
        internal const string EditorWindowCaptureUtilityCaptureWindowMethodName = "CaptureWindowAsync";
        internal const string EditorWindowCaptureUtilityCaptureGameRenderingMethodName =
            "CaptureGameRenderingAsync";
        internal const string EditorWindowCaptureUtilityWindowArgumentName = "window";
        internal const string EditorWindowCaptureUtilityResolutionScaleArgumentName = "resolutionScale";
        internal const string CaptureGameRenderingProjectionTaskVariableName = "__unityCliLoopRenderingTask";
        internal const string CurrentConstantsTypeName = "UnityCliLoopConstants";
        internal const string CurrentEditorFrameWaitTimeoutMemberName = "EDITOR_FRAME_WAIT_TIMEOUT_MS";
        internal const int MinimumRawStringDelimiterQuoteCount = 3;
        internal static readonly string[] ExcludedDirectoryNames =
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

        internal static readonly string LegacyNamespacePattern =
            $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}(?=\.|;|\s|$)";

        internal static readonly Regex LegacyNamespaceRegex =
            new(LegacyNamespacePattern, RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentDomainNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentToolContractsNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentApplicationNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentFirstPartyToolsNamespaceRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}(?=\.|;|\s|$)",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainMetadataRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(CurrentNamespace)}\.ToolInfo\b",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyNamespaceUsingRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainUsingRegex =
            new(
                $@"(?<!global\s)\busing\s+(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentDomainGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentToolContractsGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentApplicationGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentApplicationUsingRegex =
            new(
                $@"(?<!global\s)\busing\s+(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentApplicationNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentApplicationGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentToolContractsUsingRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?:global::)?{Regex.Escape(CurrentNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentFirstPartyToolsGlobalUsingRegex =
            new(
                $@"\bglobal\s+using\s+(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentFirstPartyToolsNamespaceAliasRegex =
            new(
                $@"\b(?:global\s+)?using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentFirstPartyToolsGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyGlobalNamespaceAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyRegistrarRegex =
            new(@"\bCustomToolManager\b", RegexOptions.Compiled);

        internal static readonly Regex LegacyQualifiedRegistrarRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentRegistrarRegex =
            new(@"\bUnityCliLoopToolRegistrar\b", RegexOptions.Compiled);

        internal static readonly Regex LegacyQualifiedRegistrarDomainReturnRegex =
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyRegistrarDomainReturnRegex =
            new(@"\bCustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        internal static readonly Regex CurrentRegistrarDomainReturnRegex =
            new(@"\bUnityCliLoopToolRegistrar\s*\.\s*GetRegisteredCustomTools\s*\(", RegexOptions.Compiled);

        internal static readonly Regex LegacyDomainMetadataRegex =
            new(@"\bToolInfo\b", RegexOptions.Compiled);

        internal static readonly Regex LegacyBaseTypeUsageRegex =
            new(
                @":\s*[^;{}=]*\b(?:AbstractUnityTool|BaseToolSchema|BaseToolResponse)\b",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyAssemblyScopedApiUsageRegex =
            new(
                @"\b(?:IUnityTool\s+[A-Za-z_][A-Za-z0-9_]*|" +
                @"ToolParameterSchemaGenerator\s*\.|" +
                @"new\s+ParameterValidationException\b|" +
                @"CustomToolManager\s*\.|" +
                @"ToolInfo\s*(?:\[\])?\s+[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyNamespaceAliasRegex =
            new(
                @"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?" +
                @"io\.github\.hatayama\.uLoopMCP\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyToolAttributeEntryRegex =
            new(
                @"^\s*(?:(?<qualifier>(?:global::)?io\.github\.hatayama\.uLoopMCP\.)|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.)?" +
                @"McpTool(?:Attribute)?\s*(?:\((?<arguments>[\s\S]*)\))?\s*$",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyToolInfoConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolInfo|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolInfo|(?<toolInfo>ToolInfo)|(?<typeAlias>[A-Za-z_][A-Za-z0-9_]*))\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyToolInfoTypeAliasRegex =
            new(
                $@"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyGlobalToolInfoTypeAliasRegex =
            new(
                $@"\bglobal\s+using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?{Regex.Escape(LegacyNamespace)}\.ToolInfo\s*;",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyToolSettingsCatalogItemConstructorRegex =
            new(
                $@"new\s+(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.)ToolSettingsCatalogItem|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.ToolSettingsCatalogItem|(?<toolSettingsCatalogItem>ToolSettingsCatalogItem))\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyEditorDelayFrameRegex =
            new(
                $@"(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorDelayTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorDelayTypeName}|(?<editorDelay>{LegacyEditorDelayTypeName}))\s*\.\s*{LegacyEditorDelayMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyEditorWindowCaptureUtilityCaptureWindowRegex =
            new(
                $@"(?<![\w.])await\s+(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureWindowMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyEditorWindowCaptureUtilityCaptureWindowInvocationRegex =
            new(
                $@"(?<![\w.])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureWindowMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyEditorWindowCaptureUtilityCaptureGameRenderingRegex =
            new(
                $@"(?<![\w.])await\s+(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyEditorWindowCaptureUtilityCaptureGameRenderingInvocationRegex =
            new(
                $@"(?<![\w.])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyEditorWindowCaptureUtilityTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyEditorWindowCaptureUtilityTypeName}|(?<editorWindowCaptureUtility>{LegacyEditorWindowCaptureUtilityTypeName}))\s*\.\s*{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyTimerDelayInvocationRegex =
            new(
                $@"(?:(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyTimerDelayTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyTimerDelayTypeName}|(?<timerDelay>{LegacyTimerDelayTypeName}))\s*\.\s*(?<method>Wait|WaitThenExecuteOnMainThread)\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyMainThreadSwitcherSwitchRegex =
            new(
                $@"(?<![A-Za-z0-9_])(?:(?<currentQualifier>(?:global::)?(?:{Regex.Escape(CurrentApplicationNamespace)}|{Regex.Escape(CurrentNamespace)})\.){LegacyMainThreadSwitcherTypeName}|(?<qualifier>(?:global::)?{Regex.Escape(LegacyNamespace)}\.){LegacyMainThreadSwitcherTypeName}|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.{LegacyMainThreadSwitcherTypeName}|(?<mainThreadSwitcher>{LegacyMainThreadSwitcherTypeName}))\s*\.\s*{LegacyMainThreadSwitcherSwitchMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex MigratedMainThreadSwitcherSwitchRegex =
            new(
                $@"(?<![\w.])(?:(?:global::)?{Regex.Escape(CurrentNamespace)}\.)?{LegacyMainThreadSwitcherTypeName}\s*\.\s*{LegacyMainThreadSwitcherSwitchMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex CurrentCaptureGameRenderingDeconstructionRegex =
            new(
                $@"\((?<items>[^()]*)\)\s*=\s*await\s+(?:(?<qualifier>(?:global::)?(?:{Regex.Escape(CurrentFirstPartyToolsNamespace)}|{Regex.Escape(CurrentNamespace)})\.)|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.)?{LegacyEditorWindowCaptureUtilityTypeName}\.{EditorWindowCaptureUtilityCaptureGameRenderingMethodName}\s*\(",
                RegexOptions.Compiled);

        internal static readonly Regex TypeDeclarationNameRegex =
            new(
                @"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        internal static readonly Regex InterfaceDeclarationNameRegex =
            new(
                @"\binterface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
                RegexOptions.Compiled);

        internal static readonly Regex LegacyPlayerLoopTimingDeclarationRegex =
            new(
                $@"(?m)^[ \t]*(?:(?:private|protected|internal|static|readonly)\s+)*{LegacyPlayerLoopTimingTypeName}\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*[^;]+)?;\s*(?:\r?\n)?",
                RegexOptions.Compiled);

        internal static readonly Regex NamespaceDeclarationRegex =
            new(
                @"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?<terminator>[{;])",
                RegexOptions.Compiled);

        internal static readonly TypeReplacementRule[] ToolContractTypeReplacementRules =
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

        internal static readonly TypeReplacementRule[] DomainTypeReplacementRules =
        {
            new("ServiceResult", "ServiceResult"),
            new("ToolSettingsCatalogItem", "ToolSettingsCatalogItem")
        };

        internal static readonly TypeReplacementRule[] ApplicationTypeReplacementRules =
        {
            new("MainThreadSwitcher", "MainThreadSwitcher"),
            new("SwitchToMainThreadAwaitable", "SwitchToMainThreadAwaitable")
        };

        internal static readonly TypeReplacementRule[] FirstPartyScreenshotTypeReplacementRules =
        {
            new("EditorWindowCaptureUtility", "EditorWindowCaptureUtility"),
            new("WindowMatchMode", "WindowMatchMode"),
            new("CaptureMode", "CaptureMode"),
            new("ScreenshotSchema", "ScreenshotSchema"),
            new("ScreenshotResponse", "ScreenshotResponse"),
            new("ScreenshotInfo", "ScreenshotInfo"),
            new("UIElementInfo", "UIElementInfo")
        };

        internal static readonly ReplacementRule[] CSharpReplacementRules =
        {
            new(
                $@"(?<![\w.])(?:global::)?{Regex.Escape(LegacyNamespace)}\.CustomToolManager\b",
                $"{CurrentNamespace}.UnityCliLoopToolRegistrar"),
            new(LegacyNamespacePattern, CurrentNamespace)
        };

        internal static readonly ReplacementRule[] RegistrarReplacementRules =
        {
            new(Regex.Escape($"{CurrentDomainNamespace}.ToolInfo"), $"{CurrentNamespace}.ToolInfo"),
            new(
                Regex.Escape($"{CurrentApplicationNamespace}.UnityCliLoopToolRegistrar"),
                $"{CurrentNamespace}.UnityCliLoopToolRegistrar")
        };

        internal static readonly Regex UnqualifiedToolInfoRegex =
            new(@"(?<![\.:])\bToolInfo\b(?!\s*=)", RegexOptions.Compiled);
    }
}
