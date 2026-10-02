using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies migration code masking ignores preprocessor text and bounds char literals.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCodeTextMaskBuilderTests
    {
        [Test]
        public void CreateCodeCharacters_WhenRegionNameContainsApostrophe_KeepsFollowingCodeUnmasked()
        {
            // Verifies a #region title apostrophe does not mask the rest of the file as a char literal.
            string source = "#region Bob's helpers\npublic class Tool {}\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int codeStartIndex = source.IndexOf("public class Tool");
            Assert.That(codeStartIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(AreAllCodeCharacters(codeCharacters, codeStartIndex, "public class Tool".Length), Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenWarningContainsApostrophe_KeepsFollowingCodeUnmasked()
        {
            // Verifies a #warning apostrophe does not mask the following source as a char literal.
            string source = "#warning Don't call this API\npublic class Tool {}\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int codeStartIndex = source.IndexOf("public class Tool");
            Assert.That(codeStartIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(AreAllCodeCharacters(codeCharacters, codeStartIndex, "public class Tool".Length), Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenValidCharLiteral_MasksOnlyTheLiteral()
        {
            // Verifies a real char literal stays masked while the following statement remains code.
            string source = "char c = 'a';\nint x = 1;\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            int literalIndex = source.IndexOf("'a'");
            Assert.That(codeCharacters[literalIndex], Is.False);
            Assert.That(codeCharacters[literalIndex + 1], Is.False);
            Assert.That(codeCharacters[literalIndex + 2], Is.False);
            Assert.That(codeCharacters[source.IndexOf("int x")], Is.True);
        }

        [Test]
        public void CreateCodeCharacters_WhenApostropheDoesNotCloseSoon_TreatsItAsCode()
        {
            // Verifies a long unmatched apostrophe is not treated as an open-ended char literal.
            string source = "var name = Bob's helpers;\nint x = 1;\n";

            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);

            Assert.That(codeCharacters[source.IndexOf("int x")], Is.True);
            Assert.That(AreAllCodeCharacters(codeCharacters, source.IndexOf("helpers"), "helpers".Length), Is.True);
        }

        private static bool AreAllCodeCharacters(bool[] codeCharacters, int startIndex, int length)
        {
            for (int index = startIndex; index < startIndex + length; index++)
            {
                if (!codeCharacters[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Verifies the ignored-text end of an interpolated verbatim string skips doubled quotes, for both prefix orders.
        /// </summary>
        [TestCase("$@\"a\"\"b\" x")]
        [TestCase("@$\"a\"\"b\" x")]
        public void GetIgnoredTextEndIndex_WhenInterpolatedVerbatimString_ReturnsIndexAfterClosingQuote(string source)
        {
            int endIndex = ThirdPartyToolMigrationCodeTextMaskBuilder.GetIgnoredTextEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(8));
        }

        /// <summary>
        /// Verifies the ignored-text end of an interpolated raw string is the index after its closing delimiter.
        /// </summary>
        [Test]
        public void GetIgnoredTextEndIndex_WhenInterpolatedRawString_ReturnsIndexAfterClosingDelimiter()
        {
            string source = "$$\"\"\"abc\"\"\" x";

            int endIndex = ThirdPartyToolMigrationCodeTextMaskBuilder.GetIgnoredTextEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(11));
        }

        /// <summary>
        /// Verifies the ignored-text end of an interpolated regular string is the index after its closing quote.
        /// </summary>
        [Test]
        public void GetIgnoredTextEndIndex_WhenInterpolatedRegularString_ReturnsIndexAfterClosingQuote()
        {
            string source = "$\"abc\" x";

            int endIndex = ThirdPartyToolMigrationCodeTextMaskBuilder.GetIgnoredTextEndIndex(source, 0);

            Assert.That(endIndex, Is.EqualTo(6));
        }

        /// <summary>
        /// Verifies a doubled quote inside a verbatim string is an escaped quote and a backslash is literal text.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenVerbatimStringHasDoubledQuote_MasksWholeLiteral()
        {
            string source = "@\"a\"\"\\\" + c";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("-------cccc"));
        }

        /// <summary>
        /// Verifies an unterminated verbatim string masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenVerbatimStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = @\"ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc----"));
        }

        /// <summary>
        /// Verifies an unterminated block comment masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenBlockCommentIsUnterminated_MasksToEndOfSource()
        {
            string source = "x /* ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cc-----"));
        }

        /// <summary>
        /// Verifies an escaped quote inside a regular string stays masked and does not end the literal.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRegularStringHasEscapedQuote_MasksWholeLiteral()
        {
            string source = "\"a\\\"b\" + c";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("------cccc"));
        }

        /// <summary>
        /// Verifies an unterminated regular string masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRegularStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = \"ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc---"));
        }

        /// <summary>
        /// Verifies an unterminated raw string masks everything up to the end of the source, including a lone quote inside it.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRawStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = \"\"\"a\"b";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc------"));
        }

        /// <summary>
        /// Verifies an escaped apostrophe char literal is masked as one literal and the following code stays unmasked.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenCharLiteralIsEscapedApostrophe_MasksOnlyTheLiteral()
        {
            string source = "'\\'' + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("----cccc"));
        }

        /// <summary>
        /// Verifies a preprocessor directive indented after a previous line is masked while surrounding code stays unmasked.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenPreprocessorDirectiveIsIndented_MasksOnlyTheDirective()
        {
            string source = "int a;\n  #region API's\nint b;";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("ccccccc" + "cc" + "-------------" + "c" + "cccccc"));
        }

        /// <summary>
        /// Verifies a hash sign preceded by code on the same line is not a preprocessor directive.
        /// </summary>
        [Test]
        public void IsPreprocessorDirectiveAt_WhenHashFollowsCodeOnSameLine_ReturnsFalse()
        {
            string source = "int a;\nint b; #if SAMPLE";

            bool result = ThirdPartyToolMigrationCodeTextMaskBuilder.IsPreprocessorDirectiveAt(source, source.IndexOf('#'));

            Assert.That(result, Is.False);
        }

        private static string RenderCodeMask(string source)
        {
            bool[] codeCharacters = ThirdPartyToolMigrationCodeTextMaskBuilder.CreateCodeCharacters(source);
            StringBuilder builder = new StringBuilder(codeCharacters.Length);
            foreach (bool isCode in codeCharacters)
            {
                builder.Append(isCode ? 'c' : '-');
            }

            return builder.ToString();
        }
    }
}
