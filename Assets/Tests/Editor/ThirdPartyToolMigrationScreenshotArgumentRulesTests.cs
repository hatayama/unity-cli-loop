using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies argument reordering for migrated EditorWindowCaptureUtility calls and legacy ToolInfo constructor matching.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScreenshotArgumentRulesTests
    {
        private const string Timeout = "TIMEOUT";

        /// <summary>
        /// Verifies that a CaptureWindowAsync argument list naming the same parameter twice cannot be migrated.
        /// </summary>
        [Test]
        public void GetMigratedEditorWindowCaptureUtilityArguments_WhenNamedArgumentIsDuplicated_ReturnsEmpty()
        {
            string[] result = ThirdPartyToolMigrationScreenshotArgumentRules.GetMigratedEditorWindowCaptureUtilityArguments(
                new[] { "window: first", "window: second", "ct" },
                Timeout);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that a leading named ct argument is moved to the last slot and the positional arguments fill the earlier slots.
        /// </summary>
        [Test]
        public void GetMigratedEditorWindowCaptureUtilityArguments_WhenCurrentCancellationTokenNameComesFirst_MovesItToTheEnd()
        {
            string[] result = ThirdPartyToolMigrationScreenshotArgumentRules.GetMigratedEditorWindowCaptureUtilityArguments(
                new[] { "ct: token", "window", "1.0f" },
                Timeout);

            Assert.That(result, Is.EqualTo(new[] { "window", "1.0f", Timeout, "ct: token" }));
        }

        /// <summary>
        /// Verifies that positional arguments after a leading named window argument skip the slot that window already occupies.
        /// </summary>
        [Test]
        public void GetMigratedEditorWindowCaptureUtilityArguments_WhenWindowIsNamedFirst_PlacesPositionalArgumentsAfterIt()
        {
            string[] result = ThirdPartyToolMigrationScreenshotArgumentRules.GetMigratedEditorWindowCaptureUtilityArguments(
                new[] { "window: target", "1.0f", "token" },
                Timeout);

            Assert.That(result, Is.EqualTo(new[] { "target", "1.0f", Timeout, "token" }));
        }

        /// <summary>
        /// Verifies that more positional arguments than CaptureWindowAsync slots cannot be ordered.
        /// </summary>
        [Test]
        public void GetOrderedEditorWindowCaptureUtilityArguments_WhenMorePositionalArgumentsThanSlots_ReturnsEmpty()
        {
            string[] result = ThirdPartyToolMigrationScreenshotArgumentRules.GetOrderedEditorWindowCaptureUtilityArguments(
                new[] { "window", "1.0f", "token", "extra" });

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that ordering fails when a named CaptureWindowAsync argument is repeated, even if the remaining positional arguments could fill every slot.
        /// </summary>
        [Test]
        public void GetOrderedEditorWindowCaptureUtilityArguments_WhenNamedArgumentIsDuplicated_ReturnsEmpty()
        {
            string[] result = ThirdPartyToolMigrationScreenshotArgumentRules.GetOrderedEditorWindowCaptureUtilityArguments(
                new[] { "window: first", "window: second", "1.0f", "token" });

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that a CaptureGameRenderingAsync argument list naming resolutionScale twice cannot be migrated.
        /// </summary>
        [Test]
        public void GetMigratedEditorWindowCaptureUtilityGameRenderingArguments_WhenNamedArgumentIsDuplicated_ReturnsEmpty()
        {
            string[] result =
                ThirdPartyToolMigrationScreenshotArgumentRules.GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(
                    new[] { "resolutionScale: 1.0f", "resolutionScale: 2.0f" },
                    Timeout);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that ordering fails when resolutionScale is named twice, even if a remaining positional argument could fill the last slot.
        /// </summary>
        [Test]
        public void GetOrderedEditorWindowCaptureUtilityGameRenderingArguments_WhenNamedArgumentIsDuplicated_ReturnsEmpty()
        {
            string[] result =
                ThirdPartyToolMigrationScreenshotArgumentRules.GetOrderedEditorWindowCaptureUtilityGameRenderingArguments(
                    new[] { "resolutionScale: 1.0f", "resolutionScale: 2.0f", "token" });

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that more positional arguments than CaptureGameRenderingAsync slots cannot be ordered.
        /// </summary>
        [Test]
        public void GetOrderedEditorWindowCaptureUtilityGameRenderingArguments_WhenMorePositionalArgumentsThanSlots_ReturnsEmpty()
        {
            string[] result =
                ThirdPartyToolMigrationScreenshotArgumentRules.GetOrderedEditorWindowCaptureUtilityGameRenderingArguments(
                    new[] { "1.0f", "token", "extra" });

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies that a legacy cancellationToken named argument is renamed to ct.
        /// </summary>
        [Test]
        public void GetArgumentWithMigratedCancellationTokenName_WhenLegacyNameIsUsed_RenamesToCurrentName()
        {
            string result =
                ThirdPartyToolMigrationScreenshotArgumentRules.GetArgumentWithMigratedCancellationTokenName(
                    "cancellationToken: token");

            Assert.That(result, Is.EqualTo("ct: token"));
        }

        /// <summary>
        /// Verifies that a ToolInfo constructor qualified with the legacy namespace is migrated even when bare ToolInfo migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolInfoConstructorsInCode_WhenConstructorIsLegacyNamespaceQualified_RewritesConstructor()
        {
            string source =
                "ToolInfo info = new io.github.hatayama.uLoopMCP.ToolInfo(\"sample-tool\", \"Sample description\", \"Development\");\n";
            int replacementCount = 0;

            string content = ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolInfoConstructorsInCode(
                source,
                Array.Empty<string>(),
                canMigrateBareLegacyToolInfoConstructor: false,
                canMigrateAmbiguousBareLegacyToolInfoConstructor: false,
                legacyAssemblyToolInfoAliases: Array.Empty<string>(),
                ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    "ToolInfo info = new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"sample-tool\", \"Development\");\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
