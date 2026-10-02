using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies source-level detection of legacy type alias references.
    /// </summary>
    public sealed class ThirdPartyToolMigrationSourceDetectionRulesTests
    {
        /// <summary>
        /// Verifies that a type alias used in code after a commented mention is detected.
        /// </summary>
        [Test]
        public void ContainsLegacyTypeAliasReference_WhenAliasIsUsedInCode_ReturnsTrue()
        {
            const string source = "// LegacyInfo is mentioned here\nLegacyInfo info;";

            bool contains = ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyTypeAliasReference(
                source,
                new[] { "LegacyInfo" });

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a type alias mentioned only in a comment or as a member-access suffix is not detected.
        /// </summary>
        [Test]
        public void ContainsLegacyTypeAliasReference_WhenAliasIsOnlyInCommentOrMemberAccess_ReturnsFalse()
        {
            const string source = "// LegacyInfo is mentioned here\nOther.LegacyInfo info;";

            bool contains = ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyTypeAliasReference(
                source,
                new[] { "LegacyInfo" });

            Assert.That(contains, Is.False);
        }
    }
}
