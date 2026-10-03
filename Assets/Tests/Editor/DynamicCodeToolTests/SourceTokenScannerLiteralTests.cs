using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the low-level source scanner skips raw, verbatim, and interpolated literals as single tokens,
    /// including unterminated ones, and skips comments between statements.
    /// </summary>
    public sealed class SourceTokenScannerLiteralTests
    {
        private const string Tail = " tail";

        /// <summary>
        /// Verifies leading line and block comments are skipped together with the whitespace around them.
        /// </summary>
        [Test]
        public void SkipWhitespaceAndComments_SkipsLineAndBlockComments()
        {
            const string source = "  // note\n  /* block */  value";

            Assert.That(SourceTokenScanner.SkipWhitespaceAndComments(source, 0), Is.EqualTo(source.IndexOf("value")));
        }

        /// <summary>
        /// Verifies a semicolon inside a string literal is not taken as the statement end.
        /// </summary>
        [Test]
        public void FindSemicolon_SkipsSemicolonsInsideStrings()
        {
            const string source = "x = \";\"; y";

            Assert.That(SourceTokenScanner.FindSemicolon(source, 0), Is.EqualTo(source.LastIndexOf(';')));
        }

        /// <summary>
        /// Verifies a statement without a semicolon ends at the last character, like FindStatementEnd.
        /// </summary>
        [Test]
        public void FindSemicolon_WithoutASemicolon_ReturnsTheLastIndex()
        {
            const string source = "x = 1";

            Assert.That(SourceTokenScanner.FindSemicolon(source, 0), Is.EqualTo(source.Length - 1));
        }

        /// <summary>
        /// Verifies a raw string literal is skipped up to its closing triple quote, past the quotes inside it.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithARawString_SkipsToTheClosingTripleQuote()
        {
            string source = "\"\"\"say \"hi\" now\"\"\"" + Tail;

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length - Tail.Length));
        }

        /// <summary>
        /// Verifies an unterminated raw string literal runs to the end of the source.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithAnUnterminatedRawString_RunsToTheEnd()
        {
            const string source = "\"\"\"never closed";

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length));
        }

        /// <summary>
        /// Verifies a verbatim string treats doubled quotes as content.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithDoubledQuotesInAVerbatimString_SkipsTheWholeLiteral()
        {
            string source = "@\"a \"\"quoted\"\" b\"" + Tail;

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length - Tail.Length));
        }

        /// <summary>
        /// Verifies an unterminated verbatim string runs to the end of the source.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithAnUnterminatedVerbatimString_RunsToTheEnd()
        {
            const string source = "@\"never closed";

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length));
        }

        /// <summary>
        /// Verifies an interpolated string skips escaped quotes and an unpaired doubled brace in its text.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithEscapesAndDoubledBracesInAnInterpolatedString_SkipsTheWholeLiteral()
        {
            string source = "$\"a \\\" {{ b {value} c\"" + Tail;

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length - Tail.Length));
        }

        /// <summary>
        /// Verifies an interpolation hole skips nested braces and the quotes of the literals inside it.
        /// </summary>
        [TestCase("$\"{new[] { \"}\" }.Length + \"x\".Length}\"", TestName = "AdvanceOneToken_WithNestedBracesAndAStringInsideAHole_SkipsTheWholeLiteral")]
        [TestCase("$\"{@\"\\\"}\"", TestName = "AdvanceOneToken_WithAVerbatimStringInsideAHole_SkipsTheWholeLiteral")]
        [TestCase("$\"{\"\"\"a\"b\"\"\"}\"", TestName = "AdvanceOneToken_WithARawStringInsideAHole_SkipsTheWholeLiteral")]
        [TestCase("$\"{'\"'}\"", TestName = "AdvanceOneToken_WithACharLiteralInsideAHole_SkipsTheWholeLiteral")]
        public void AdvanceOneToken_WithALiteralInsideAHole_SkipsTheWholeLiteral(string literal)
        {
            string source = literal + Tail;

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(literal.Length));
        }

        /// <summary>
        /// Verifies an unterminated interpolated string runs to the end of the source.
        /// </summary>
        [Test]
        public void AdvanceOneToken_WithAnUnterminatedInterpolatedString_RunsToTheEnd()
        {
            const string source = "$\"never closed";

            Assert.That(SourceTokenScanner.AdvanceOneToken(source, 0), Is.EqualTo(source.Length));
        }
    }
}
