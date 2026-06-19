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
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>());

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
                "return (await io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
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
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "return (await io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
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
                "return (await io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)).texture;"));
            Assert.That(result.Content, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
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
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.WindowMatchMode MatchMode"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.WindowMatchMode.contains"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.CaptureMode CaptureMode"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.CaptureMode.rendering"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.FirstPartyTools.ScreenshotInfo()"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo()"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.FindWindowsByName"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.GetOpenWindowNames"));
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
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync"));
            Assert.That(result.Content, Does.Contain("private UIElementInfo CreateElementInfo()"));
            Assert.That(result.Content, Does.Contain("List<UIElementInfo> clickableElements"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLocalUIElementInfoWasAlreadyQualified_RepairsLocalReferences()
        {
            // Verifies that rerunning migration repairs local DTO references qualified by an older migration pass.
            string source = @"using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class LocalTool : UnityCliLoopTool<LocalSchema, LocalResponse>
{
    private List<io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo> Classify()
    {
        List<io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo> elements = new();
        elements.Add(CreateElementInfo());
        return elements;
    }

    private io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo CreateElementInfo()
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
            Assert.That(result.Content, Does.Contain("private List<UIElementInfo> Classify()"));
            Assert.That(result.Content, Does.Contain("List<UIElementInfo> elements = new();"));
            Assert.That(result.Content, Does.Contain("private UIElementInfo CreateElementInfo()"));
            Assert.That(result.Content, Does.Not.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.UIElementInfo"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyGameRenderingCaptureIsUsed_RewritesSignatureAndDiscard()
        {
            // Verifies that old rendering capture deconstruction keeps compiling after the V3 timeout result.
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
            Assert.That(result.Content, Does.Contain("(texture, yOffset, _) = await"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)"));
            Assert.That(result.Content, Does.Not.Contain("CaptureGameRenderingAsync(1.0f, ct)"));
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
        SwitchToMainThreadAwaitable awaitable = MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update);
        bool isMainThread = MainThreadSwitcher.IsMainThread;
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread(ct);"));
            Assert.That(result.Content, Does.Contain(
                "await io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread(ct: ct);"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Application.SwitchToMainThreadAwaitable awaitable = io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread();"));
            Assert.That(result.Content, Does.Contain(
                "bool isMainThread = io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.IsMainThread;"));
            Assert.That(result.Content, Does.Not.Contain("PlayerLoopTiming"));
            Assert.That(result.Content, Does.Not.Contain("cancellationToken:"));
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
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.Domain.ToolInfo[]"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
                "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int> CreateResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int>.SuccessResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Domain.ToolSettingsCatalogItem[] GetCatalog"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.Domain.ToolSettingsCatalogItem[0]"));
            Assert.That(result.Content, Does.Not.Contain("ToolContracts.ServiceResult"));
            Assert.That(result.Content, Does.Not.Contain("ToolContracts.ToolSettingsCatalogItem"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolSettingsCatalogItem(\"hello\", displayDevelopmentOnly, isThirdParty)"));
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
                "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int> CreateResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int>.SuccessResult"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Domain.ToolSettingsCatalogItem[] GetCatalog"));
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
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.Domain.ToolInfo[]"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema, true)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", parameters)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", parameters)"));
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
                "using ToolInfo = io.github.hatayama.UnityCliLoop.Domain.ToolInfo;"));
            Assert.That(result.Content, Does.Contain(
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", parameters)"));
            Assert.That(result.Content, Does.Not.Contain("using io.github.hatayama.UnityCliLoop.Domain.ToolInfo ="));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(name: \"hello\", parameterSchema: schema)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema, displayDevelopmentOnly)"));
            Assert.That(result.Content, Does.Not.Contain(
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", displayDevelopmentOnly)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema, true)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema)"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", parameters, includeDevTools)"));
            Assert.That(result.Content, Does.Not.Contain(
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", includeDevTools)"));
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
            Assert.That(result.Content, Does.Not.Contain("public void io.github.hatayama.UnityCliLoop.Domain.ToolInfo"));
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
                "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema)"));
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
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.RegisterCustomTool"));
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.Domain.ToolInfo[]"));
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
            Assert.That(result.Content, Does.Not.Contain("public string io.github.hatayama.UnityCliLoop.Domain.ToolInfo"));
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
                "public sealed class io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar"));
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
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>());

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
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>());

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int> Create"));
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
        public void MigrateAsmdefSource_WhenManualRegistrationIsUsed_KeepsApplicationReference()
        {
            // Verifies that migrated manual registration code can reference the V3 registrar assembly.
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
            // Verifies that ToolInfo-only helper assemblies can resolve the V3 Domain metadata type.
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
        public void MigrateAsmdefSource_WhenCurrentDomainMetadataRequiresDomainReference_AddsToolContractsReference()
        {
            // Verifies that direct V3 Domain consumers also receive transitive ToolContracts access.
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
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
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
