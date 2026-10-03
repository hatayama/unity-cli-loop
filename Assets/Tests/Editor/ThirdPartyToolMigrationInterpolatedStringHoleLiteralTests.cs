using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the closing-quote search for regular interpolated strings with raw strings in a hole, and with
    /// literals in a hole that never close.
    /// </summary>
    public sealed class ThirdPartyToolMigrationInterpolatedStringHoleLiteralTests
    {
        /// <summary>
        /// Verifies a raw interpolated string in a hole is skipped as one literal, including single braces that are
        /// text under two dollar signs and a nested brace pair inside its own hole.
        /// </summary>
        [TestCase("$\"{$$\"\"\"{x}\"\"\"}\"", TestName = "FindRegularInterpolatedStringEndIndex_WithSingleBracesInATwoDollarRawString_ReturnsFinalQuoteIndex")]
        [TestCase("$\"{$$\"\"\"{{new { }}}\"\"\"}\"", TestName = "FindRegularInterpolatedStringEndIndex_WithNestedBracesInARawStringHole_ReturnsFinalQuoteIndex")]
        public void FindRegularInterpolatedStringEndIndex_WithARawStringInAHole_ReturnsFinalQuoteIndex(string source)
        {
            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(source.Length - 1));
        }

        /// <summary>
        /// Verifies a literal in a hole that never closes leaves the whole interpolated string unterminated.
        /// </summary>
        [TestCase("$\"{$$\"\"\"text", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnterminatedRawInterpolatedString_ReturnsMinusOne")]
        [TestCase("$\"{$$\"\"\"{{x", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnclosedRawStringHole_ReturnsMinusOne")]
        [TestCase("$\"{\"\"\"text", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnterminatedRawString_ReturnsMinusOne")]
        [TestCase("$\"{@\"text", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnterminatedVerbatimString_ReturnsMinusOne")]
        [TestCase("$\"{\"text", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnterminatedRegularString_ReturnsMinusOne")]
        [TestCase("$\"{'t", TestName = "FindRegularInterpolatedStringEndIndex_WithAnUnterminatedCharLiteral_ReturnsMinusOne")]
        public void FindRegularInterpolatedStringEndIndex_WithAnUnterminatedLiteralInAHole_ReturnsMinusOne(string source)
        {
            int endIndex = ThirdPartyToolMigrationInterpolatedStringRules.FindRegularInterpolatedStringEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(-1));
        }
    }
}
