using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies V3 third-party tool migration rewrite rules.
    /// </summary>
    public sealed class ThirdPartyToolMigrationRulesTests
    {
        [Test]
        public void MigrateCSharpSource_WhenLegacyToolApiIsUsed_RewritesToV3Contracts()
        {
            // Verifies that V2 custom tool source is rewritten to the V3 public contract names.
            string source = @"using io.github.hatayama.uLoopMCP;

namespace Samples
{
    [McpTool(Description = ""hello"")]
    public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
    {
    }

    public sealed class HelloSchema : BaseToolSchema
    {
    }

    public sealed class HelloResponse : BaseToolResponse
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("using io.github.hatayama.UnityCliLoop.ToolContracts;"));
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolSchema"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP"));
            Assert.That(result.ReplacementCount, Is.EqualTo(5));
        }

        [Test]
        public void MigrateCSharpSource_WhenNoLegacyToolApiIsUsed_KeepsContent()
        {
            // Verifies that unrelated C# files are not rewritten.
            string source = "public sealed class PlainClass {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentFirstPartyToolTypesAreUsed_KeepsNamespaceImport()
        {
            // Verifies that first-party tool implementations are not rebound to the public contract namespace.
            string source = @"using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class InputTest
{
    private SimulateKeyboardTool tool = null;
    private SimulateKeyboardResponse response = null;
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentApplicationTypesAreUsed_KeepsNamespaceImport()
        {
            // Verifies that application-layer implementation references are not rebound to the public contract namespace.
            string source = @"using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class SetupTest
{
    private SkillInstallState installState;
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentDomainTypesAreUsed_KeepsNamespaceImport()
        {
            // Verifies that domain-layer implementation references are not rebound to the public contract namespace.
            string source = @"using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class SettingsTest
{
    private ToolSettingsService settingsService;
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentDomainAliasUsesMovedContractTypes_RewritesContracts()
        {
            // Verifies that current Domain aliases do not leave moved public contract types in the Domain namespace.
            string source = @"using Dom = io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static Dom.ToolInfo[] GetTools()
    {
        return new Dom.ToolInfo[0];
    }

    public static Dom.ServiceResult<int> Create()
    {
        return null;
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[] GetTools"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[0]"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> Create"));
            Assert.That(result.Content, Does.Not.Contain("Dom.ToolInfo"));
            Assert.That(result.Content, Does.Not.Contain("Dom.ServiceResult"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentToolContractsReferencesUseBareNames_KeepsContent()
        {
            // Verifies that already-current ToolContracts references are not rewritten to fully qualified names.
            string source = @"using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CurrentReferences
{
    private WindowMatchMode matchMode = WindowMatchMode.exact;

    public void Run()
    {
        EditorWindowCaptureUtility.GetOpenWindowNames();
        MainThreadSwitcher.RegisterService(null);
        ScreenshotResponse response = new();
        List<UIElementInfo> elements = new();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeSuffixHasArguments_DropsUnsupportedArguments()
        {
            // Verifies that old attribute suffix syntax does not keep removed V3 attribute arguments.
            string source =
                "using io.github.hatayama.uLoopMCP;\n" +
                "[McpToolAttribute(Description = \"hello\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
            Assert.That(result.Content, Does.Not.Contain("UnityCliLoopToolAttribute("));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeHasSupportedArguments_PreservesSupportedArguments()
        {
            // Verifies that migration drops removed metadata without changing supported tool visibility metadata.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(Description = \"hello\", DisplayDevelopmentOnly = true)] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool(DisplayDevelopmentOnly = true)]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeDescriptionIsRawStringWithComma_DropsDescriptionArgument()
        {
            // Verifies that commas inside raw string literals do not split legacy attribute arguments.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(Description = \"\"\"\"say \"hi\", world\"\"\"\", DisplayDevelopmentOnly = true)] " +
                "public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool(DisplayDevelopmentOnly = true)]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
            Assert.That(result.Content, Does.Not.Contain("\"\"\"\"say \"hi\", world\"\"\"\""));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolDescriptionInterpolatesCommaExpression_DropsDescriptionArgument()
        {
            // Verifies that commas inside interpolated-string expressions do not split legacy attribute arguments.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(Description = $\"{string.Join(\",\", values)}\", DisplayDevelopmentOnly = true)] " +
                "public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool(DisplayDevelopmentOnly = true)]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
            Assert.That(result.Content, Does.Not.Contain("string.Join"));
        }

        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsInterpolatedRawStringWithRawStringHole_FindsOuterStringEnd()
        {
            // Verifies that raw-string delimiters inside nested interpolation holes do not close the outer string.
            string source = "$\"{Format($$\"\"\"outer {{ \"\"\" } , still text \"\"\" }} final\"\"\")}\", next";
            int expectedEndIndex = source.IndexOf(", next") - 1;

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(
                source,
                0);

            Assert.That(endIndex, Is.EqualTo(expectedEndIndex));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeHasSecurityArgument_RewritesSecurityArgument()
        {
            // Verifies that supported security metadata keeps compiling after the enum rename.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(RequiredSecuritySetting = SecuritySettings.None)] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[UnityCliLoopTool(RequiredSecuritySetting = UnityCliLoopSecuritySetting.None)]"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeUsesSecurityAlias_KeepsAliasIdentifier()
        {
            // Verifies that aliases containing the old enum name are not rewritten as partial identifiers.
            string source = @"using LegacySecuritySettings = io.github.hatayama.uLoopMCP.SecuritySettings;
using io.github.hatayama.uLoopMCP;

[McpTool(RequiredSecuritySetting = LegacySecuritySettings.None)]
public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "using LegacySecuritySettings = io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopSecuritySetting;"));
            Assert.That(result.Content, Does.Contain(
                "[UnityCliLoopTool(RequiredSecuritySetting = LegacySecuritySettings.None)]"));
            Assert.That(result.Content, Does.Not.Contain("LegacyUnityCliLoopSecuritySetting"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeSharesAttributeList_RewritesOnlyLegacyToolEntry()
        {
            // Verifies that valid C# attribute lists migrate the tool attribute without dropping sibling attributes.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(Description = \"hello\"), System.Obsolete] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool, System.Obsolete]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolDescriptionContainsBracket_RewritesAttribute()
        {
            // Verifies that description text cannot terminate the attribute-list scan early.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool(Description = \"Use [foo]\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeIsQualified_RewritesQualifiedAttribute()
        {
            // Verifies that tools without a namespace import still receive a compilable V3 attribute.
            string source =
                "[io.github.hatayama.uLoopMCP.McpToolAttribute(Description = \"hello\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeIsGlobalQualified_RewritesGlobalQualifiedAttribute()
        {
            // Verifies that global-qualified V2 attributes do not bypass argument cleanup.
            string source =
                "[global::io.github.hatayama.uLoopMCP.McpTool(Description = \"hello\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeIsAliasQualified_RewritesAliasQualifiedAttribute()
        {
            // Verifies that namespace alias attribute shorthand migrates to a resolvable V3 attribute.
            string source = @"using Old = io.github.hatayama.uLoopMCP;

[Old.McpTool(DisplayDevelopmentOnly = true)]
public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool(DisplayDevelopmentOnly = true)]"));
            Assert.That(result.Content, Does.Not.Contain("Old.McpTool"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyPublicHelpersAreUsed_RewritesHelperTypes()
        {
            // Verifies that migrated custom tools keep compiling when they override helper-driven schema behavior.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class HelloTool
{
    public ToolParameterSchema ParameterSchema => ToolParameterSchemaGenerator.FromDto<HelloSchema>();

    private void Fail()
    {
        throw new ParameterValidationException(""bad"");
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolParameterSchemaGenerator"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolParameterValidationException"));
            Assert.That(result.Content, Does.Not.Match(@"\bToolParameterSchemaGenerator\b"));
            Assert.That(result.Content, Does.Not.Match(@"\bParameterValidationException\b"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMcpConstantsAreUsed_RewritesConstantsType()
        {
            // Verifies that legacy public constants references resolve to the V3 constants type.
            string source =
                "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public static class ToolConstants\n" +
                "{\n" +
                "    public const string Name = McpConstants.PROJECT_NAME;\n" +
                "    public static string QualifiedName => io.github.hatayama.uLoopMCP.McpConstants.PROJECT_NAME;\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("UnityCliLoopConstants.PROJECT_NAME"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.PROJECT_NAME"));
            Assert.That(result.Content, Does.Not.Contain("McpConstants"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyEditorDelayIsUsed_RewritesFrameWait()
        {
            // Verifies that legacy frame waits migrate to the V3 frame waiter API.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        await EditorDelay.DelayFrame(1, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("using io.github.hatayama.UnityCliLoop.ToolContracts;"));
            Assert.That(result.Content, Does.Contain(
                "await EditorFrameWaiter.WaitFramesOrTimeoutAsync(1, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
            Assert.That(result.Content, Does.Not.Contain("EditorDelay"));
            Assert.That(result.Content, Does.Not.Contain("DelayFrame"));
        }

        [Test]
        public void MigrateCSharpSource_WhenOnlyQualifiedCurrentContractsExist_QualifiesEditorDelayReplacement()
        {
            // Verifies that helper rewrites remain resolvable when current contracts are only fully qualified.
            string source = @"using System.Threading;
using System.Threading.Tasks;

public sealed class DelayTool : io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool<DelaySchema, DelayResponse>
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        await EditorDelay.DelayFrame(2, ct);
    }
}

public sealed class DelaySchema : io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolSchema
{
}

public sealed class DelayResponse : io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolResponse
{
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
            Assert.That(result.Content, Does.Not.Contain(
                "await EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenFileReliesOnGlobalUsing_RewritesEditorDelay()
        {
            // Verifies that split files relying on a legacy global using migrate frame waits.
            string source = @"using System.Threading;
using System.Threading.Tasks;

public sealed class ScreenshotTool
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        await EditorDelay.DelayFrame(cancellationToken: ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await EditorFrameWaiter.WaitFramesOrTimeoutAsync(1, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
            Assert.That(result.Content, Does.Not.Contain("EditorDelay"));
            Assert.That(result.Content, Does.Not.Contain("DelayFrame"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyEditorWindowCaptureUtilityIsUsed_RewritesCaptureCall()
        {
            // Verifies that legacy window capture calls migrate to the V3 screenshot utility signature.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenFileReliesOnGlobalUsing_RewritesEditorWindowCaptureUtility()
        {
            // Verifies that split files relying on a legacy global using migrate window capture calls.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenAssemblyUsesCurrentFirstPartyToolsGlobalUsing_RewritesEditorWindowCaptureUtility()
        {
            // Verifies that split files relying on a current FirstPartyTools global using finish capture migration.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: false,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: true,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenAssemblyDeclaresCaptureUtility_KeepsBareLocalHelperCall()
        {
            // Verifies that bare capture calls are not rebound when the project owns the helper type.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class ScreenshotTool
{
    public Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}

public static class EditorWindowCaptureUtility
{
    public static Task<Texture2D> CaptureWindowAsync(
        EditorWindow window,
        float resolutionScale,
        CancellationToken ct)
    {
        return Task.FromResult<Texture2D>(null);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: new[] { "EditorWindowCaptureUtility" });

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Does.Contain(
                "return EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentToolContractsFileHasLegacyEditorWindowCaptureUtility_RewritesCaptureCall()
        {
            // Verifies that partially migrated files still finish the window capture helper migration.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentFirstPartyToolsFileHasLegacyEditorWindowCaptureUtility_RewritesCaptureCall()
        {
            // Verifies that partially migrated screenshot helper files still receive the V3 timeout argument.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentQualifiedCaptureWindowLacksTimeout_DoesNotDoubleQualify()
        {
            // Verifies that already qualified capture calls receive the timeout without duplicating namespaces.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.io.github.hatayama.UnityCliLoop.FirstPartyTools"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentFirstPartyToolsAliasCaptureLacksTimeout_RewritesCaptureCall()
        {
            // Verifies that current FirstPartyTools aliases receive the V3 timeout argument without rebinding the namespace.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;"));
            Assert.That(result.Content, Does.Contain(
                "return (await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("Fpt.UnityCliLoopConstants"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCaptureWindowExpressionBodiedAwait_ProjectsTexture()
        {
            // Verifies that expression-bodied awaits keep the legacy Texture2D return shape.
            string source = @"using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct) =>
        await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);

    public Func<EditorWindow, CancellationToken, Task<Texture2D>> CreateCapture()
    {
        return async (window, ct) => await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "=>\n        (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Contain(
                "=> (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain(
                "=> await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyCaptureUsesNamedCancellationToken_RewritesArgumentName()
        {
            // Verifies that migrated screenshot capture calls use the V3 cancellation token argument name.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, cancellationToken: ct);
    }

    public async Task CaptureRenderingAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, cancellationToken: ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct: ct)"));
            Assert.That(result.Content, Does.Contain(
                "CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct: ct)"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyCaptureUsesNamedArgumentsInDifferentOrder_ReordersArguments()
        {
            // Verifies that legal named-argument capture calls keep the V3 positional timeout in the right slot.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(
            cancellationToken: ct,
            window: window,
            resolutionScale: 1.0f);
    }

    public async Task CaptureRenderingAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
            cancellationToken: ct,
            resolutionScale: 1.0f);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct: ct)"));
            Assert.That(result.Content, Does.Contain(
                "CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct: ct)"));
            Assert.That(result.Content, Does.Not.Contain("CaptureWindowAsync(ct: ct"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
        }

        [Test]
        public void MigrateCSharpSource_WhenQualifiedLegacyCaptureIsUsed_QualifiesTimeoutConstant()
        {
            // Verifies that capture rewrites keep timeout constants resolvable without a ToolContracts using directive.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Legacy = io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureQualifiedAsync(EditorWindow window, CancellationToken ct)
    {
        return await io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }

    public async Task<Texture2D> CaptureAliasAsync(EditorWindow window, CancellationToken ct)
    {
        return await Legacy.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
            Assert.That(result.Content, Does.Contain("Legacy.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
            Assert.That(result.Content, Does.Contain(
                "using Legacy = io.github.hatayama.UnityCliLoop.ToolContracts;"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCaptureWindowUsesConfigureAwait_PreservesConfigureAwaitOnTask()
        {
            // Verifies that texture extraction happens after awaiting the configured capture task.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct).ConfigureAwait(false);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ConfigureAwait(false)).texture;"));
            Assert.That(result.Content, Does.Not.Contain(".texture.ConfigureAwait"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCaptureWindowAwaitIgnoresResult_KeepsStatementValid()
        {
            // Verifies that ignored capture results do not become invalid property-access statements.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public async Task CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
        await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct).ConfigureAwait(false);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ConfigureAwait(false);"));
            Assert.That(result.Content, Does.Not.Contain(".texture;"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCaptureWindowTaskIsReturned_MapsTaskTextureResult()
        {
            // Verifies that non-awaited legacy capture tasks keep their old Task<Texture2D> shape.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotTool
{
    public Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ContinueWith(__unityCliLoopCaptureTask => __unityCliLoopCaptureTask.GetAwaiter().GetResult().texture)"));
            Assert.That(result.Content, Does.Not.Contain(
                "return io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyScreenshotHelpersAreUsed_RewritesFirstPartyReferences()
        {
            // Verifies that screenshot helper types moved out of the public tool contract namespace are fully qualified.
            string source = @"using UnityEditor;
using io.github.hatayama.uLoopMCP;

public sealed class ScreenshotHelper
{
    public WindowMatchMode MatchMode => WindowMatchMode.contains;
    public CaptureMode CaptureMode => CaptureMode.rendering;
    public ScreenshotInfo CreateInfo() => new ScreenshotInfo();
    public UIElementInfo CreateElement() => new UIElementInfo();
    public EditorWindow[] FindWindows() => EditorWindowCaptureUtility.FindWindowsByName(""Game"", WindowMatchMode.exact);
    public string[] GetNames() => EditorWindowCaptureUtility.GetOpenWindowNames();
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.WindowMatchMode MatchMode"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.WindowMatchMode.contains"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.CaptureMode CaptureMode"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.CaptureMode.rendering"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotInfo()"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo()"));
            Assert.That(result.Content, Does.Contain(
                "EditorWindowCaptureUtility.FindWindowsByName"));
            Assert.That(result.Content, Does.Contain(
                "EditorWindowCaptureUtility.GetOpenWindowNames"));
        }

        [Test]
        public void MigrateCSharpSource_WhenFileDeclaresLocalUIElementInfo_KeepsLocalReferences()
        {
            // Verifies that project DTOs sharing first-party screenshot names are not rewritten.
            string source = @"using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class LocalTool : UnityCliLoopTool<LocalSchema, LocalResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }

    private (List<UIElementInfo> clickableElements, List<UIElementInfo> draggableElements) Classify()
    {
        List<UIElementInfo> clickableElements = new();
        List<UIElementInfo> draggableElements = new();
        UIElementInfo elementInfo = CreateElementInfo();
        clickableElements.Add(elementInfo);
        return (clickableElements, draggableElements);
    }

    private UIElementInfo CreateElementInfo()
    {
        return new UIElementInfo();
    }
}

public sealed class LocalSchema : UnityCliLoopToolSchema
{
}

public sealed class LocalResponse : UnityCliLoopToolResponse
{
}

public sealed class UIElementInfo
{
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)"));
            Assert.That(result.Content, Does.Contain("private UIElementInfo CreateElementInfo()"));
            Assert.That(result.Content, Does.Contain("List<UIElementInfo> clickableElements"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo"));
        }

        [Test]
        public void MigrateCSharpSource_WhenFileDeclaresLocalUIElementInfoAndUsesExplicitFirstPartyInfo_PreservesExplicitReference()
        {
            // Verifies that explicit first-party DTO references are not rebound to project DTOs.
            string source = @"using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class LocalTool : UnityCliLoopTool<LocalSchema, LocalResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }

    private List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> BuildFirstPartyElements()
    {
        List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> elements = new();
        elements.Add(CreateFirstPartyElement());
        return elements;
    }

    private io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo CreateFirstPartyElement()
    {
        return new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo();
    }

    private UIElementInfo CreateProjectElement()
    {
        return new UIElementInfo();
    }
}

public sealed class LocalSchema : UnityCliLoopToolSchema
{
}

public sealed class LocalResponse : UnityCliLoopToolResponse
{
}

public sealed class UIElementInfo
{
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "private List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> BuildFirstPartyElements()"));
            Assert.That(result.Content, Does.Contain(
                "List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> elements = new();"));
            Assert.That(result.Content, Does.Contain(
                "private io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo CreateFirstPartyElement()"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo()"));
            Assert.That(result.Content, Does.Contain("private UIElementInfo CreateProjectElement()"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyGameRenderingCaptureIsUsed_ProjectsLegacyTuple()
        {
            // Verifies that old rendering capture deconstruction keeps the legacy two-item tuple shape.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "(texture, yOffset) = await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ContinueWith(__unityCliLoopRenderingTask => (__unityCliLoopRenderingTask.GetAwaiter().GetResult().texture, __unityCliLoopRenderingTask.GetAwaiter().GetResult().yOffset))"));
            Assert.That(result.Content, Does.Not.Contain("CaptureGameRenderingAsync(1.0f, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentGameRenderingCaptureIsUsed_KeepsCurrentTuple()
        {
            // Verifies that already-current rendering captures keep the V3 three-item tuple shape.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        bool timedOut = false;
        (texture, yOffset, timedOut) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain(
                "(texture, yOffset, timedOut) = await"));
            Assert.That(result.Content, Does.Contain(
                "CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
            Assert.That(result.Content, Does.Not.Contain(".ContinueWith"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentBareGameRenderingCaptureUsesTwoItemDeconstruction_AddsDiscard()
        {
            // Verifies that bare current rendering captures deconstruct the V3 timeout result.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "(texture, yOffset, _) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync"));
            Assert.That(result.Content, Does.Not.Contain("(texture, yOffset) = await"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentRenderingCaptureUsesFirstPartyAlias_AddsDiscard()
        {
            // Verifies that current FirstPartyTools aliases receive the V3 rendering discard without rebinding the namespace.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await Fpt.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;"));
            Assert.That(result.Content, Does.Contain(
                "(texture, yOffset, _) = await Fpt.EditorWindowCaptureUtility.CaptureGameRenderingAsync"));
        }

        [Test]
        public void MigrateCSharpSource_WhenProjectCaptureHelperUsesTwoItemDeconstruction_KeepsProjectHelperCall()
        {
            // Verifies that discard insertion only targets the V3 first-party capture helper.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);
    }
}

public static class EditorWindowCaptureUtility
{
    public static Task<(Texture2D texture, int yOffset)> CaptureGameRenderingAsync(
        float resolutionScale,
        int timeoutMilliseconds,
        CancellationToken ct)
    {
        return Task.FromResult<(Texture2D texture, int yOffset)>((null, 0));
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Does.Contain("(texture, yOffset) = await"));
            Assert.That(result.Content, Does.Not.Contain("(texture, yOffset, _) = await"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyGameRenderingCaptureIsReturned_ProjectsLegacyTuple()
        {
            // Verifies that return-await rendering captures keep returning the legacy two-item tuple.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class RenderingCapture
{
    public async Task<(Texture2D texture, int yOffset)> CaptureAsync(CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ContinueWith(__unityCliLoopRenderingTask => (__unityCliLoopRenderingTask.GetAwaiter().GetResult().texture, __unityCliLoopRenderingTask.GetAwaiter().GetResult().yOffset));"));
            Assert.That(result.Content, Does.Not.Contain(
                "return await io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyGameRenderingCaptureTaskIsReturned_MapsTaskTupleResult()
        {
            // Verifies that task-returning rendering captures keep the legacy Task two-item tuple.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.uLoopMCP;

public sealed class RenderingCapture
{
    public Task<(Texture2D texture, int yOffset)> CaptureAsync(CancellationToken ct)
    {
        return EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct).ContinueWith(__unityCliLoopRenderingTask => (__unityCliLoopRenderingTask.GetAwaiter().GetResult().texture, __unityCliLoopRenderingTask.GetAwaiter().GetResult().yOffset));"));
            Assert.That(result.Content, Does.Not.Contain(
                "return EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimerDelayNamedCancellationTokenIsUsed_RewritesArgumentName()
        {
            // Verifies that TimerDelay named arguments keep compiling after the V3 cancellation token rename.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class DelayTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await TimerDelay.Wait(10, cancellationToken: ct);
        await TimerDelay.WaitThenExecuteOnMainThread(10, () => {}, cancellationToken: ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("await TimerDelay.Wait(10, ct: ct);"));
            Assert.That(result.Content, Does.Contain(
                "await TimerDelay.WaitThenExecuteOnMainThread(10, () => {}, ct: ct);"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherIsUsed_RewritesApplicationReferences()
        {
            // Verifies that main-thread switch helpers moved to Application keep compiling after timing arguments were removed.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
        await MainThreadSwitcher.SwitchToMainThread(timing: PlayerLoopTiming.PostLateUpdate, cancellationToken: ct);
        await MainThreadSwitcher.SwitchToMainThread(timing: default, cancellationToken: ct);
        await MainThreadSwitcher.SwitchToMainThread(timing);
        await MainThreadSwitcher.SwitchToMainThread(timing, ct);
        await MainThreadSwitcher.SwitchToMainThread(default, ct);
        SwitchToMainThreadAwaitable awaitable = MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update);
        bool isMainThread = MainThreadSwitcher.IsMainThread;
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct: ct);"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.SwitchToMainThreadAwaitable awaitable = io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread();"));
            Assert.That(result.Content, Does.Contain(
                "bool isMainThread = io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.IsMainThread;"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(timing)"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(timing, ct)"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(default, ct)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
            Assert.That(result.Content, Does.Not.Contain("timing:"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentApplicationAliasSwitcherHasLegacyTiming_RemovesTimingArgument()
        {
            // Verifies that moved public contract types are rebound when a partially migrated Application alias is used.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using App = io.github.hatayama.UnityCliLoop.Application;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await App.MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "using App = io.github.hatayama.UnityCliLoop.Application;"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
            Assert.That(result.Content, Does.Not.Contain("App.MainThreadSwitcher"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming.Update"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentToolContractsSwitcherHasLegacyTiming_PreservesBareReference()
        {
            // Verifies that partially migrated ToolContracts calls drop legacy timing without adding full qualifiers.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await MainThreadSwitcher.SwitchToMainThread(ct);"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming.Update"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherUsesGlobalQualifiedTiming_RemovesTimingArgument()
        {
            // Verifies that global-qualified legacy timing values are not mistaken for named arguments.
            string source = @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(
            global::io.github.hatayama.uLoopMCP.PlayerLoopTiming.Update,
            ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming.Update"));
            Assert.That(result.Content, Does.Not.Contain("global::"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherUsesOutOfOrderNamedCancellationToken_PreservesToken()
        {
            // Verifies that named cancellation tokens are preserved even when callers pass them before timing.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(cancellationToken: ct, timing: PlayerLoopTiming.Update);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct: ct);"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread();"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
            Assert.That(result.Content, Does.Not.Contain("timing:"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherUsesSingleTimingVariable_RemovesStaleTimingParameter()
        {
            // Verifies that timing-only wrapper parameters do not leave V3 migration output uncompilable.
            string source = @"using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync()"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread();"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(loop)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherUsesSingleCancelVariable_PreservesToken()
        {
            // Verifies that ambiguous single-token switch calls keep cancellation-like arguments.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken cancel)
    {
        await MainThreadSwitcher.SwitchToMainThread(cancel);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("SwitchToMainThread(cancel);"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread();"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyMainThreadSwitcherUsesSingleLoopVariables_RemovesTimingArguments()
        {
            // Verifies that common single-token timing variables still migrate to the no-argument V3 call.
            string source = @"using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    private PlayerLoopTiming loop;

    public async Task RunAsync(PlayerLoopTiming playerLoop)
    {
        await MainThreadSwitcher.SwitchToMainThread(playerLoop);
        await MainThreadSwitcher.SwitchToMainThread(this.loop);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("SwitchToMainThread();"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(playerLoop)"));
            Assert.That(result.Content, Does.Not.Contain("SwitchToMainThread(this.loop)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenPartiallyMigratedSwitcherLeavesTimingParameter_RemovesStaleParameter()
        {
            // Verifies that rerunning migration cleans stale timing parameters after the switch call was already migrated.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;
using io.github.hatayama.UnityCliLoop.Application;

public sealed class MainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming loop"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnrelatedPlayerLoopTimingFieldExists_KeepsDeclaration()
        {
            // Verifies that timing declaration cleanup does not mutate project-owned timing types without switch migration.
            string source = @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ProjectState
{
    [SerializeField]
    private PlayerLoopTiming loop;
}

public enum PlayerLoopTiming
{
    Update
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Does.Contain("[SerializeField]"));
            Assert.That(result.Content, Does.Contain("private PlayerLoopTiming loop;"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnusedTimingFieldHasAttributes_RemovesAttributeBlock()
        {
            // Verifies that removing a stale timing field does not leave its attributes attached to the next member.
            string source = @"using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    [SerializeField]
    [UnityEngine.Tooltip(""Legacy timing"")]
    private PlayerLoopTiming loop;

    public string Name => ""ready"";

    public async Task RunAsync()
    {
        await MainThreadSwitcher.SwitchToMainThread(loop);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Not.Contain("[SerializeField]"));
            Assert.That(result.Content, Does.Not.Contain("Legacy timing"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming loop"));
            Assert.That(result.Content, Does.Contain("public string Name => \"ready\";"));
        }

        [Test]
        public void MigrateCSharpSource_WhenProjectDefinesMainThreadSwitcher_KeepsProjectHelperCall()
        {
            // Verifies that bare main-thread switch calls are not rebound when the project owns the helper type.
            string source = @"using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public Task RunAsync(PlayerLoopTiming loop)
    {
        return MainThreadSwitcher.SwitchToMainThread(loop);
    }
}

public static class MainThreadSwitcher
{
    public static Task SwitchToMainThread(PlayerLoopTiming loop)
    {
        return Task.CompletedTask;
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("return MainThreadSwitcher.SwitchToMainThread(loop);"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenAssemblyDeclaresMainThreadSwitcher_KeepsProjectHelperCall()
        {
            // Verifies that helper ownership discovered in a sibling file prevents rebinding bare calls.
            string source = @"using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public Task RunAsync(PlayerLoopTiming loop)
    {
        return MainThreadSwitcher.SwitchToMainThread(loop);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: new[] { "MainThreadSwitcher" });

            Assert.That(result.Content, Does.Contain("return MainThreadSwitcher.SwitchToMainThread(loop);"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperHasSameFileCaller_UpdatesCallerArguments()
        {
            // Verifies that wrapper call sites stay aligned when a stale timing parameter is removed.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }

    public Task CallAsync()
    {
        return RunAsync(PlayerLoopTiming.Update, CancellationToken.None);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return RunAsync(CancellationToken.None);"));
            Assert.That(result.Content, Does.Not.Contain("RunAsync(PlayerLoopTiming.Update, CancellationToken.None)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperCallerOmitsOptionalToken_UpdatesCallerArguments()
        {
            // Verifies that callers omitting optional trailing parameters stay aligned after timing removal.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct = default)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }

    public Task CallAsync()
    {
        return RunAsync(PlayerLoopTiming.Update);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct = default)"));
            Assert.That(result.Content, Does.Contain("return RunAsync();"));
            Assert.That(result.Content, Does.Not.Contain("RunAsync(PlayerLoopTiming.Update)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingLambdaUsesTimingParameter_KeepsLambdaSignature()
        {
            // Verifies that lambda parameter lists are not treated as method declarations.
            string source = @"using System;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public enum PlayerLoopTiming
{
    Update
}

public sealed class MainThreadTool
{
    public Func<PlayerLoopTiming, Task> Create()
    {
        return async (PlayerLoopTiming loop) =>
        {
            await MainThreadSwitcher.SwitchToMainThread(loop);
        };
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("async (PlayerLoopTiming loop) =>"));
            Assert.That(result.Content, Does.Not.Contain("async () =>"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperIsGeneric_UpdatesGenericCallerArguments()
        {
            // Verifies that generic wrapper call sites stay aligned after stale timing parameters are removed.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync<T>(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }

    public Task CallAsync(CancellationToken ct)
    {
        return RunAsync<int>(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync<T>(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return RunAsync<int>(ct);"));
            Assert.That(result.Content, Does.Not.Contain("RunAsync<int>(PlayerLoopTiming.Update, ct)"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenSameNameTimingCallerTargetsDifferentType_KeepsOtherTypeArguments()
        {
            // Verifies that caller rewrites target the migrated owner type instead of every same-name method.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public enum PlayerLoopTiming
{
    Update
}

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class OtherWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.FromResult(loop.ToString());
    }
}

public sealed class MainThreadCaller
{
    public Task CallMigratedAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }

    public Task CallOtherAsync(OtherWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenSameNameTimingCallerTargetsDifferentNamespace_KeepsOtherTypeArguments()
        {
            // Verifies that caller rewrites distinguish migrated owner types across namespaces.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Migrated
{
    public sealed class Wrapper
    {
        public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(loop, ct);
        }
    }
}

namespace Other
{
    public enum PlayerLoopTiming
    {
        Update
    }

    public sealed class Wrapper
    {
        public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            return Task.FromResult(loop.ToString());
        }
    }

    public sealed class Caller
    {
        public Task CallAsync(Wrapper wrapper, CancellationToken ct)
        {
            return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("namespace Migrated"));
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerUsesQualifiedOtherTarget_KeepsOtherTypeArguments()
        {
            // Verifies that qualified receivers are not reduced to the final identifier before matching.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Other
{
    public sealed class Wrapper
    {
        public static Task RunStaticAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class WrapperFactory
    {
        public Wrapper Wrapper { get; }
    }
}

namespace Migrated
{
    public sealed class Wrapper
    {
        public static async Task RunStaticAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(loop, ct);
        }
    }

    public sealed class MainThreadCaller
    {
        public Task CallOtherStaticAsync(CancellationToken ct)
        {
            return global::Other.Wrapper.RunStaticAsync(PlayerLoopTiming.Update, ct);
        }

        public Task CallOtherMemberAsync(Other.WrapperFactory factory, CancellationToken ct)
        {
            return factory.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public static async Task RunStaticAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain(
                "return global::Other.Wrapper.RunStaticAsync(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Contain(
                "return factory.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerUsesThisQualifiedField_RewritesCallerArguments()
        {
            // Verifies that this-qualified fields still match migrated wrapper receiver types.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    private MainThreadWrapper wrapper;

    public Task CallAsync(CancellationToken ct)
    {
        return this.wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return this.wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("this.wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerUsesFieldDeclaredAfterCall_RewritesCallerArguments()
        {
            // Verifies that type members declared after the call still match migrated wrapper receiver types.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }

    private MainThreadWrapper wrapper;
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerUsesPropertyDeclaredAfterCall_RewritesCallerArguments()
        {
            // Verifies that later properties are treated as type members, not out-of-scope locals.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }

    private MainThreadWrapper wrapper { get; }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerOnlyHasLocalDeclaredAfterCall_KeepsCallerArguments()
        {
            // Verifies that later local declarations are not treated as receiver declarations in scope.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class OtherWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(OtherWrapper wrapper, CancellationToken ct)
    {
        Task task = wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        MainThreadWrapper wrapper2 = new MainThreadWrapper();
        return task;
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("Task task = wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Not.Contain("Task task = wrapper.RunAsync(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenPreviousMethodLocalMatchesMigratedType_KeepsFieldReceiverArguments()
        {
            // Verifies that locals from previous methods do not shadow the actual receiver field.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class OtherWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class MainThreadCaller
{
    public void Prepare()
    {
        MainThreadWrapper wrapper = new MainThreadWrapper();
    }

    public Task CallAsync(CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }

    private OtherWrapper wrapper;
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Not.Contain("return wrapper.RunAsync(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLaterMethodParameterMatchesMigratedType_KeepsFieldReceiverArguments()
        {
            // Verifies that later method parameters are not treated as type member receiver declarations.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class OtherWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }

    private OtherWrapper wrapper;

    public void Configure(MainThreadWrapper wrapper)
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Not.Contain("return wrapper.RunAsync(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenTimingCallerUsesImportedWrapperType_RewritesCallerArguments()
        {
            // Verifies that using-imported receiver types match migrated wrapper signatures across namespaces.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Helpers
{
    public sealed class Wrapper
    {
        public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(loop, ct);
        }
    }
}

namespace Tools
{
    using Helpers;

    public sealed class Caller
    {
        public Task CallAsync(Wrapper wrapper, CancellationToken ct)
        {
            return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCommentLooksLikeMigratedDeclaration_KeepsOtherTypeArguments()
        {
            // Verifies that declaration-shaped text in comments does not retarget caller rewrites.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MigratedWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class OtherWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(OtherWrapper wrapper, CancellationToken ct)
    {
        // MigratedWrapper wrapper;
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenNamespacedStaticTimingWrapperIsCalled_RewritesCallerArguments()
        {
            // Verifies that static wrapper calls inside the same namespace match the migrated owner type.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Tools
{
    public static class Wrapper
    {
        public static async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(loop, ct);
        }
    }

    public sealed class Caller
    {
        public Task CallAsync(CancellationToken ct)
        {
            return Wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public static async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return Wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("Wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenNamespacedNestedTimingWrapperIsFullyQualified_RewritesCallerArguments()
        {
            // Verifies that namespace-qualified nested wrapper calls match the migrated owner type.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Tools
{
    public sealed class Outer
    {
        public static class Wrapper
        {
            public static async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread(loop, ct);
            }
        }
    }

    public sealed class Caller
    {
        public Task CallAsync(CancellationToken ct)
        {
            return Tools.Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public static async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return Tools.Outer.Wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("Tools.Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenOtherNamespaceHasSameNestedTimingWrapperName_KeepsOtherCallerArguments()
        {
            // Verifies that nested wrapper caller rewrites do not cross namespace ownership.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Tools
{
    public sealed class Outer
    {
        public static class Wrapper
        {
            public static async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread(loop, ct);
            }
        }
    }
}

namespace Other
{
    public sealed class Caller
    {
        public Task CallAsync(CancellationToken ct)
        {
            return Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }

    public sealed class Outer
    {
        public static class Wrapper
        {
            public static Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
            {
                return Task.CompletedTask;
            }
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public static async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Not.Contain("return Outer.Wrapper.RunAsync(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperCallerUsesVarLocal_UpdatesCallerArguments()
        {
            // Verifies that var locals initialized with the migrated owner type keep wrapper call sites compiling.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(CancellationToken ct)
    {
        var wrapper = new MainThreadWrapper();
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenNestedLegacyTimingWrapperIsCalled_UpdatesCallerArguments()
        {
            // Verifies that nested migrated owner types keep qualified wrapper call sites compiling.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

namespace Tools
{
    public sealed class Outer
    {
        public static class Wrapper
        {
            public static async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread(loop, ct);
            }
        }
    }

    public sealed class Caller
    {
        public Task CallAsync(CancellationToken ct)
        {
            return Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct);
        }
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public static async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return Outer.Wrapper.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("Outer.Wrapper.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperCallerUsesNullConditional_UpdatesCallerArguments()
        {
            // Verifies that null-conditional calls stay aligned with migrated wrapper signatures.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper?.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper?.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper?.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperCallerUsesNullableReceiver_UpdatesCallerArguments()
        {
            // Verifies that nullable wrapper references still match the migrated owner type.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper? wrapper, CancellationToken ct)
    {
        return wrapper?.RunAsync(PlayerLoopTiming.Update, ct) ?? Task.CompletedTask;
    }

    public Task CallRequiredAsync(MainThreadWrapper? wrapper, CancellationToken ct)
    {
        return wrapper!.RunAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return wrapper?.RunAsync(ct) ?? Task.CompletedTask;"));
            Assert.That(result.Content, Does.Contain("return wrapper!.RunAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("wrapper?.RunAsync(PlayerLoopTiming.Update, ct)"));
            Assert.That(result.Content, Does.Not.Contain("wrapper!.RunAsync(PlayerLoopTiming.Update, ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperCallChain_RevisitsStaleOuterParameter()
        {
            // Verifies that chained wrapper migrations remove timing parameters exposed by earlier caller rewrites.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task InnerAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }

    public Task OuterAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return InnerAsync(loop, ct);
    }

    public Task CallAsync(CancellationToken ct)
    {
        return OuterAsync(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public async Task InnerAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("public Task OuterAsync(CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return InnerAsync(ct);"));
            Assert.That(result.Content, Does.Contain("return OuterAsync(ct);"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingWrapperIsExpressionBodied_RemovesStaleTimingParameter()
        {
            // Verifies that expression-bodied main-thread wrappers do not keep the removed timing type.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public SwitchToMainThreadAwaitable Switch(PlayerLoopTiming loop) =>
        MainThreadSwitcher.SwitchToMainThread(loop);
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public io.github.hatayama.UnityCliLoop.ToolContracts.SwitchToMainThreadAwaitable Switch() =>"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread()"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
        }

        [Test]
        public void MigrateCSharpSource_WhenConstructorUsesLegacyTimingSwitcher_PreservesConstructorSignature()
        {
            // Verifies that constructor parameters are not removed without constructor call-site migration.
            string source = @"using System.Threading;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public MainThreadWrapper(PlayerLoopTiming loop, CancellationToken ct)
    {
        MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}

public sealed class MainThreadCaller
{
    public MainThreadWrapper Create(CancellationToken ct)
    {
        return new MainThreadWrapper(PlayerLoopTiming.Update, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public MainThreadWrapper(PlayerLoopTiming loop, CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain("return new MainThreadWrapper(PlayerLoopTiming.Update, ct);"));
            Assert.That(result.Content, Does.Not.Contain("public MainThreadWrapper(CancellationToken ct)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnrelatedLocalTimingParameterIsStillUsed_PreservesParameter()
        {
            // Verifies that timing-parameter cleanup only removes stale main-thread wrapper parameters.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public enum PlayerLoopTiming
{
    Update
}

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }

    public string Format(PlayerLoopTiming loop)
    {
        return loop.ToString();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public string Format(PlayerLoopTiming loop)"));
            Assert.That(result.Content, Does.Contain("return loop.ToString();"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnrelatedTimingParameterIsUnused_PreservesSignature()
        {
            // Verifies that cleanup does not remove unused timing parameters from unrelated public methods.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }

    public void Configure(PlayerLoopTiming loop)
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public void Configure(PlayerLoopTiming loop)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnrelatedTimingParameterMentionsSwitcherInText_PreservesSignature()
        {
            // Verifies that comments and strings do not make unrelated timing parameters look migratable.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }

    public void Configure(PlayerLoopTiming loop)
    {
        string label = ""SwitchToMainThread"";
        // MainThreadSwitcher.SwitchToMainThread(loop);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public void Configure(PlayerLoopTiming loop)"));
            Assert.That(result.Content, Does.Contain("string label = \"SwitchToMainThread\";"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingOverrideUsesSwitcher_PreservesSignature()
        {
            // Verifies that override signatures are not changed independently from their base contract.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public abstract class BaseTool
{
    public abstract Task RunAsync(PlayerLoopTiming loop, CancellationToken ct);
}

public sealed class MainThreadTool : BaseTool
{
    public override async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public override async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingInterfaceImplementationUsesSwitcher_PreservesSignature()
        {
            // Verifies that interface implementations are not changed independently from their interface contract.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public interface IMainThreadTool
{
    Task RunAsync(PlayerLoopTiming loop, CancellationToken ct);
}

public sealed class MainThreadTool : IMainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingVirtualMethodUsesSwitcher_PreservesSignature()
        {
            // Verifies that virtual signatures are not changed independently from derived overrides.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public class MainThreadTool
{
    public virtual async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public virtual async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTimingExternalInterfaceImplementationUsesSwitcher_PreservesSignature()
        {
            // Verifies that possible external interface implementations keep their public contract shape.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool : IMainThreadTool
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct);"));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnrelatedTimingParameterCallsHelperSwitcher_PreservesSignature()
        {
            // Verifies that unrelated helper methods named SwitchToMainThread do not justify dropping timing parameters.
            string source = @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }

    public void Configure(PlayerLoopTiming loop, Helper helper)
    {
        helper.SwitchToMainThread();
    }
}

public sealed class Helper
{
    public void SwitchToMainThread()
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public void Configure(PlayerLoopTiming loop, Helper helper)"));
            Assert.That(result.Content, Does.Contain("helper.SwitchToMainThread();"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyRegistrarMetadataIsUsed_RewritesDomainMetadataType()
        {
            // Verifies that explicit registrar metadata declarations keep compiling after namespace migration.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ManualToolRegistration
{
    public static ToolInfo[] GetTools()
    {
        return CustomToolManager.GetRegisteredCustomTools();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[]"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyDomainHelpersAreUsed_RewritesDomainHelperTypes()
        {
            // Verifies that legacy helpers moved to Domain keep compiling after namespace migration.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolHelper
{
    public static ServiceResult<int> CreateResult()
    {
        return ServiceResult<int>.SuccessResult(1);
    }

    public static ToolSettingsCatalogItem[] GetCatalog()
    {
        return new ToolSettingsCatalogItem[0];
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> CreateResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int>.SuccessResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem[] GetCatalog"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem[0]"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP.ServiceResult"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP.ToolSettingsCatalogItem"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolSettingsCatalogItemConstructorHasDescription_RemovesDescriptionArgument()
        {
            // Verifies that old settings catalog metadata keeps compiling after the V3 constructor signature change.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolSettingsCatalogProvider
{
    public static ToolSettingsCatalogItem Create(bool displayDevelopmentOnly, bool isThirdParty)
    {
        return new ToolSettingsCatalogItem(""hello"", ""description"", displayDevelopmentOnly, isThirdParty);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem(\"hello\", displayDevelopmentOnly, isThirdParty)"));
            Assert.That(result.Content, Does.Not.Contain("\"description\", displayDevelopmentOnly"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyDomainHelpersUseNamespaceAlias_RewritesDomainHelperTypes()
        {
            // Verifies that namespace aliases targeting V2 helpers do not survive as ToolContracts references.
            string source = @"using Old = io.github.hatayama.uLoopMCP;

public static class ToolHelper
{
    public static Old.ServiceResult<int> CreateResult()
    {
        return Old.ServiceResult<int>.SuccessResult(1);
    }

    public static Old.ToolSettingsCatalogItem[] GetCatalog()
    {
        return new Old.ToolSettingsCatalogItem[0];
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> CreateResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int>.SuccessResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem[] GetCatalog"));
            Assert.That(result.Content, Does.Not.Contain("Old.ServiceResult"));
            Assert.That(result.Content, Does.Not.Contain("Old.ToolSettingsCatalogItem"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiIsUsedInsideInterpolatedString_RewritesInterpolationCode()
        {
            // Verifies that code inside interpolation holes migrates while literal text stays inert.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolCountLabel
{
    public static string GetLabel()
    {
        return $""Tools: {CustomToolManager.GetRegisteredCustomTools().Length}"";
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
            Assert.That(result.Content, Does.Not.Contain("{CustomToolManager"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiIsUsedInsideInterpolatedVerbatimString_RewritesInterpolationCode()
        {
            // Verifies that verbatim interpolation holes are treated as code.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolCountLabel
{
    public static string GetLabel()
    {
        return $@""Tools: {CustomToolManager.GetRegisteredCustomTools().Length}"";
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
            Assert.That(result.Content, Does.Not.Contain("{CustomToolManager"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiIsUsedInsideInterpolatedRawString_RewritesInterpolationCode()
        {
            // Verifies that raw interpolation holes are treated as code.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public static class ToolCountLabel\n" +
                "{\n" +
                "    public static string GetLabel()\n" +
                "    {\n" +
                "        return $\"\"\"Tools: {CustomToolManager.GetRegisteredCustomTools().Length}\"\"\";\n" +
                "    }\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
            Assert.That(result.Content, Does.Not.Contain("{CustomToolManager"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiIsUsedInsideMultiDollarRawString_RewritesInterpolationCode()
        {
            // Verifies that literal braces stay inert when multiple dollar signs are used.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public static class ToolCountLabel\n" +
                "{\n" +
                "    public static string GetLabel()\n" +
                "    {\n" +
                "        return $$\"\"\"Literal { braces } tools: {{CustomToolManager.GetRegisteredCustomTools().Length}}\"\"\";\n" +
                "    }\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("Literal { braces }"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
            Assert.That(result.Content, Does.Not.Contain("{{CustomToolManager"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyTextIsUsedInsideNestedInterpolatedStringLiteral_KeepsNestedLiteral()
        {
            // Verifies that string literals inside interpolation holes do not get rewritten as executable code.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public static class ToolLabel\n" +
                "{\n" +
                "    public static string GetLabel()\n" +
                "    {\n" +
                "        return $\"{Log(\"CustomToolManager\")}\";\n" +
                "    }\n" +
                "\n" +
                "    private static string Log(string value)\n" +
                "    {\n" +
                "        return value;\n" +
                "    }\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("Log(\"CustomToolManager\")"));
            Assert.That(result.Content, Does.Not.Contain("Log(\"io.github.hatayama.UnityCliLoop.Application"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyDomainMetadataIsUsedWithoutRegistrar_RewritesDomainMetadataType()
        {
            // Verifies that metadata helpers split away from registration code keep compiling after migration.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[]"));
            Assert.That(result.Content, Does.Not.Match(@"(?<!\.)\bToolInfo\b"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorHasDescription_RemovesDescriptionArgument()
        {
            // Verifies that old registrar metadata construction keeps compiling after the V3 ToolInfo signature change.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema)
    {
        return new ToolInfo(""hello"", ""description"", schema);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema)"));
            Assert.That(result.Content, Does.Not.Contain("\"description\", schema"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoDescriptionIsRawStringWithComma_RemovesDescriptionArgument()
        {
            // Verifies that commas inside raw string literals do not split legacy ToolInfo arguments.
            string source =
                "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public static class ToolMetadataProvider\n" +
                "{\n" +
                "    public static ToolInfo Create(ToolParameterSchema schema)\n" +
                "    {\n" +
                "        return new ToolInfo(\"hello\", \"\"\"\"say \"hi\", world\"\"\"\", schema);\n" +
                "    }\n" +
                "}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema)"));
            Assert.That(result.Content, Does.Not.Contain("\"\"\"\"say \"hi\", world\"\"\"\""));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorHasDevelopmentFlag_PreservesDevelopmentFlag()
        {
            // Verifies that old registrar metadata visibility keeps compiling after the V3 ToolInfo signature change.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema)
    {
        return new ToolInfo(""hello"", ""description"", schema, true);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema, true)"));
            Assert.That(result.Content, Does.Not.Contain("\"description\", schema"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorUsesDescriptionVariable_RemovesDescriptionArgument()
        {
            // Verifies that non-literal V2 description arguments do not survive into the V3 constructor call.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema parameters, string label)
    {
        return new ToolInfo(""hello"", label, parameters);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", parameters)"));
            Assert.That(result.Content, Does.Not.Contain("label, parameters"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorUsesTypeAlias_RemovesDescriptionArgument()
        {
            // Verifies that constructor migration follows aliases that target the old ToolInfo type.
            string source = @"using LegacyToolInfo = io.github.hatayama.uLoopMCP.ToolInfo;
using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static LegacyToolInfo Create(ToolParameterSchema parameters, string label)
    {
        return new LegacyToolInfo(""hello"", label, parameters);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", parameters)"));
            Assert.That(result.Content, Does.Not.Contain("new LegacyToolInfo(\"hello\", label, parameters)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorUsesSameNameTypeAlias_KeepsAliasIdentifier()
        {
            // Verifies that unqualified type replacement does not rewrite the left side of using aliases.
            string source = @"using ToolInfo = io.github.hatayama.uLoopMCP.ToolInfo;
using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema parameters, string label)
    {
        return new ToolInfo(""hello"", label, parameters);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "using ToolInfo = io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo;"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", parameters)"));
            Assert.That(result.Content, Does.Not.Contain("using io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo ="));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorUsesNamedDescription_RemovesDescriptionArgument()
        {
            // Verifies that named V2 description arguments are removed without reordering supported arguments.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema, string description)
    {
        return new ToolInfo(name: ""hello"", description: description, parameterSchema: schema);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(name: \"hello\", parameterSchema: schema)"));
            Assert.That(result.Content, Does.Not.Contain("description: description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentToolInfoConstructorExistsInMixedFile_KeepsConstructorArguments()
        {
            // Verifies that partially migrated metadata construction is not treated as the removed V2 signature.
            string source = @"using io.github.hatayama.uLoopMCP;
using io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema)
    {
        bool displayDevelopmentOnly = true;
        return new ToolInfo(""hello"", schema, displayDevelopmentOnly);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema, displayDevelopmentOnly)"));
            Assert.That(result.Content, Does.Not.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", displayDevelopmentOnly)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentDomainUsingHasLegacyToolInfoConstructor_RemovesDescriptionArgument()
        {
            // Verifies that partially migrated files still remove the deleted V2 description argument.
            string source = @"using io.github.hatayama.uLoopMCP;
using io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema)
    {
        return new ToolInfo(""hello"", ""description"", schema, true);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema, true)"));
            Assert.That(result.Content, Does.Not.Contain("\"description\""));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentDomainUsingHasLegacyToolInfoDescriptionVariable_RemovesDescriptionArgument()
        {
            // Verifies that mixed partially migrated files do not keep the removed V2 description argument.
            string source = @"using io.github.hatayama.uLoopMCP;
using io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema schema, string description)
    {
        return new ToolInfo(""hello"", description, schema);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema)"));
            Assert.That(result.Content, Does.Not.Contain("description, schema"));
        }

        [Test]
        public void MigrateCSharpSource_WhenCurrentToolInfoConstructorUsesArbitraryVariableNames_KeepsConstructorArguments()
        {
            // Verifies that current V3 metadata construction is not inferred from local variable names.
            string source = @"using io.github.hatayama.uLoopMCP;
using io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static ToolInfo Create(ToolParameterSchema parameters, bool includeDevTools)
    {
        return new ToolInfo(""hello"", parameters, includeDevTools);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", parameters, includeDevTools)"));
            Assert.That(result.Content, Does.Not.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", includeDevTools)"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyFileHasMethodNameMatchingDomainType_KeepsIdentifier()
        {
            // Verifies that method names are not rewritten as migrated type references.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class ToolMetadataProvider
{
    public void ToolInfo()
    {
    }

    public ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public void ToolInfo()"));
            Assert.That(result.Content, Does.Not.Contain("public void io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolInfoConstructorUsesNamespaceAlias_RemovesDescriptionArgument()
        {
            // Verifies that aliased legacy metadata constructors migrate before namespace alias rewrites.
            string source = @"using Old = io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static Old.ToolInfo Create(Old.ToolParameterSchema schema)
    {
        return new Old.ToolInfo(""hello"", ""description"", schema);
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema)"));
            Assert.That(result.Content, Does.Not.Contain("\"description\", schema"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyRegistrarIsUsedThroughNamespaceAlias_RewritesRegistrar()
        {
            // Verifies that namespace aliases do not create invalid qualified registrar names.
            string source = @"using Old = io.github.hatayama.uLoopMCP;

public static class ManualToolRegistration
{
    public static Old.ToolInfo[] Register(Old.IUnityTool tool)
    {
        Old.CustomToolManager.RegisterCustomTool(tool);
        return Old.CustomToolManager.GetRegisteredCustomTools();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.RegisterCustomTool"));
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[]"));
            Assert.That(result.Content, Does.Contain("Old.IUnityCliLoopTool tool"));
            Assert.That(result.Content, Does.Not.Contain("Old.io.github"));
            Assert.That(result.Content, Does.Not.Contain("Old.CustomToolManager"));
            Assert.That(result.Content, Does.Not.Contain("Old.ToolInfo"));
            Assert.That(result.Content, Does.Not.Contain("Old.IUnityTool"));
        }

        [Test]
        public void MigrateCSharpSource_WhenQualifiedUnrelatedTypeNamesExist_KeepsQualifiedTypeNames()
        {
            // Verifies that migration does not rewrite unrelated project types behind another qualifier.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
    private Other.BaseToolResponse response;
    private MyGame.SecuritySettings securitySettings;
}

public sealed class HelloSchema : BaseToolSchema {}

public sealed class HelloResponse : BaseToolResponse {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("Other.BaseToolResponse"));
            Assert.That(result.Content, Does.Contain("MyGame.SecuritySettings"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolSchema"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("Other.UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("MyGame.UnityCliLoopSecuritySetting"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyFileHasUnrelatedToolInfoProperty_KeepsIdentifier()
        {
            // Verifies that metadata type migration does not rewrite member names.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
    public string ToolInfo { get; }
}

public sealed class HelloSchema : BaseToolSchema {}

public sealed class HelloResponse : BaseToolResponse {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public string ToolInfo { get; }"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
            Assert.That(result.Content, Does.Not.Contain("public string io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyFileHasMemberNameMatchingLegacyContractType_KeepsIdentifier()
        {
            // Verifies that contract type migration does not rewrite member names.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : BaseToolSchema
{
    public SecuritySettings SecuritySettings { get; set; }
}

public sealed class HelloResponse : BaseToolResponse {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "public UnityCliLoopSecuritySetting SecuritySettings { get; set; }"));
            Assert.That(result.Content, Does.Not.Contain(
                "UnityCliLoopSecuritySetting UnityCliLoopSecuritySetting"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyContractTypeIsFullyQualified_RewritesFullyQualifiedType()
        {
            // Verifies that qualified legacy namespace usage still migrates after qualified unrelated types are ignored.
            string source = @"public sealed class HelloTool :
    io.github.hatayama.uLoopMCP.AbstractUnityTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloResponse : io.github.hatayama.uLoopMCP.BaseToolResponse {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool<HelloSchema, HelloResponse>"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiExistsOnlyInComment_KeepsContent()
        {
            // Verifies that migration does not rewrite inert documentation comments inside C# files.
            string source = "// AbstractUnityTool should not be rewritten here";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiExistsOnlyInStringLiteral_KeepsContent()
        {
            // Verifies that migration does not rewrite test fixture strings or examples inside C# files.
            string source = "public const string Example = \"IUnityTool\";";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenGenericLegacyNameExistsWithoutLegacyMarker_KeepsContent()
        {
            // Verifies that unrelated project types are not migrated just because their names resemble old API names.
            string source = "public sealed class CustomToolManager { public SecuritySettings Settings; }";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyFileDeclaresCustomToolManagerIdentifier_KeepsIdentifier()
        {
            // Verifies that registrar migration does not rewrite unrelated declaration identifiers.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class CustomToolManager
{
    public void CustomToolManager()
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("public sealed class CustomToolManager"));
            Assert.That(result.Content, Does.Contain("public void CustomToolManager()"));
            Assert.That(result.Content, Does.Not.Contain(
                "public sealed class io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar"));
        }

        [Test]
        public void MigrateCSharpSource_WhenBareMcpToolHasNoLegacyMarker_KeepsContent()
        {
            // Verifies that unrelated attribute types with the same short name do not trigger migration.
            string source = @"using Some.Other.Mcp;

[McpTool]
public sealed class OtherTool
{
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenUnqualifiedLegacyLikeBaseTypeHasNoLegacyMarker_KeepsContent()
        {
            // Verifies that unrelated base types with the same short name do not trigger migration.
            string source = "public sealed class MyResponse : BaseToolResponse {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyNamespacePrefixExists_KeepsContent()
        {
            // Verifies that namespace matching does not treat prefixes as the V2 namespace.
            string source = "using io.github.hatayama.uLoopMCPExtensions;";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenFileReliesOnGlobalUsing_RewritesContractTypes()
        {
            // Verifies that files split away from a legacy global using still migrate inside the same assembly.
            string source = "public sealed class HelloSchema : BaseToolSchema {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolSchema"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenFileReliesOnGlobalUsing_RewritesDomainHelpers()
        {
            // Verifies that split helper files relying on a legacy global using migrate to Domain types.
            string source =
                "public static class ToolHelper { public static ServiceResult<int> Create() => null; }";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> Create"));
        }

        [Test]
        public void ContainsLegacyAssemblyScopedApi_WhenGenericLegacyTypesAreUsed_ReturnsTrue()
        {
            // Verifies that split files using collection-shaped legacy types are migrated with their assembly.
            string source = "public sealed class ToolList { public System.Collections.Generic.List<IUnityTool> Tools; }";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(
                source,
                System.Array.Empty<string>());

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void ContainsLegacyAssemblyScopedApi_WhenLegacyDomainHelpersAreUsed_ReturnsTrue()
        {
            // Verifies that split files using Domain helpers are migrated with their legacy assembly.
            string source = "public static class ToolHelper { public static ServiceResult<int> Create() => null; }";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(
                source,
                System.Array.Empty<string>());

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void ContainsLegacyAssemblyScopedApi_WhenLegacyEditorDelayIsUsed_ReturnsTrue()
        {
            // Verifies that split files using old frame waits are migrated with their legacy assembly.
            string source = "public sealed class Tool { public async Task Run() { await EditorDelay.DelayFrame(1); } }";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(
                source,
                System.Array.Empty<string>());

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void ContainsLegacyAssemblyScopedApi_WhenLegacyEditorWindowCaptureUtilityIsUsed_ReturnsTrue()
        {
            // Verifies that split files using old window capture helpers are migrated with their legacy assembly.
            string source =
                "public sealed class Tool { public async Task Run() { await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct); } }";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(
                source,
                System.Array.Empty<string>());

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyNamespaceLikeTextExists_KeepsContent()
        {
            // Verifies that namespace migration treats dots as literal characters.
            string source = "using ioXgithubYhatayamaZUnityCliLoop;";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacyReferenceIsUsed_RewritesToToolContractsGuid()
        {
            // Verifies that a custom tool asmdef points at the V3 ToolContracts assembly.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""uLoopMCP.Editor""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP.Editor"));
            Assert.That(result.ReplacementCount, Is.EqualTo(1));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacyRuntimeReferenceIsUsed_RewritesToRuntimeGuid()
        {
            // Verifies that old runtime assembly name references keep resolving after the asmdef rename.
            string source = @"{
    ""name"": ""MyCompany.Tools"",
    ""references"": [
        ""uLoopMCP.Runtime""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:c956a21f824994ef087b6de566690b3d"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP.Runtime"));
            Assert.That(result.ReplacementCount, Is.EqualTo(1));
        }

        [Test]
        public void MigrateAsmdefSource_WhenOnlyLegacyGuidIsUsedWithoutLegacySource_KeepsContent()
        {
            // Verifies that package-owned Application references are not rewritten by GUID alone.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacyGuidIsUsedWithLegacySource_RewritesToToolContractsGuid()
        {
            // Verifies that old custom tool assemblies with GUID references are migrated when source confirms old API usage.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacySourceHasNoReferencesArray_AddsToolContractsReference()
        {
            // Verifies that valid minimal asmdefs receive references needed by migrated source files.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor""
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(@"""references"": ["));
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenCurrentToolContractsUsesApplicationGuid_AddsToolContractsGuid()
        {
            // Verifies that partially migrated custom tool assemblies keep current Application refs by GUID.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: true,
                    requiresApplicationReference: false,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenCurrentApplicationGuidAlreadyExists_DoesNotAddDomainReference()
        {
            // Verifies that current V3 Application refs do not look like pending legacy migration.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d"",
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: true,
                    requiresApplicationReference: true,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.Content, Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenManualRegistrationIsUsed_AddsApplicationReference()
        {
            // Verifies that manual registration code keeps the Application assembly when requested.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: true,
                    requiresDomainReference: false,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            Assert.That(result.Content, Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenDomainMetadataIsUsed_AddsDomainReference()
        {
            // Verifies that domain metadata code keeps the Domain assembly when requested.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: true,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            Assert.That(result.Content, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenCurrentDomainMetadataRequiresReference_AddsDomainReference()
        {
            // Verifies that direct V3 Domain consumers receive the Domain assembly reference.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": []
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: false,
                    requiresApplicationReference: false,
                    requiresDomainReference: true,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Not.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            Assert.That(result.Content, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenCurrentReferencesUseAssemblyNames_DoesNotAddDuplicateGuids()
        {
            // Verifies that name-based V3 references are treated as the same assemblies as their GUID references.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""UnityCLILoop.ToolContracts"",
        ""UnityCLILoop.Application"",
        ""UnityCLILoop.Domain""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresToolContractsReference: true,
                    requiresApplicationReference: true,
                    requiresDomainReference: true,
                    requiresFirstPartyScreenshotReference: false);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.Content, Does.Not.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExists_ReturnsTrue()
        {
            // Verifies that migration detection is based on public custom tool API usage.
            string source = "using io.github.hatayama.uLoopMCP;\n" +
                "[McpTool] public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse> {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExistsOnlyInStringLiteral_ReturnsFalse()
        {
            // Verifies that inert fixture text does not trigger project migration UI.
            string source = "public const string Example = \"[McpTool]\";";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExistsOnlyInComment_ReturnsFalse()
        {
            // Verifies that comments do not trigger project migration UI.
            string source = "// CustomToolManager";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void ContainsMigrationCandidateText_WhenPlainSourceExists_ReturnsFalse()
        {
            // Verifies that unrelated source files can skip expensive migration parsing.
            string source = "public sealed class PlainEditorUtility { public int Count; }";

            bool containsCandidateText = ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source);

            Assert.That(containsCandidateText, Is.False);
        }

        [Test]
        public void ContainsMigrationCandidateText_WhenLegacyNamespaceExists_ReturnsTrue()
        {
            // Verifies that old custom tool API source still enters migration parsing.
            string source = "using io.github.hatayama.uLoopMCP;";

            bool containsCandidateText = ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source);

            Assert.That(containsCandidateText, Is.True);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenGenericLegacyNameExistsWithoutLegacyMarker_ReturnsFalse()
        {
            // Verifies that unrelated source names do not trigger project migration UI.
            string source = "public sealed class CustomToolManager {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenOnlyCurrentApiExists_ReturnsFalse()
        {
            // Verifies that already migrated V3 tools are not detected again.
            string source = "[UnityCliLoopTool] public sealed class HelloTool : UnityCliLoopTool<HelloSchema, HelloResponse> {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void GetExcludedDirectoryNames_IncludesUnityGeneratedDirectories()
        {
            // Verifies that generated Unity folders are skipped during project-wide scans.
            string[] names = ThirdPartyToolMigrationRules.GetExcludedDirectoryNames();

            Assert.That(names.Contains("Library"), Is.True);
            Assert.That(names.Contains("Temp"), Is.True);
            Assert.That(names.Contains(".git"), Is.True);
        }
    }
}
