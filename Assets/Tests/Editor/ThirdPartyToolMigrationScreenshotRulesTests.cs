using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the screenshot migration orchestrator rewrites legacy CaptureGameRenderingAsync call sites.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScreenshotRulesTests
    {
        private const string LegacyCaptureUtility = "io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility";
        private const string MigratedCaptureUtility = "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility";
        private const string MigratedTimeout =
            "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS";
        private const string LegacyTupleProjection =
            ".ContinueWith(__unityCliLoopRenderingTask => (__unityCliLoopRenderingTask.GetAwaiter().GetResult().texture, " +
            "__unityCliLoopRenderingTask.GetAwaiter().GetResult().yOffset))";

        /// <summary>
        /// Verifies that an awaited CaptureGameRenderingAsync call without a closing parenthesis is left untouched while an earlier complete call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenAwaitedGameRenderingCallIsUnterminated_RewritesOnlyCompleteCall()
        {
            string source =
                "object first = await " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(1.0f, ct);\n" +
                "Use(await " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(2.0f, ct";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "object first = await " + MigratedCaptureUtility + ".CaptureGameRenderingAsync(1.0f, " +
                    MigratedTimeout + ", ct)" + LegacyTupleProjection + ";\n" +
                    "Use(await " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(2.0f, ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a non-awaited CaptureGameRenderingAsync call without a closing parenthesis is left untouched while an earlier complete call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenTaskGameRenderingCallIsUnterminated_RewritesOnlyCompleteCall()
        {
            string source =
                "Task first = " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(1.0f, ct);\n" +
                "Task second = " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(2.0f, ct";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "Task first = " + MigratedCaptureUtility + ".CaptureGameRenderingAsync(1.0f, " +
                    MigratedTimeout + ", ct)" + LegacyTupleProjection + ";\n" +
                    "Task second = " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(2.0f, ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a non-awaited CaptureGameRenderingAsync call that already passes three arguments is not rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenTaskGameRenderingCallHasThreeArguments_LeavesSourceUnchanged()
        {
            string source =
                "Task capture = " + LegacyCaptureUtility + ".CaptureGameRenderingAsync(1.0f, timeout, ct);\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        private static (string Content, int ReplacementCount) Migrate(string source)
        {
            return ThirdPartyToolMigrationScreenshotRules.ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                source,
                Array.Empty<string>(),
                Array.Empty<string>(),
                canMigrateBareLegacyEditorWindowCaptureUtility: false,
                shouldQualifyBareEditorWindowCaptureUtilityTimeout: false,
                canPreserveBareCurrentToolContractsReferences: false,
                canUseBareCurrentFirstPartyTools: false,
                assemblyDeclaredTypeNames: Array.Empty<string>());
        }
    }
}
