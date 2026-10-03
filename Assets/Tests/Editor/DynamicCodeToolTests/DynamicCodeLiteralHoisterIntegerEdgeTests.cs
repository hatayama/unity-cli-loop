using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies which long-suffixed and trailing integer literals the hoister turns into parameters and which
    /// it leaves in the source.
    /// </summary>
    public sealed class DynamicCodeLiteralHoisterIntegerEdgeTests
    {
        /// <summary>
        /// Verifies an L suffix followed by more identifier characters is not a long literal and stays in place.
        /// </summary>
        [Test]
        public void Rewrite_WithAnLSuffixInsideAnIdentifier_LeavesTheTextInPlace()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("var x = 10Lx;");

            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.RewrittenSource, Is.EqualTo("var x = 10Lx;"));
        }

        /// <summary>
        /// Verifies a long literal too large for long is left in the source for the compiler to report.
        /// </summary>
        [Test]
        public void Rewrite_WithALongLiteralBeyondTheRange_LeavesItInPlace()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("long x = 99999999999999999999L;");

            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.RewrittenSource, Is.EqualTo("long x = 99999999999999999999L;"));
        }

        /// <summary>
        /// Verifies an integer at the very end of the source is still hoisted.
        /// </summary>
        [Test]
        public void Rewrite_WithAnIntegerAtTheEnd_HoistsIt()
        {
            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite("return 5");

            Assert.That(result.Bindings.Count, Is.EqualTo(1));
            Assert.That(result.Bindings[0].TypeName, Is.EqualTo("int"));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(5));
            Assert.That(result.RewrittenSource, Is.EqualTo("return " + result.Bindings[0].ParameterName));
        }
    }
}
