using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Characterization tests that lock current DynamicCodeLiteralHoister Rewrite outputs before god-class splits.
    /// </summary>
    [TestFixture]
    public class DynamicCodeLiteralHoisterCharacterizationTests
    {
        /// <summary>
        /// Pins integer literal hoisting for a simple return statement.
        /// </summary>
        [Test]
        public void Rewrite_WhenIntegerLiteral_ShouldHoistToParameter()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return 42;");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].ParameterName, Is.EqualTo("__uloop_literal_0"));
            Assert.That(result.Bindings[0].TypeName, Is.EqualTo("int"));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(42));
            Assert.That(
                result.DeclarationLines,
                Is.EqualTo(new[]
                {
                    "int __uloop_literal_0 = (int)parameters[\"__uloop_literal_0\"];"
                }));
        }

        /// <summary>
        /// Pins long-suffixed integer literal hoisting.
        /// </summary>
        [Test]
        public void Rewrite_WhenLongLiteral_ShouldHoistAsLong()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return 10L;");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].TypeName, Is.EqualTo("long"));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(10L));
        }

        /// <summary>
        /// Pins multiple integer literals hoisted in source order.
        /// </summary>
        [Test]
        public void Rewrite_WhenMultipleIntegerLiterals_ShouldHoistInOrder()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return 1 + 2;");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0 + __uloop_literal_1;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(2));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(1));
            Assert.That(result.Bindings[1].Value, Is.EqualTo(2));
        }

        /// <summary>
        /// Pins regular string literal hoisting with the unescaped value stored in the binding.
        /// </summary>
        [Test]
        public void Rewrite_WhenRegularStringLiteral_ShouldHoistUnescapedValue()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return \"hello\";");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].TypeName, Is.EqualTo("string"));
            Assert.That(result.Bindings[0].Value, Is.EqualTo("hello"));
            Assert.That(
                result.DeclarationLines,
                Is.EqualTo(new[]
                {
                    "string __uloop_literal_0 = (string)parameters[\"__uloop_literal_0\"];"
                }));
        }

        /// <summary>
        /// Pins escape-sequence unescaping for regular string literals.
        /// </summary>
        [Test]
        public void Rewrite_WhenStringLiteralHasEscape_ShouldStoreUnescapedBindingValue()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return \"a\\nb\";");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].Value, Is.EqualTo("a\nb"));
        }

        /// <summary>
        /// Pins verbatim strings remaining inline without hoisting.
        /// </summary>
        [Test]
        public void Rewrite_WhenVerbatimString_ShouldKeepInline()
        {
            string source = "return @\"verbatim\";";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.DeclarationLines, Is.Empty);
        }

        /// <summary>
        /// Pins character literals remaining inline without hoisting.
        /// </summary>
        [Test]
        public void Rewrite_WhenCharLiteral_ShouldKeepInline()
        {
            string source = "return 'x';";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
        }

        /// <summary>
        /// Pins interpolated strings remaining opaque, including nested integer tokens.
        /// </summary>
        [Test]
        public void Rewrite_WhenInterpolatedString_ShouldKeepOpaque()
        {
            string source = "return $\"hi{1}\";";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
        }

        /// <summary>
        /// Pins decimal tokens remaining inline because the integer scanner stops at the decimal point.
        /// </summary>
        [Test]
        public void Rewrite_WhenDecimalLiteral_ShouldKeepInline()
        {
            string source = "return 1.5;";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
        }

        /// <summary>
        /// Pins sources with no hoistable literals remaining unchanged.
        /// </summary>
        [Test]
        public void Rewrite_WhenNoHoistableLiteral_ShouldReturnOriginalSource()
        {
            string source = "return null;";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.DeclarationLines, Is.Empty);
        }

        /// <summary>
        /// Pins comments remaining intact while trailing literals still hoist.
        /// </summary>
        [Test]
        public void Rewrite_WhenCommentPrecedesLiteral_ShouldPreserveCommentAndHoistLiteral()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("// note\nreturn 7;");

            Assert.That(result.RewrittenSource, Is.EqualTo("// note\nreturn __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(7));
        }

        /// <summary>
        /// Pins block-comment copy path: comment text stays intact while a following integer still hoists.
        /// </summary>
        [Test]
        public void Rewrite_WhenBlockCommentPrecedesLiteral_ShouldPreserveCommentAndHoistLiteral()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("/* c */ return 1;");

            Assert.That(result.RewrittenSource, Is.EqualTo("/* c */ return __uloop_literal_0;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(1));
        }

        /// <summary>
        /// Pins unterminated block comments consuming the remainder of the source through EOF.
        /// </summary>
        [Test]
        public void Rewrite_WhenUnterminatedBlockComment_ShouldTreatRemainderAsComment()
        {
            string source = "/* unterminated";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.DeclarationLines, Is.Empty);
        }

        /// <summary>
        /// Pins mixed string and integer hoisting in a single expression.
        /// </summary>
        [Test]
        public void Rewrite_WhenStringAndIntegerLiterals_ShouldHoistBoth()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(
                "return \"count\" + 3;");

            Assert.That(result.RewrittenSource, Is.EqualTo("return __uloop_literal_0 + __uloop_literal_1;"));
            Assert.That(result.Bindings, Has.Count.EqualTo(2));
            Assert.That(result.Bindings[0].Value, Is.EqualTo("count"));
            Assert.That(result.Bindings[1].Value, Is.EqualTo(3));
        }
    }
}
