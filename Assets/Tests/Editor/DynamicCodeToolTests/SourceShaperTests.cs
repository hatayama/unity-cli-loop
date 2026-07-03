using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
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

        [Test]
        public void Analyze_WhenVerbatimUsingAlias_ShouldRecordNormalizedAliasName()
        {
            SourceShapeResult result = SourceShaper.Analyze(
                "using @Object = System.Object;\nreturn new @Object();");

            Assert.That(result.AliasedNames, Does.Contain("Object"));
        }

        [Test]
        public void Analyze_WhenGlobalUsingAliasHasComments_ShouldRecordAliasName()
        {
            SourceShapeResult result = SourceShaper.Analyze(
                "global /* comment */ using /* comment */ Random /* comment */ = UnityEngine.Random;\nreturn Random.Range(0, 1);");

            Assert.That(result.AliasedNames, Does.Contain("Random"));
        }

        [Test]
        public void Analyze_WhenUsingAliasHasComments_ShouldRecordAliasName()
        {
            SourceShapeResult result = SourceShaper.Analyze(
                "using /* comment */ Object /* comment */ = UnityEngine.Object;\nreturn null;");

            Assert.That(result.AliasedNames, Does.Contain("Object"));
        }
    }
}
