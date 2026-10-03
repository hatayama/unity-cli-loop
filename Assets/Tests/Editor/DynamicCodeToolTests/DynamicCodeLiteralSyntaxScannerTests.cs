using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the literal syntax scanner copies whole interpolated, verbatim, regular, and char literals,
    /// and leaves the index and output untouched when a literal never closes.
    /// </summary>
    public sealed class DynamicCodeLiteralSyntaxScannerTests
    {
        private const string Tail = " tail";

        /// <summary>
        /// Verifies a verbatim interpolated string opened with $@ keeps doubled quotes and an unpaired doubled brace
        /// inside it.
        /// </summary>
        [Test]
        public void TryCopyInterpolatedStringLiteral_WithDollarAtPrefix_CopiesTheWholeLiteral()
        {
            AssertInterpolatedLiteralCopied("$@\"a \"\"q\"\" {{ x {value}\"");
        }

        /// <summary>
        /// Verifies a verbatim interpolated string opened with @$ ends at a quote right after a backslash,
        /// because a backslash is not an escape there.
        /// </summary>
        [Test]
        public void TryCopyInterpolatedStringLiteral_WithAtDollarPrefix_TreatsBackslashAsText()
        {
            AssertInterpolatedLiteralCopied("@$\"{value}\\\"");
        }

        /// <summary>
        /// Verifies a regular interpolated string skips escaped quotes and doubled closing braces in its text.
        /// </summary>
        [Test]
        public void TryCopyInterpolatedStringLiteral_WithEscapesAndDoubledClosingBraces_CopiesTheWholeLiteral()
        {
            AssertInterpolatedLiteralCopied("$\"a \\\" b }} {value}\"");
        }

        /// <summary>
        /// Verifies holes skip nested braces, nested interpolated strings, and the quotes and braces inside
        /// verbatim, regular, and char literals and comments.
        /// </summary>
        [Test]
        public void TryCopyInterpolatedStringLiteral_WithLiteralsAndCommentsInsideHoles_CopiesTheWholeLiteral()
        {
            AssertInterpolatedLiteralCopied(
                "$\"{new[] { 1 }.Length + \"x\".Length} {$\"{\"}\"}\"} {@\"}\"} {\"\\\"}\"} {'}'} {x /* \"} */} {y // \"\n}\"");
        }

        /// <summary>
        /// Verifies an unterminated interpolated string is not copied and the index stays at its start.
        /// </summary>
        [Test]
        public void TryCopyInterpolatedStringLiteral_WhenUnterminated_LeavesTheIndexAndOutputUntouched()
        {
            AssertNotCopied("$\"never {closed}", TryCopyInterpolated);
        }

        /// <summary>
        /// Verifies a verbatim string keeps doubled quotes as content.
        /// </summary>
        [Test]
        public void TryCopyVerbatimStringLiteral_WithDoubledQuotes_CopiesTheWholeLiteral()
        {
            string literal = "@\"a \"\"b\"\" c\"";
            string source = literal + Tail;
            StringBuilder output = new StringBuilder();
            int index = 0;

            bool copied = DynamicCodeLiteralSyntaxScanner.TryCopyVerbatimStringLiteral(source, output, ref index);

            Assert.That(copied, Is.True);
            Assert.That(output.ToString(), Is.EqualTo(literal));
            Assert.That(index, Is.EqualTo(literal.Length));
        }

        /// <summary>
        /// Verifies an unterminated verbatim string is not copied.
        /// </summary>
        [Test]
        public void TryCopyVerbatimStringLiteral_WhenUnterminated_LeavesTheIndexAndOutputUntouched()
        {
            AssertNotCopied("@\"never closed", DynamicCodeLiteralSyntaxScanner.TryCopyVerbatimStringLiteral);
        }

        /// <summary>
        /// Verifies a quote right after @ is not read again as a regular string.
        /// </summary>
        [Test]
        public void TryCopyRegularStringLiteral_AfterAnAtSign_IsNotALiteral()
        {
            const string source = "@\"x\"";
            StringBuilder output = new StringBuilder();
            int index = 1;

            bool copied = DynamicCodeLiteralSyntaxScanner.TryCopyRegularStringLiteral(source, output, ref index);

            Assert.That(copied, Is.False);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(output.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an unterminated regular string is not copied.
        /// </summary>
        [Test]
        public void TryCopyRegularStringLiteral_WhenUnterminated_LeavesTheIndexAndOutputUntouched()
        {
            AssertNotCopied("\"never \\\" closed", DynamicCodeLiteralSyntaxScanner.TryCopyRegularStringLiteral);
        }

        /// <summary>
        /// Verifies an unterminated char literal is not copied.
        /// </summary>
        [Test]
        public void TryCopyCharLiteral_WhenUnterminated_LeavesTheIndexAndOutputUntouched()
        {
            AssertNotCopied("'\\'", DynamicCodeLiteralSyntaxScanner.TryCopyCharLiteral);
        }

        private delegate bool CopyLiteral(string source, StringBuilder output, ref int index);

        private static bool TryCopyInterpolated(string source, StringBuilder output, ref int index)
        {
            return DynamicCodeLiteralSyntaxScanner.TryCopyInterpolatedStringLiteral(source, output, ref index);
        }

        private static void AssertInterpolatedLiteralCopied(string literal)
        {
            string source = literal + Tail;
            StringBuilder output = new StringBuilder();
            int index = 0;

            bool copied = DynamicCodeLiteralSyntaxScanner.TryCopyInterpolatedStringLiteral(source, output, ref index);

            Assert.That(copied, Is.True);
            Assert.That(output.ToString(), Is.EqualTo(literal));
            Assert.That(index, Is.EqualTo(literal.Length));
        }

        private static void AssertNotCopied(string source, CopyLiteral copy)
        {
            StringBuilder output = new StringBuilder();
            int index = 0;

            bool copied = copy(source, output, ref index);

            Assert.That(copied, Is.False);
            Assert.That(index, Is.EqualTo(0));
            Assert.That(output.Length, Is.EqualTo(0));
        }
    }
}
