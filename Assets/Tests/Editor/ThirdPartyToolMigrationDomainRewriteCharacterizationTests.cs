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
    }
}
