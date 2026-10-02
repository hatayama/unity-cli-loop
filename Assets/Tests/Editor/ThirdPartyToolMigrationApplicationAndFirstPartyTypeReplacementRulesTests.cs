using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies qualified Application and first-party screenshot type rewrites to the ToolContracts namespace.
    /// </summary>
    public sealed class ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRulesTests
    {
        /// <summary>
        /// Verifies that a legacy fully qualified Application type is rewritten to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyApplicationTypeNamesInCode_WhenTypeIsLegacyQualified_RewritesToToolContracts()
        {
            const string source = "io.github.hatayama.uLoopMCP.SwitchToMainThreadAwaitable awaitable;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyApplicationTypeNamesInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.SwitchToMainThreadAwaitable awaitable;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an Application type qualified with the current Application namespace is moved to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyApplicationTypeNamesInCode_WhenTypeIsCurrentApplicationQualified_RewritesToToolContracts()
        {
            const string source = "io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher switcher;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyApplicationTypeNamesInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher switcher;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an Application type referenced through a legacy namespace alias is rewritten to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyApplicationTypeNamesInCode_WhenTypeUsesLegacyAlias_RewritesToToolContracts()
        {
            const string source = "Legacy.MainThreadSwitcher switcher;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyApplicationTypeNamesInCode(
                    source,
                    new[] { "Legacy" },
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher switcher;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a legacy fully qualified screenshot type is rewritten to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyFirstPartyScreenshotTypeNamesInCode_WhenTypeIsLegacyQualified_RewritesToToolContracts()
        {
            const string source = "io.github.hatayama.uLoopMCP.WindowMatchMode mode;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(content, Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.WindowMatchMode mode;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a screenshot type referenced through a legacy namespace alias is rewritten to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyFirstPartyScreenshotTypeNamesInCode_WhenTypeUsesLegacyAlias_RewritesToToolContracts()
        {
            const string source = "Legacy.CaptureMode mode;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
                    source,
                    new[] { "Legacy" },
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(content, Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.CaptureMode mode;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a screenshot type qualified with the current FirstPartyTools namespace is moved to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyFirstPartyScreenshotTypeNamesInCode_WhenTypeIsCurrentFirstPartyQualified_RewritesToToolContracts()
        {
            const string source = "io.github.hatayama.UnityCliLoop.FirstPartyTools.ScreenshotSchema schema;";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules.ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
                    source,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    false,
                    Array.Empty<string>(),
                    ref replacementCount);

            Assert.That(content, Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotSchema schema;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
