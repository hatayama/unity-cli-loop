using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Source Shaper behavior.
    /// </summary>
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
        public void Analyze_WhenAttributedTypeHasAccessModifier_ShouldDetectTypeDeclaration()
        {
            // Verifies attributed public types are not mistaken for top-level statements.
            string source = "[System.Serializable] public sealed class Example {}";

            SourceShapeResult result = SourceShaper.Analyze(source);

            Assert.IsTrue(result.HasTypeDeclaration);
            Assert.IsFalse(result.HasTopLevelStatements);
        }
    }
}
