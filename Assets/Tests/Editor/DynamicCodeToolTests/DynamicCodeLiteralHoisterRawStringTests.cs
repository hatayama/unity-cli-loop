using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the hoister copies non-interpolated raw string literals whole instead of splitting their quotes
    /// into separate string literals, while the literals around them are still hoisted.
    /// </summary>
    public sealed class DynamicCodeLiteralHoisterRawStringTests
    {
        /// <summary>
        /// Verifies a single-line raw string stays in the source unchanged.
        /// </summary>
        [Test]
        public void Rewrite_WithARawString_KeepsItInPlace()
        {
            const string source = "string raw = \"\"\"text\"\"\"; return raw;";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.RewrittenSource, Is.EqualTo(source));
        }

        /// <summary>
        /// Verifies a raw string opened with four quotes keeps a run of three quotes inside it as content.
        /// </summary>
        [Test]
        public void Rewrite_WithAFourQuoteRawString_KeepsTheInnerTripleQuotes()
        {
            const string source = "string raw = \"\"\"\"has \"\"\" inside\"\"\"\"; return raw;";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.RewrittenSource, Is.EqualTo(source));
        }

        /// <summary>
        /// Verifies a multi-line raw string, including a quoted word and a number inside it, stays unchanged.
        /// </summary>
        [Test]
        public void Rewrite_WithAMultiLineRawString_KeepsItInPlace()
        {
            const string source = "string raw = \"\"\"\n    say \"hi\" 5 times\n    \"\"\";\nreturn raw;";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.RewrittenSource, Is.EqualTo(source));
        }

        /// <summary>
        /// Verifies the regular string and integer literals next to a raw string are still hoisted.
        /// </summary>
        [Test]
        public void Rewrite_WithARawStringAndOtherLiterals_HoistsOnlyTheOtherLiterals()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(
                "string raw = \"\"\"a \"b\" c\"\"\"; return raw + \"x\" + 5;");

            List<object> values = result.Bindings.ConvertAll(binding => binding.Value);
            Assert.That(values, Is.EqualTo(new List<object> { "x", 5 }));
            Assert.That(
                result.RewrittenSource,
                Is.EqualTo(
                    "string raw = \"\"\"a \"b\" c\"\"\"; return raw + "
                    + result.Bindings[0].ParameterName
                    + " + "
                    + result.Bindings[1].ParameterName
                    + ";"));
        }

        /// <summary>
        /// Verifies the source preparer keeps a raw string intact in the wrapped user snippet.
        /// </summary>
        [Test]
        public void Prepare_WithARawString_KeepsItInTheUserSnippet()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "string raw = \"\"\"text\"\"\"; return raw;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(prepared.PreparedSource, Does.Contain("string raw = \"\"\"text\"\"\"; return raw;"));
        }
    }
}
