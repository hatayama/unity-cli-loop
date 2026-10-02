using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies attribute-list rewriting and closing-bracket search in migration code text.
    /// </summary>
    public sealed class ThirdPartyToolMigrationRegexRewriteRulesTests
    {
        /// <summary>
        /// Verifies an unclosed bracket is copied unchanged and a later legacy tool attribute is still rewritten.
        /// </summary>
        [Test]
        public void ReplaceLegacyToolAttributesInCode_WhenUnclosedBracketPrecedesLegacyAttribute_RewritesLaterAttribute()
        {
            string source = "int[ x;\n[McpTool]\nclass SampleTool {}";
            int replacementCount = 0;

            string migrated = ThirdPartyToolMigrationRegexRewriteRules.ReplaceLegacyToolAttributesInCode(
                source,
                Array.Empty<string>(),
                true,
                ref replacementCount);

            Assert.That(migrated, Is.EqualTo("int[ x;\n[UnityCliLoopTool]\nclass SampleTool {}"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies nested brackets inside an attribute list are skipped so the outer closing bracket is returned.
        /// </summary>
        [Test]
        public void FindAttributeListClosingBracketIndex_WhenListContainsNestedBrackets_ReturnsOuterClosingBracketIndex()
        {
            string source = "[Values(new[] { 1 })] x";
            ThirdPartyToolMigrationParsingRules.CodeTextMask mask =
                ThirdPartyToolMigrationParsingRules.CodeTextMask.CreateUncached(source);

            int closingIndex = ThirdPartyToolMigrationRegexRewriteRules.FindAttributeListClosingBracketIndex(
                source,
                mask,
                1);

            Assert.That(closingIndex, Is.EqualTo(20));
        }

        /// <summary>
        /// Verifies an attribute list without a closing bracket reports that no closing bracket was found.
        /// </summary>
        [Test]
        public void FindAttributeListClosingBracketIndex_WhenListIsUnclosed_ReturnsMinusOne()
        {
            string source = "[Values(new[] { 1 }) x";
            ThirdPartyToolMigrationParsingRules.CodeTextMask mask =
                ThirdPartyToolMigrationParsingRules.CodeTextMask.CreateUncached(source);

            int closingIndex = ThirdPartyToolMigrationRegexRewriteRules.FindAttributeListClosingBracketIndex(
                source,
                mask,
                1);

            Assert.That(closingIndex, Is.EqualTo(-1));
        }
    }
}
