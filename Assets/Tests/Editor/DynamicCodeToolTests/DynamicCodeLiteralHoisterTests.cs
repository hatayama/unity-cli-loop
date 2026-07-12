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

        [Test]
        public void Rewrite_WhenIntegerLiteralIsInsideStaticLocalFunction_ShouldKeepLiteralInline()
        {
            string source = @"int Compute(int value)
{
    static int Double(int x) => x * 2;
    return Double(value);
}
return Compute(3);";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Does.Contain("x * 2"));
            Assert.That(result.RewrittenSource, Does.Contain("return Compute(__uloop_literal_0)"));
            Assert.That(result.RewrittenSource, Does.Not.Contain("x * __uloop_literal"));
            Assert.That(result.Bindings, Has.Count.EqualTo(1));
            Assert.That(result.Bindings[0].Value, Is.EqualTo(3));
        }

        [Test]
        public void Rewrite_WhenStringLiteralIsInsideStaticLocalFunctionBlockBody_ShouldKeepLiteralInline()
        {
            string source = @"string Build()
{
    static string Label() { return ""ok""; }
    return Label();
}
return Build();";

            HoistedLiteralRewriteResult result = DynamicCodeLiteralHoister.Rewrite(source);

            Assert.That(result.RewrittenSource, Does.Contain("return \"ok\""));
            Assert.That(result.RewrittenSource, Does.Not.Contain("__uloop_literal_0"));
            Assert.That(result.Bindings, Is.Empty);
        }
    }
}
