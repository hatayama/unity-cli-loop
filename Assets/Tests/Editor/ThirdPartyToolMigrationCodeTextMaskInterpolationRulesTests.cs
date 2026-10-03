using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies code masking of interpolated strings masks literal text while keeping interpolation holes as code.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCodeTextMaskInterpolationRulesTests
    {
        /// <summary>
        /// Verifies an escaped quote inside an interpolated string stays masked and does not end the literal.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedStringHasEscapedQuote_MasksWholeLiteral()
        {
            string source = "$\"a\\\"b\" + c";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("-------cccc"));
        }

        /// <summary>
        /// Verifies doubled braces in an interpolated string are literal text rather than an interpolation hole.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedStringHasDoubledBraces_MasksBracesAsLiteralText()
        {
            string source = "$\"{{x}}\" + y";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--------cccc"));
        }

        /// <summary>
        /// Verifies an unterminated interpolated string masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = $\"ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc----"));
        }

        /// <summary>
        /// Verifies a doubled quote inside an interpolated verbatim string is an escaped quote, for both prefix orders.
        /// </summary>
        [TestCase("$@\"a\"\"\\\" + c")]
        [TestCase("@$\"a\"\"\\\" + c")]
        public void CreateCodeCharacters_WhenInterpolatedVerbatimStringHasDoubledQuote_MasksWholeLiteral(string source)
        {
            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--------cccc"));
        }

        /// <summary>
        /// Verifies doubled braces in an interpolated verbatim string are literal text rather than an interpolation hole.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedVerbatimStringHasDoubledBraces_MasksBracesAsLiteralText()
        {
            string source = "$@\"{{x}}\" + y";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("---------cccc"));
        }

        /// <summary>
        /// Verifies an unterminated interpolated verbatim string masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedVerbatimStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = $@\"ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc-----"));
        }

        /// <summary>
        /// Verifies nested braces inside an interpolation hole keep the whole hole as code until its own closing brace.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolationHoleHasNestedBraces_KeepsWholeHoleAsCode()
        {
            string source = "$\"{new[] { 1 }.Length}\" + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--" + "cccccccccccccccccccc" + "-" + "cccc"));
        }

        /// <summary>
        /// Verifies an unterminated interpolated raw string masks everything up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenInterpolatedRawStringIsUnterminated_MasksToEndOfSource()
        {
            string source = "x = $$\"\"\"ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("cccc-------"));
        }

        /// <summary>
        /// Verifies an unclosed raw interpolation hole keeps its content as code up to the end of the source.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRawInterpolationHoleIsUnclosed_KeepsHoleContentAsCode()
        {
            string source = "$$\"\"\"{{ab";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("-----cccc"));
        }

        /// <summary>
        /// Verifies a string literal inside a raw interpolation hole is masked while the hole braces stay code.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRawInterpolationHoleContainsString_MasksTheInnerString()
        {
            string source = "$$\"\"\"{{\"a\"}}\"\"\" + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("-----" + "cc" + "---" + "cc" + "---" + "cccc"));
        }

        /// <summary>
        /// Verifies nested braces inside a raw interpolation hole do not close the hole at an inner doubled closing brace.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenRawInterpolationHoleHasNestedBraces_KeepsWholeHoleAsCode()
        {
            string hole = "{{new[] { new[] { 1 }}.Length}}";
            string source = "$$\"\"\"" + hole + "\"\"\" + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("-----" + new string('c', hole.Length) + "---" + "cccc"));
        }

        /// <summary>
        /// Verifies an interpolated raw string inside a hole masks its literal text but keeps its own hole as code.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenHoleContainsInterpolatedRawString_KeepsInnerHoleAsCode()
        {
            string source = "$\"{$$\"\"\"a{{b}}c\"\"\"}\" + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--" + "c" + "------" + "ccccc" + "----" + "c" + "-" + "cccc"));
        }

        /// <summary>
        /// Verifies an interpolated verbatim string inside a hole masks its literal text but keeps its own hole as code.
        /// </summary>
        [TestCase("$\"{$@\"a{b}c\"}\" + x")]
        [TestCase("$\"{@$\"a{b}c\"}\" + x")]
        public void CreateCodeCharacters_WhenHoleContainsInterpolatedVerbatimString_KeepsInnerHoleAsCode(string source)
        {
            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--" + "c" + "----" + "ccc" + "--" + "c" + "-" + "cccc"));
        }

        /// <summary>
        /// Verifies an interpolated regular string inside a hole masks its literal text but keeps its own hole as code.
        /// </summary>
        [Test]
        public void CreateCodeCharacters_WhenHoleContainsInterpolatedRegularString_KeepsInnerHoleAsCode()
        {
            string source = "$\"{$\"a{b}c\"}\" + x";

            string mask = RenderCodeMask(source);

            Assert.That(mask, Is.EqualTo("--" + "c" + "---" + "ccc" + "--" + "c" + "-" + "cccc"));
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
