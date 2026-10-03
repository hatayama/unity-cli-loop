using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how regular string literal tokens are decoded, which malformed escapes are rejected, and how
    /// far an escape sequence advances the scan index.
    /// </summary>
    public sealed class DynamicCodeRegularStringLiteralUnescaperTests
    {
        /// <summary>
        /// Verifies simple, unicode, UTF-32, and variable-length hex escapes decode to their characters.
        /// </summary>
        [Test]
        public void TryUnescapeRegularStringLiteral_WithEachEscapeKind_DecodesTheText()
        {
            bool decoded = DynamicCodeRegularStringLiteralUnescaper.TryUnescapeRegularStringLiteral(
                "\"a\\tb\\u0041\\U0001F600\\x41z\"",
                out string value);

            Assert.That(decoded, Is.True);
            Assert.That(value, Is.EqualTo("a\tbA\U0001F600Az"));
        }

        /// <summary>
        /// Verifies each malformed escape makes the whole token undecodable.
        /// </summary>
        [TestCase("\"abc\\\"", TestName = "TryUnescapeRegularStringLiteral_WithATrailingBackslash_IsRejected")]
        [TestCase("\"\\u12\"", TestName = "TryUnescapeRegularStringLiteral_WithAShortUnicodeEscape_IsRejected")]
        [TestCase("\"\\u12G4\"", TestName = "TryUnescapeRegularStringLiteral_WithANonHexUnicodeEscape_IsRejected")]
        [TestCase("\"\\U00110000\"", TestName = "TryUnescapeRegularStringLiteral_WithAnOutOfRangeUtf32Escape_IsRejected")]
        [TestCase("\"\\xZ\"", TestName = "TryUnescapeRegularStringLiteral_WithAHexEscapeWithoutDigits_IsRejected")]
        [TestCase("\"\\q\"", TestName = "TryUnescapeRegularStringLiteral_WithAnUnknownEscape_IsRejected")]
        public void TryUnescapeRegularStringLiteral_WithAMalformedEscape_IsRejected(string token)
        {
            bool decoded = DynamicCodeRegularStringLiteralUnescaper.TryUnescapeRegularStringLiteral(token, out string value);

            Assert.That(decoded, Is.False);
            Assert.That(value, Is.Null);
        }

        /// <summary>
        /// Verifies a backslash at the very end stops the scan at the end of the source.
        /// </summary>
        [Test]
        public void AdvanceEscapedLiteralSequence_AtTheEnd_StopsAtTheSourceLength()
        {
            const string source = "ab\\";
            int index = 2;

            DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref index);

            Assert.That(index, Is.EqualTo(source.Length));
        }

        /// <summary>
        /// Verifies each escape kind advances past exactly its own characters.
        /// </summary>
        [TestCase("\\n rest", 2, TestName = "AdvanceEscapedLiteralSequence_WithASimpleEscape_SkipsTwoCharacters")]
        [TestCase("\\u0041 rest", 6, TestName = "AdvanceEscapedLiteralSequence_WithAUnicodeEscape_SkipsFourDigits")]
        [TestCase("\\U0001F600 rest", 10, TestName = "AdvanceEscapedLiteralSequence_WithAUtf32Escape_SkipsEightDigits")]
        [TestCase("\\x4G rest", 3, TestName = "AdvanceEscapedLiteralSequence_WithAHexEscape_StopsAtTheFirstNonHexDigit")]
        [TestCase("\\x12345 rest", 6, TestName = "AdvanceEscapedLiteralSequence_WithAHexEscape_StopsAfterFourDigits")]
        public void AdvanceEscapedLiteralSequence_SkipsTheEscape(string source, int expectedIndex)
        {
            int index = 0;

            DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref index);

            Assert.That(index, Is.EqualTo(expectedIndex));
        }
    }
}
