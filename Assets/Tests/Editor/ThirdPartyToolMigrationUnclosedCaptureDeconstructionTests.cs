using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies screenshot deconstruction migration leaves a CaptureGameRenderingAsync call alone
    /// when its argument list is never closed, because the rewrite cannot tell what the call returns.
    /// </summary>
    public sealed class ThirdPartyToolMigrationUnclosedCaptureDeconstructionTests
    {
        private const string UnclosedDeconstruction =
            "(Texture2D texture, int yOffset) = await " +
            "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility" +
            ".CaptureGameRenderingAsync(1.0f, timeout, ct";

        /// <summary>
        /// Verifies that an unclosed CaptureGameRenderingAsync deconstruction is not reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsCurrentCaptureGameRenderingDeconstructionMigration_WhenCallIsUnclosed_ReturnsFalse()
        {
            bool result = ThirdPartyToolMigrationScreenshotDeconstructionRules
                .ContainsCurrentCaptureGameRenderingDeconstructionMigration(
                    UnclosedDeconstruction,
                    canUseBareCurrentFirstPartyTools: false,
                    currentFirstPartyToolsNamespaceAliases: Array.Empty<string>(),
                    assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that an unclosed CaptureGameRenderingAsync deconstruction gets no discard and is not counted.
        /// </summary>
        [Test]
        public void AddDiscardToCaptureGameRenderingDeconstructionsInCode_WhenCallIsUnclosed_LeavesSourceUnchanged()
        {
            int replacementCount = 0;

            string content = ThirdPartyToolMigrationScreenshotDeconstructionRules
                .AddDiscardToCaptureGameRenderingDeconstructionsInCode(
                    UnclosedDeconstruction,
                    canUseBareCurrentFirstPartyTools: false,
                    currentFirstPartyToolsNamespaceAliases: Array.Empty<string>(),
                    assemblyDeclaredTypeNames: Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(content, Is.EqualTo(UnclosedDeconstruction));
            Assert.That(replacementCount, Is.EqualTo(0));
        }
    }
}
