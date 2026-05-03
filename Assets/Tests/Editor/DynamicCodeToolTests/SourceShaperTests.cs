using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.DynamicCodeToolTests
{
    [TestFixture]
    public class SourceShaperTests
    {
        [Test]
        public void WrapIfNeeded_WhenInterpolationHoleContainsNestedStringLiteral_ShouldWrapAsScript()
        {
            string source = "return $\"x{string.Concat(\"}\", \"z\")}y\";";

            string wrapped = SourceShaper.WrapIfNeeded(
                source,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(wrapped);
            StringAssert.Contains("return $\"x{string.Concat(\"}\", \"z\")}y\";", wrapped);
        }

        [Test]
        public void WrapIfNeeded_WhenInterpolationHoleContainsNestedInterpolatedString_ShouldWrapAsScript()
        {
            string source = "return $\"outer {$\"inner {1}\"}\";";

            string wrapped = SourceShaper.WrapIfNeeded(
                source,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.IsNotNull(wrapped);
            StringAssert.Contains("return $\"outer {$\"inner {1}\"}\";", wrapped);
        }

        [Test]
        public void HasTopLevelReturn_WhenInterpolationHoleContainsNestedStringLiteral_ShouldDetectReturn()
        {
            string source = "return $\"x{System.String.Concat(\"}\", \"z\")}y\";";

            bool hasReturn = TopLevelReturnDetector.HasTopLevelReturn(source);

            Assert.IsTrue(hasReturn);
        }
    }
}
