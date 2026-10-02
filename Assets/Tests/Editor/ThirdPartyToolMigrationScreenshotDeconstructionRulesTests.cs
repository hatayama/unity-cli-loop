using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies CaptureGameRenderingAsync deconstruction detection, texture extraction, and ConfigureAwait suffix parsing used by screenshot migration.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScreenshotDeconstructionRulesTests
    {
        private const string LegacyCaptureUtility = "io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility";
        private const string CurrentCaptureUtility = "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility";
        private const string MigratedCaptureUtility = "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility";
        private const string MigratedTimeout =
            "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS";
        private const string TwoItemDeconstruction =
            "(Texture2D texture, int yOffset) = await " + CurrentCaptureUtility +
            ".CaptureGameRenderingAsync(1.0f, timeout, ct);\n";

        /// <summary>
        /// Verifies that a deconstruction inside a line comment does not get a discard added while the code deconstruction does.
        /// </summary>
        [Test]
        public void AddDiscardToCaptureGameRenderingDeconstructionsInCode_WhenDeconstructionIsCommentedOut_SkipsComment()
        {
            string source = "// " + TwoItemDeconstruction + TwoItemDeconstruction;
            int replacementCount = 0;

            string content = ThirdPartyToolMigrationScreenshotDeconstructionRules.AddDiscardToCaptureGameRenderingDeconstructionsInCode(
                source,
                canUseBareCurrentFirstPartyTools: false,
                currentFirstPartyToolsNamespaceAliases: Array.Empty<string>(),
                assemblyDeclaredTypeNames: Array.Empty<string>(),
                ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    "// " + TwoItemDeconstruction +
                    "(Texture2D texture, int yOffset, _) = await " + CurrentCaptureUtility +
                    ".CaptureGameRenderingAsync(1.0f, timeout, ct);\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a qualified two-item CaptureGameRenderingAsync deconstruction is reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenQualifiedTwoItemDeconstruction_ReturnsTrue()
        {
            bool result = ContainsDeconstructionMigration(TwoItemDeconstruction, Array.Empty<string>());

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a deconstruction that only appears inside a comment is not reported.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenDeconstructionIsCommentedOut_ReturnsFalse()
        {
            bool result = ContainsDeconstructionMigration("// " + TwoItemDeconstruction, Array.Empty<string>());

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a deconstruction qualified with an alias that is not a current FirstPartyTools alias is not reported.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenAliasIsNotCurrentFirstPartyTools_ReturnsFalse()
        {
            string source =
                "(Texture2D texture, int yOffset) = await Other.EditorWindowCaptureUtility.CaptureGameRenderingAsync(1.0f, timeout, ct);\n";

            bool result = ContainsDeconstructionMigration(source, new[] { "Tools" });

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a deconstruction of a call already projected to the legacy tuple through ContinueWith is not reported.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenCallIsAlreadyProjected_ReturnsFalse()
        {
            string source =
                "(Texture2D texture, int yOffset) = await " + CurrentCaptureUtility +
                ".CaptureGameRenderingAsync(1.0f, timeout, ct)" +
                ".ContinueWith(__unityCliLoopRenderingTask => (__unityCliLoopRenderingTask.GetAwaiter().GetResult().texture, " +
                "__unityCliLoopRenderingTask.GetAwaiter().GetResult().yOffset));\n";

            bool result = ContainsDeconstructionMigration(source, Array.Empty<string>());

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a deconstruction that already has three items is not reported.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenDeconstructionHasThreeItems_ReturnsFalse()
        {
            string source =
                "(Texture2D texture, int yOffset, _) = await " + CurrentCaptureUtility +
                ".CaptureGameRenderingAsync(1.0f, timeout, ct);\n";

            bool result = ContainsDeconstructionMigration(source, Array.Empty<string>());

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that an awaited CaptureWindowAsync call used as a method argument is wrapped so that only the texture is passed on.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenCaptureWindowIsUsedAsArgument_ExtractsTexture()
        {
            string source = "Use(await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct));\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "Use((await " + MigratedCaptureUtility + ".CaptureWindowAsync(window, 1.0f, " + MigratedTimeout +
                    ", ct)).texture);\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a ConfigureAwait call on the following line stays inside the texture-extracting parentheses.
        /// </summary>
        [Test]
        public void ReplaceLegacyEditorWindowCaptureUtilityCallsInCode_WhenConfigureAwaitFollowsOnNextLine_KeepsItInsideExtraction()
        {
            string source =
                "return await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct)\n" +
                "    .ConfigureAwait(false);\n";

            (string content, int replacementCount) = Migrate(source);

            Assert.That(
                content,
                Is.EqualTo(
                    "return (await " + MigratedCaptureUtility + ".CaptureWindowAsync(window, 1.0f, " + MigratedTimeout +
                    ", ct)\n    .ConfigureAwait(false)).texture;\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that whitespace between ConfigureAwait and its argument list is included in the suffix.
        /// </summary>
        [Test]
        public void ReadOptionalConfigureAwaitSuffix_WhenWhitespacePrecedesArgumentList_ReturnsWholeSuffix()
        {
            string source = "capture.ConfigureAwait (false);";
            int startIndex = "capture".Length;

            (string suffix, int endIndex) = ReadSuffix(source, startIndex);

            Assert.That(suffix, Is.EqualTo(".ConfigureAwait (false)"));
            Assert.That(endIndex, Is.EqualTo(source.Length - 1));
        }

        /// <summary>
        /// Verifies that a member access truncated before the full ConfigureAwait name yields no suffix.
        /// </summary>
        [Test]
        public void ReadOptionalConfigureAwaitSuffix_WhenSourceEndsBeforeMemberName_ReturnsNoSuffix()
        {
            string source = "capture.Configure";
            int startIndex = "capture".Length;

            (string suffix, int endIndex) = ReadSuffix(source, startIndex);

            Assert.That(suffix, Is.EqualTo(string.Empty));
            Assert.That(endIndex, Is.EqualTo(startIndex));
        }

        /// <summary>
        /// Verifies that a member whose name differs from ConfigureAwait only by case is not treated as ConfigureAwait.
        /// </summary>
        [Test]
        public void ReadOptionalConfigureAwaitSuffix_WhenMemberNameDiffersByCase_ReturnsNoSuffix()
        {
            string source = "capture.configureAwait(false);";
            int startIndex = "capture".Length;

            (string suffix, int endIndex) = ReadSuffix(source, startIndex);

            Assert.That(suffix, Is.EqualTo(string.Empty));
            Assert.That(endIndex, Is.EqualTo(startIndex));
        }

        /// <summary>
        /// Verifies that a longer member name starting with ConfigureAwait is not treated as a ConfigureAwait call.
        /// </summary>
        [Test]
        public void ReadOptionalConfigureAwaitSuffix_WhenMemberNameOnlyStartsWithConfigureAwait_ReturnsNoSuffix()
        {
            string source = "Use(capture.ConfigureAwaitFlag, Next());";
            int startIndex = "Use(capture".Length;

            (string suffix, int endIndex) = ReadSuffix(source, startIndex);

            Assert.That(suffix, Is.EqualTo(string.Empty));
            Assert.That(endIndex, Is.EqualTo(startIndex));
        }

        /// <summary>
        /// Verifies that a ConfigureAwait call without a closing parenthesis yields no suffix.
        /// </summary>
        [Test]
        public void ReadOptionalConfigureAwaitSuffix_WhenArgumentListIsUnterminated_ReturnsNoSuffix()
        {
            string source = "capture.ConfigureAwait(false";
            int startIndex = "capture".Length;

            (string suffix, int endIndex) = ReadSuffix(source, startIndex);

            Assert.That(suffix, Is.EqualTo(string.Empty));
            Assert.That(endIndex, Is.EqualTo(startIndex));
        }

        private static bool ContainsDeconstructionMigration(string source, string[] currentFirstPartyToolsNamespaceAliases)
        {
            return ThirdPartyToolMigrationScreenshotDeconstructionRules.ContainsCurrentCaptureGameRenderingDeconstructionMigration(
                source,
                canUseBareCurrentFirstPartyTools: false,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames: Array.Empty<string>());
        }

        private static (string Suffix, int EndIndex) ReadSuffix(string source, int startIndex)
        {
            return ThirdPartyToolMigrationScreenshotDeconstructionRules.ReadOptionalConfigureAwaitSuffix(
                source,
                CodeTextMask.Create(source),
                startIndex);
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
