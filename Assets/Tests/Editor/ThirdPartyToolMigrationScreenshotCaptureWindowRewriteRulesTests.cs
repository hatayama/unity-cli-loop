using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how legacy EditorWindowCaptureUtility.CaptureWindowAsync call sites are rewritten during migration.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScreenshotCaptureWindowRewriteRulesTests
    {
        private const string LegacyCaptureUtility = "io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility";
        private const string MigratedCaptureUtility = "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility";
        private const string MigratedTimeout =
            "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS";

        /// <summary>
        /// Verifies that an awaited CaptureWindowAsync call without a closing parenthesis is left untouched while an earlier complete call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenAwaitedCaptureWindowCallIsUnterminated_RewritesOnlyCompleteCall()
        {
            string source =
                "Texture2D first = await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct);\n" +
                "Use(await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 2.0f, ct";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "Texture2D first = (await " + MigratedCaptureUtility + ".CaptureWindowAsync(window, 1.0f, " +
                    MigratedTimeout + ", ct)).texture;\n" +
                    "Use(await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 2.0f, ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an awaited CaptureWindowAsync call with only two arguments is not rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenAwaitedCaptureWindowCallHasTwoArguments_LeavesSourceUnchanged()
        {
            string source = "Texture2D texture = await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f);\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a non-awaited CaptureWindowAsync call without a closing parenthesis is left untouched while an earlier complete call is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenTaskCaptureWindowCallIsUnterminated_RewritesOnlyCompleteCall()
        {
            string source =
                "Task first = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct);\n" +
                "Task second = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 2.0f, ct";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "Task first = " + MigratedCaptureUtility + ".CaptureWindowAsync(window, 1.0f, " + MigratedTimeout +
                    ", ct).ContinueWith(__unityCliLoopCaptureTask => __unityCliLoopCaptureTask.GetAwaiter().GetResult().texture);\n" +
                    "Task second = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 2.0f, ct"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a non-awaited CaptureWindowAsync call with only two arguments is not rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenTaskCaptureWindowCallHasTwoArguments_LeavesSourceUnchanged()
        {
            string source = "Task capture = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f);\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a CaptureWindowAsync call qualified with the current FirstPartyTools namespace keeps that qualifier when rewritten.
        /// </summary>
        [TestCase("io.github.hatayama.UnityCliLoop.FirstPartyTools.")]
        [TestCase("global::io.github.hatayama.UnityCliLoop.FirstPartyTools.")]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenQualifiedWithCurrentFirstPartyTools_KeepsQualifier(
            string qualifier)
        {
            string source =
                "return await " + qualifier + "EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "return (await " + qualifier + "EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, " +
                    MigratedTimeout + ", ct)).texture;\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
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
