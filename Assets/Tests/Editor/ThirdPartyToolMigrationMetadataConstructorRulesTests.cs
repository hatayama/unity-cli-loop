using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies legacy ToolInfo and ToolSettingsCatalogItem constructor rewrites.
    /// </summary>
    public sealed class ThirdPartyToolMigrationMetadataConstructorRulesTests
    {
        private const string CurrentToolSettingsCatalogItemConstructor =
            "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem(";

        /// <summary>
        /// Verifies that an unclosed bare ToolInfo constructor is skipped while a complete one before it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolInfoConstructorsInCode_WhenLaterConstructorIsUnclosed_RewritesOnlyClosedConstructor()
        {
            const string source =
                "new ToolInfo(\"a\", \"desc\", schema);\nnew ToolInfo(\"b\", \"desc\", schema";
            int replacementCount = 0;

            string content = ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolInfoConstructorsInCode(
                source,
                Array.Empty<string>(),
                true,
                true,
                Array.Empty<string>(),
                ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"a\", schema);\n" +
                    "new ToolInfo(\"b\", \"desc\", schema"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a ToolSettingsCatalogItem constructor opened inside a block comment and closed in code stays untouched while the real one is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenConstructorStartsInBlockComment_RewritesOnlyCodeConstructor()
        {
            const string source =
                "M(/* new ToolSettingsCatalogItem(\"a\", \"desc\", */ dev, third);\n" +
                "new ToolSettingsCatalogItem(\"b\", \"desc\", dev, third);";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    "M(/* new ToolSettingsCatalogItem(\"a\", \"desc\", */ dev, third);\n" +
                    CurrentToolSettingsCatalogItemConstructor + "\"b\", dev, third);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an unclosed ToolSettingsCatalogItem constructor is skipped while a complete one before it is rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenLaterConstructorIsUnclosed_RewritesOnlyClosedConstructor()
        {
            const string source =
                "new ToolSettingsCatalogItem(\"a\", \"desc\", dev, third);\n" +
                "new ToolSettingsCatalogItem(\"b\", \"desc\", dev, third";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    CurrentToolSettingsCatalogItemConstructor + "\"a\", dev, third);\n" +
                    "new ToolSettingsCatalogItem(\"b\", \"desc\", dev, third"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a ToolSettingsCatalogItem constructor already in the current three-argument shape is left unchanged and uncounted.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenConstructorHasCurrentShape_ReturnsSourceUnchanged()
        {
            const string source = "new ToolSettingsCatalogItem(\"a\",dev,third);";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    ref replacementCount);

            Assert.That(content, Is.EqualTo("new ToolSettingsCatalogItem(\"a\",dev,third);"));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a named description argument is removed from a ToolSettingsCatalogItem constructor.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenDescriptionIsNamed_RemovesNamedDescription()
        {
            const string source = "new ToolSettingsCatalogItem(\"a\", description: \"desc\", dev);";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    ref replacementCount);

            Assert.That(content, Is.EqualTo(CurrentToolSettingsCatalogItemConstructor + "\"a\", dev);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a fully qualified legacy ToolSettingsCatalogItem constructor is rewritten even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenConstructorIsFullyQualified_RewritesWithoutBareMigration()
        {
            const string source =
                "new io.github.hatayama.uLoopMCP.ToolSettingsCatalogItem(\"a\", \"desc\", dev, third);";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    Array.Empty<string>(),
                    false,
                    ref replacementCount);

            Assert.That(content, Is.EqualTo(CurrentToolSettingsCatalogItemConstructor + "\"a\", dev, third);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a ToolSettingsCatalogItem constructor through a known legacy alias is rewritten while one through an unknown alias is not.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolSettingsCatalogItemConstructorsInCode_WhenConstructorUsesLegacyAlias_RewritesOnlyKnownAlias()
        {
            const string source =
                "new Other.ToolSettingsCatalogItem(\"a\", \"desc\", dev, third);\n" +
                "new Legacy.ToolSettingsCatalogItem(\"b\", \"desc\", dev, third);";
            int replacementCount = 0;

            string content =
                ThirdPartyToolMigrationMetadataConstructorRules.ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                    source,
                    new[] { "Legacy" },
                    false,
                    ref replacementCount);

            Assert.That(
                content,
                Is.EqualTo(
                    "new Other.ToolSettingsCatalogItem(\"a\", \"desc\", dev, third);\n" +
                    CurrentToolSettingsCatalogItemConstructor + "\"b\", dev, third);"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
