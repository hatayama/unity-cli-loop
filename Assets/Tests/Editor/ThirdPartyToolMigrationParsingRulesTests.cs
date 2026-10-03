using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the shared text-scanning helpers used by third-party tool migration parsing.
    /// </summary>
    public sealed class ThirdPartyToolMigrationParsingRulesTests
    {
        /// <summary>
        /// Verifies the next non-whitespace index is the source length when only whitespace remains.
        /// </summary>
        [Test]
        public void ReadNextNonWhitespaceIndex_WhenOnlyWhitespaceRemains_ReturnsSourceLength()
        {
            int index = ThirdPartyToolMigrationParsingRules.ReadNextNonWhitespaceIndex("a \t\n", 1);

            Assert.That(index, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies the next non-whitespace character is the null character when only whitespace remains.
        /// </summary>
        [Test]
        public void ReadNextNonWhitespaceCharacter_WhenOnlyWhitespaceRemains_ReturnsNullCharacter()
        {
            char value = ThirdPartyToolMigrationParsingRules.ReadNextNonWhitespaceCharacter("a \t\n", 1);

            Assert.That(value, Is.EqualTo('\0'));
        }

        /// <summary>
        /// Verifies letters and underscores can start an identifier while digits and symbols cannot.
        /// </summary>
        [TestCase('a', true)]
        [TestCase('Z', true)]
        [TestCase('_', true)]
        [TestCase('1', false)]
        [TestCase('$', false)]
        public void IsIdentifierStartCharacter_WhenGivenCharacter_ReturnsWhetherItCanStartIdentifier(
            char value,
            bool expected)
        {
            bool result = ThirdPartyToolMigrationParsingRules.IsIdentifierStartCharacter(value);

            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies an unclosed bracket before a legacy tool attribute does not hide the later attribute list.
        /// </summary>
        [Test]
        public void ContainsLegacyToolAttributeList_WhenUnclosedBracketPrecedesLegacyAttribute_ReturnsTrue()
        {
            string source = "int[ x;\n[McpTool]\nclass SampleTool {}";

            bool result = ThirdPartyToolMigrationParsingRules.ContainsLegacyToolAttributeList(
                source,
                Array.Empty<string>(),
                true);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a repeated-character check that would run past the end of the source returns false.
        /// </summary>
        [Test]
        public void HasRepeatedCharacterAt_WhenRunWouldPassEndOfSource_ReturnsFalse()
        {
            bool result = ThirdPartyToolMigrationParsingRules.HasRepeatedCharacterAt("ab\"\"", 2, '"', 3);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies the string prefix length is two for interpolated verbatim prefixes and one otherwise.
        /// </summary>
        [TestCase("$@\"x\"", 2)]
        [TestCase("@$\"x\"", 2)]
        [TestCase("@\"x\"", 1)]
        [TestCase("$\"x\"", 1)]
        public void GetStringPrefixLength_WhenGivenStringPrefix_ReturnsPrefixLength(string source, int expected)
        {
            int length = ThirdPartyToolMigrationParsingRules.GetStringPrefixLength(source, 0);

            Assert.That(length, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies indices outside the masked source are reported as not code.
        /// </summary>
        [TestCase(-1)]
        [TestCase(3)]
        public void IsCodeAt_WhenIndexIsOutOfRange_ReturnsFalse(int index)
        {
            ThirdPartyToolMigrationParsingRules.CodeTextMask mask =
                ThirdPartyToolMigrationParsingRules.CodeTextMask.CreateUncached("abc");

            Assert.That(mask.IsCodeAt(index), Is.False);
        }
    }
}
