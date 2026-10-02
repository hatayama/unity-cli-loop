using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the closing-quote search for regular interpolated strings skips escapes, doubled braces, and literals inside interpolation holes.
    /// </summary>
    public sealed class ThirdPartyToolMigrationInterpolatedStringRulesTests
    {
        /// <summary>
        /// Verifies an escaped quote in the literal text does not end the interpolated string.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenLiteralTextHasEscapedQuote_ReturnsFinalQuoteIndex()
        {
            string source = "$\"a\\\"b\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(6));
        }

        /// <summary>
        /// Verifies a doubled opening brace is literal text, so the following quote closes the string.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenLiteralTextHasDoubledOpenBrace_ReturnsQuoteAfterBraces()
        {
            string source = "$\"{{\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies a nested brace pair inside a hole keeps the hole open until its own closing brace.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsNestedBraces_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{new[] { 1 }.Length + \"s\"}!\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(source.Length - 1));
        }

        /// <summary>
        /// Verifies a nested interpolated string inside a hole is skipped as one literal, including a quote char literal in its own hole.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsInterpolatedString_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{$\"{'\"'}\"}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(12));
        }

        /// <summary>
        /// Verifies a verbatim string inside a hole treats a backslash as literal text, for every verbatim prefix form.
        /// </summary>
        [TestCase("$\"{@\"\\\"}\"", 8)]
        [TestCase("$\"{$@\"\\\"}\"", 9)]
        [TestCase("$\"{@$\"\\\"}\"", 9)]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsVerbatimStringEndingWithBackslash_ReturnsFinalQuoteIndex(
            string source,
            int expectedEndIndex)
        {
            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(expectedEndIndex));
        }

        /// <summary>
        /// Verifies a doubled quote inside a verbatim string in a hole is an escaped quote, not the end of the verbatim string.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleVerbatimStringHasDoubledQuote_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{@\"\"\"\\\"}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(10));
        }

        /// <summary>
        /// Verifies a char literal holding a quote inside a hole does not end the interpolated string.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsQuoteCharLiteral_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{'\"'}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies an escaped apostrophe char literal inside a hole is skipped as one literal.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleContainsEscapedApostropheCharLiteral_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{'\\'' + '\"'}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(14));
        }

        /// <summary>
        /// Verifies an escaped quote inside a regular string in a hole does not end that string.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleStringHasEscapedQuote_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{\"\\\"\" + \"x\"}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(14));
        }

        /// <summary>
        /// Verifies a closing brace inside a regular string in a hole does not close the hole.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleStringContainsClosingBrace_ReturnsFinalQuoteIndex()
        {
            string source = "$\"{\"}\"}\"";

            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies a raw string inside a hole is skipped as one literal, so its quote and closing brace do not end the hole or the string.
        /// </summary>
        [TestCase("$\"{\"\"\"a\"}b\"\"\"}\"", 14)]
        [TestCase("$\"{$$\"\"\"a\"}b\"\"\"}\"", 16)]
        public void FindRegularInterpolatedStringEndIndex_WhenHoleRawStringContainsQuoteAndClosingBrace_ReturnsFinalQuoteIndex(
            string source,
            int expectedEndIndex)
        {
            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(expectedEndIndex));
        }

        /// <summary>
        /// Verifies an interpolated string without a closing quote reports that no end was found.
        /// </summary>
        [Test]
        public void FindRegularInterpolatedStringEndIndex_WhenStringIsUnterminated_ReturnsMinusOne()
        {
            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex("$\"abc", 0);

            Assert.That(endIndex, Is.EqualTo(-1));
        }
    }
}
