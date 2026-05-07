using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Literal Hoister behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeLiteralHoisterTests
    {
        [Test]
        public void Rewrite_WhenInterpolatedHoleContainsNestedStringLiteral_ShouldKeepInterpolatedStringOpaque()
        {
            string source = @"return $""value: {int.Parse(""2"") + 1}"";";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Is.EqualTo(source));
            Assert.That(result.Bindings, Is.Empty);
            Assert.That(result.DeclarationLines, Is.Empty);
        }
    }
}
