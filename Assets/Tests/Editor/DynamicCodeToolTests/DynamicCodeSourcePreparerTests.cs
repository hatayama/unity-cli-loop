using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Source Preparer behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeSourcePreparerTests
    {
        [Test]
        public void Prepare_WhenOnlyLiteralValuesDiffer_ShouldGenerateSamePreparedSource()
        {
            PreparedDynamicCode first = DynamicCodeSourcePreparer.Prepare(
                "int benchNonce = 100; return benchNonce;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);
            PreparedDynamicCode second = DynamicCodeSourcePreparer.Prepare(
                "int benchNonce = 200; return benchNonce;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.AreEqual(first.PreparedSource, second.PreparedSource);
            Assert.AreEqual(1, first.HoistedLiteralBindings.Count);
            Assert.AreEqual(1, second.HoistedLiteralBindings.Count);
            Assert.AreEqual(100, first.HoistedLiteralBindings[0].Value);
            Assert.AreEqual(200, second.HoistedLiteralBindings[0].Value);
        }

        [Test]
        public void Prepare_WhenStringLiteralExists_ShouldEmitPreambleBeforeUserCodeMarker()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"hello\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            StringAssert.Contains("string __uloop_literal_0 = (string)parameters[\"__uloop_literal_0\"];", prepared.PreparedSource);
            Assert.Less(
                prepared.PreparedSource.IndexOf("string __uloop_literal_0 = (string)parameters[\"__uloop_literal_0\"];", System.StringComparison.Ordinal),
                prepared.PreparedSource.IndexOf(WrapperTemplate.UserCodeStartMarker, System.StringComparison.Ordinal));
        }

        [Test]
        public void Prepare_WhenInterpolatedStringExists_ShouldSkipLiteralHoisting()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return $\"outer {$\"inner {1}\"}\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return $\"outer {$\"inner {1}\"}\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenInterpolatedRawStringExists_ShouldSkipLiteralHoisting()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return $\"\"\"outer {1}\"\"\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return $\"\"\"outer {1}\"\"\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenFloatLiteralExists_ShouldNotHoistNumericLiteral()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "float blend = 1.5f; return blend;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("float blend = 1.5f;", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenDecimalLiteralExists_ShouldNotHoistNumericLiteral()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "double ratio = 0.25; return ratio;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("double ratio = 0.25;", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenVerbatimStringContainsCommentLikeText_ShouldNotHoistInsideLiteral()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return @\"http://127.0.0.1\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return @\"http://127.0.0.1\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenCharLiteralContainsDigit_ShouldNotHoistInsideLiteral()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "char c = '1'; return c;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("char c = '1';", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenCharLiteralUsesUnicodeEscape_ShouldKeepEntireLiteral()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "char c = '\\u0027'; return c;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("char c = '\\u0027';", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenRegularStringUsesHexEscape_ShouldHoistDecodedValue()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\x41\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.AreEqual(1, prepared.HoistedLiteralBindings.Count);
            Assert.AreEqual("A", prepared.HoistedLiteralBindings[0].Value);
        }

        [Test]
        public void Prepare_WhenRegularStringUsesUnicodeEscape_ShouldHoistDecodedValue()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\U0001F600\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.AreEqual(1, prepared.HoistedLiteralBindings.Count);
            Assert.AreEqual(char.ConvertFromUtf32(0x1F600), prepared.HoistedLiteralBindings[0].Value);
        }

        [Test]
        public void Prepare_WhenRegularStringUsesBackspaceEscape_ShouldHoistDecodedValue()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\b\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.AreEqual(1, prepared.HoistedLiteralBindings.Count);
            Assert.AreEqual("\b", prepared.HoistedLiteralBindings[0].Value);
        }

        [Test]
        public void Prepare_WhenRegularStringContainsUnicodeQuoteEscape_ShouldHoistDecodedValue()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\u0022\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.AreEqual(1, prepared.HoistedLiteralBindings.Count);
            Assert.AreEqual("\"", prepared.HoistedLiteralBindings[0].Value);
        }

        [Test]
        public void Prepare_WhenRegularStringContainsTruncatedUnicodeEscape_ShouldLeaveLiteralInSource()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\u00\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return \"\\u00\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenRegularStringContainsUnknownEscape_ShouldLeaveLiteralInSource()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\q\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return \"\\q\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenRegularStringContainsInvalidNumericEscape_ShouldLeaveLiteralInSource()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return \"\\8\";",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return \"\\8\";", prepared.PreparedSource);
        }

        [Test]
        public void Prepare_WhenIntegerLiteralExceedsIntRange_ShouldLeaveLiteralInSource()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                "return 3000000000;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(prepared.PreparedSource);
            Assert.AreEqual(0, prepared.HoistedLiteralBindings.Count);
            StringAssert.Contains("return 3000000000;", prepared.PreparedSource);
        }
    }
}
