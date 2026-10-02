using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies attribute argument splitting and argument lookup helpers used by the migration rules.
    /// </summary>
    public sealed class ThirdPartyToolMigrationArgumentRulesTests
    {
        /// <summary>
        /// Verifies that commas and doubled quotes inside a verbatim string do not split the argument list, and that a backslash after a doubled quote is not read as an escape.
        /// </summary>
        [Test]
        public void SplitAttributeArguments_WhenVerbatimStringContainsCommaAndDoubledQuote_KeepsStringAsOneArgument()
        {
            string[] arguments = ThirdPartyToolMigrationArgumentRules.SplitAttributeArguments(
                "@\"a,\"\"\\\", c");

            Assert.That(arguments, Is.EqualTo(new[] { "@\"a,\"\"\\\"", " c" }));
        }

        /// <summary>
        /// Verifies that a backslash inside a verbatim string is not an escape, so the closing quote ends the argument.
        /// </summary>
        [Test]
        public void SplitAttributeArguments_WhenVerbatimStringEndsWithBackslash_SplitsAfterClosingQuote()
        {
            string[] arguments = ThirdPartyToolMigrationArgumentRules.SplitAttributeArguments(
                "@\"a\\\", b");

            Assert.That(arguments, Is.EqualTo(new[] { "@\"a\\\"", " b" }));
        }

        /// <summary>
        /// Verifies that a comma inside a char literal, including after an escaped quote literal, does not split the argument list.
        /// </summary>
        [Test]
        public void SplitAttributeArguments_WhenCharLiteralsContainCommaAndEscapedQuote_KeepsLiteralsIntact()
        {
            string[] arguments = ThirdPartyToolMigrationArgumentRules.SplitAttributeArguments(
                "'\\'', ',', x");

            Assert.That(arguments, Is.EqualTo(new[] { "'\\''", " ','", " x" }));
        }

        /// <summary>
        /// Verifies that an escaped quote inside a regular string does not end the string before a following comma.
        /// </summary>
        [Test]
        public void SplitAttributeArguments_WhenRegularStringContainsEscapedQuote_KeepsCommaInsideString()
        {
            string[] arguments = ThirdPartyToolMigrationArgumentRules.SplitAttributeArguments(
                "\"a\\\",b\", c");

            Assert.That(arguments, Is.EqualTo(new[] { "\"a\\\",b\"", " c" }));
        }

        /// <summary>
        /// Verifies that an unterminated interpolated string is scanned as a string to the end, so its commas never split.
        /// </summary>
        [Test]
        public void SplitAttributeArguments_WhenInterpolatedStringIsUnterminated_ReturnsSingleArgument()
        {
            string[] arguments = ThirdPartyToolMigrationArgumentRules.SplitAttributeArguments(
                "$\"a, b");

            Assert.That(arguments, Is.EqualTo(new[] { "$\"a, b" }));
        }

        /// <summary>
        /// Verifies that a missing closing parenthesis yields -1 instead of an index.
        /// </summary>
        [Test]
        public void FindInvocationClosingParenthesisIndex_WhenClosingParenthesisIsMissing_ReturnsMinusOne()
        {
            const string source = "Foo(a, (b)";
            ThirdPartyToolMigrationParsingRules.CodeTextMask codeTextMask =
                ThirdPartyToolMigrationParsingRules.CodeTextMask.CreateUncached(source);

            int index = ThirdPartyToolMigrationArgumentRules.FindInvocationClosingParenthesisIndex(
                source,
                codeTextMask,
                3);

            Assert.That(index, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies that an argument consisting of the name followed only by whitespace is not treated as a named assignment.
        /// </summary>
        [Test]
        public void IsNamedAttributeArgument_WhenNameIsFollowedOnlyByWhitespace_ReturnsFalse()
        {
            bool isNamed = ThirdPartyToolMigrationArgumentRules.IsNamedAttributeArgument("Description  ", "Description");

            Assert.That(isNamed, Is.False);
        }
    }
}
