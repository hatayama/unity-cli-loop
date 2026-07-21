using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterization tests that pin Domain migration rewrite entry points before Extract Class.
    /// </summary>
    public sealed class ThirdPartyToolMigrationDomainRewriteCharacterizationTests
    {
        [Test]
        public void MigrateCSharpSource_WhenLegacyRegistrarIsUsed_RewritesToCurrentRegistrar()
        {
            // Pins the CSharpRules orchestration entry for a bare legacy registrar reference.
            string source =
                "using io.github.hatayama.uLoopMCP;\n" +
                "public class Sample {\n" +
                "  public void Run() { CustomToolManager.Register(); }\n" +
                "}\n";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationCSharpRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("UnityCliLoopToolRegistrar"));
            Assert.That(result.Content, Does.Not.Contain("CustomToolManager"));
            Assert.That(result.ReplacementCount, Is.GreaterThan(0));
        }

        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenBareCaptureWindowLacksLegacyContext_DoesNotRewrite()
        {
            // why: bare CaptureWindow migration requires legacy assembly/alias context; without it the orchestrator records zero replacements
            string source =
                "public class Sample {\n" +
                "  public async void Run() {\n" +
                "    await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n" +
                "  }\n" +
                "}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationScreenshotRules.ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    canMigrateBareLegacyEditorWindowCaptureUtility: false,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout: false,
                    canPreserveBareCurrentToolContractsReferences: false,
                    canUseBareCurrentFirstPartyTools: false,
                    assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(replacementCount, Is.EqualTo(0));
            Assert.That(content, Is.EqualTo(source));
        }

        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenLegacyQualifiedCaptureWindowExists_RewritesCall()
        {
            // Pins ScreenshotRules orchestration for a legacy-namespace-qualified CaptureWindowAsync call.
            string source =
                "using System.Threading.Tasks;\n" +
                "using UnityEditor;\n" +
                "using UnityEngine;\n" +
                "public class Sample {\n" +
                "  public async Task<Texture2D> Run(EditorWindow window, System.Threading.CancellationToken ct) {\n" +
                "    return await io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n" +
                "  }\n" +
                "}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationScreenshotRules.ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    canMigrateBareLegacyEditorWindowCaptureUtility: true,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout: true,
                    canPreserveBareCurrentToolContractsReferences: false,
                    canUseBareCurrentFirstPartyTools: false,
                    assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(replacementCount, Is.GreaterThan(0));
            Assert.That(content, Does.Contain("CaptureWindow"));
            Assert.That(content, Does.Not.Contain("io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility"));
        }

        [Test]
        public void ShouldMigrateLegacyTypeReference_WhenIdentifierIsMethodInvocation_ReturnsFalse()
        {
            // Pins TypeReplacementRules guard that skips method-like ToolInfo(...) without new.
            string source = "var x = ToolInfo(name);";
            int index = source.IndexOf("ToolInfo", StringComparison.Ordinal);

            bool shouldMigrate = ThirdPartyToolMigrationTypeReplacementRules.ShouldMigrateLegacyTypeReference(
                source,
                "ToolInfo",
                index);

            Assert.That(shouldMigrate, Is.False);
        }

        [Test]
        public void ShouldMigrateLegacyTypeReference_WhenIdentifierIsVariableDeclaration_ReturnsTrue()
        {
            // why: current guard treats "ToolInfo info = null;" as migratable (declaration terminator check does not reject this shape)
            string source = "ToolInfo info = null;";
            int index = source.IndexOf("ToolInfo", StringComparison.Ordinal);

            bool shouldMigrate = ThirdPartyToolMigrationTypeReplacementRules.ShouldMigrateLegacyTypeReference(
                source,
                "ToolInfo",
                index);

            Assert.That(shouldMigrate, Is.True);
        }

        [Test]
        public void ShouldMigrateLegacyToolInfoTypeReference_WhenNewExpression_ReturnsTrue()
        {
            // Pins ToolInfo-specific migration eligibility for constructor usage.
            string source = "var info = new ToolInfo(\"a\", \"b\");";
            int index = source.IndexOf("ToolInfo", StringComparison.Ordinal);

            bool shouldMigrate =
                ThirdPartyToolMigrationTypeReplacementRules.ShouldMigrateLegacyToolInfoTypeReference(
                    source,
                    index);

            Assert.That(shouldMigrate, Is.True);
        }

        [Test]
        public void ReplaceLegacyRegistrarAliasesInCode_WhenAliasQualifiedRegistrarExists_RewritesAlias()
        {
            // Pins TypeReplacementRules alias registrar rewrite against the current public contract namespace.
            string source = "Legacy.CustomToolManager.Register();";
            int replacementCount = 0;

            string migrated = ThirdPartyToolMigrationTypeReplacementRules.ReplaceLegacyRegistrarAliasesInCode(
                source,
                new[] { "Legacy" },
                ref replacementCount);

            Assert.That(replacementCount, Is.GreaterThan(0));
            Assert.That(
                migrated,
                Does.Contain($"{ThirdPartyToolMigrationRuleCatalog.CurrentNamespace}.UnityCliLoopToolRegistrar"));
        }

        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenAlreadyMigratedFileHasHidingAutoProperty_RemovesDeclarationAndKeepsConstructorAssignment()
        {
            // Pins removal of a real-world-shaped Success auto-property (attributes + XML doc + get-only) on a file already on UnityCliLoopToolResponse.
            string source =
                "using Newtonsoft.Json;\n" +
                "using io.github.hatayama.UnityCliLoop.ToolContracts;\n" +
                "\n" +
                "public sealed class SampleResponse : UnityCliLoopToolResponse\n" +
                "{\n" +
                "    /// <summary>\n" +
                "    /// Whether the operation succeeded.\n" +
                "    /// </summary>\n" +
                "    [JsonProperty(\"success\")]\n" +
                "    public bool Success { get; }\n" +
                "\n" +
                "    public SampleResponse(bool success)\n" +
                "    {\n" +
                "        Success = success;\n" +
                "    }\n" +
                "}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(replacementCount, Is.GreaterThan(0));
            Assert.That(content, Does.Not.Contain("public bool Success { get; }"));
            Assert.That(content, Does.Not.Contain("[JsonProperty(\"success\")]"));
            Assert.That(content, Does.Not.Contain("Whether the operation succeeded."));
            Assert.That(content, Does.Contain("Success = success;"));
        }

        [Test]
        public void MigrateCSharpSource_WhenV2ResponseHasHidingSuccessProperty_ReplacesBaseTypeAndRemovesSuccessDeclaration()
        {
            // Pins the full orchestration: BaseToolResponse rename and hiding Success removal happen together for V2 sources.
            string source =
                "using io.github.hatayama.uLoopMCP;\n" +
                "\n" +
                "public sealed class LegacyResponse : BaseToolResponse\n" +
                "{\n" +
                "    public bool Success { get; set; }\n" +
                "}\n";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationCSharpRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("BaseToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("public bool Success"));
            Assert.That(result.ReplacementCount, Is.GreaterThan(0));
        }

        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenSuccessGetterHasLogic_DoesNotRewriteButIsDetectable()
        {
            // why: a getter with logic cannot be safely auto-rewritten; it must stay untouched but remain detectable so it can be surfaced.
            string source =
                "using io.github.hatayama.UnityCliLoop.ToolContracts;\n" +
                "\n" +
                "public sealed class CustomLogicResponse : UnityCliLoopToolResponse\n" +
                "{\n" +
                "    private readonly bool _succeeded;\n" +
                "\n" +
                "    public bool Success { get { return _succeeded && true; } }\n" +
                "}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);
            bool isDetectableAsNonAutoHiding =
                ThirdPartyToolMigrationSuccessPropertyRules.ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(
                    source);

            Assert.That(replacementCount, Is.EqualTo(0));
            Assert.That(content, Is.EqualTo(source));
            Assert.That(isDetectableAsNonAutoHiding, Is.True);
        }

        [Test]
        public void ContainsSuccessPropertyHidingUnityCliLoopToolResponse_WhenAlreadyMigratedFileOnlyHasSuccessHiding_ReturnsTrue()
        {
            // Pins detection extension: a file with no remaining legacy API but a hiding Success auto-property is still a migration target.
            string source =
                "using io.github.hatayama.UnityCliLoop.ToolContracts;\n" +
                "\n" +
                "public sealed class AlreadyMigratedResponse : UnityCliLoopToolResponse\n" +
                "{\n" +
                "    public bool Success { get; }\n" +
                "}\n";

            bool containsTarget =
                ThirdPartyToolMigrationSuccessPropertyRules.ContainsSuccessPropertyHidingUnityCliLoopToolResponse(
                    source);

            Assert.That(containsTarget, Is.True);
        }
    }
}
