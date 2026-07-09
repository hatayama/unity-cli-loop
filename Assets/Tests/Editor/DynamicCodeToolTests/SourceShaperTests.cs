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

        [Test]
        public void Analyze_WhenVerbatimUsingAlias_ShouldRecordNormalizedAliasName()
        {
            // Verifies "using @Object = ..." records the alias name without the leading '@'.
            SourceShapeResult result = SourceShaper.Analyze(
                "using @Object = System.Object;\nreturn new @Object();");

            Assert.That(result.AliasedNames, Does.Contain("Object"));
        }

        [Test]
        public void Analyze_WhenGlobalUsingAliasHasComments_ShouldRecordAliasName()
        {
            // Verifies comments between "global", "using", and the alias name do not block alias detection.
            SourceShapeResult result = SourceShaper.Analyze(
                "global /* comment */ using /* comment */ Random /* comment */ = UnityEngine.Random;\nreturn Random.Range(0, 1);");

            Assert.That(result.AliasedNames, Does.Contain("Random"));
        }

        [Test]
        public void Analyze_WhenUsingAliasHasComments_ShouldRecordAliasName()
        {
            // Verifies comments between "using" and the alias name do not block alias detection.
            SourceShapeResult result = SourceShaper.Analyze(
                "using /* comment */ Object /* comment */ = UnityEngine.Object;\nreturn null;");

            Assert.That(result.AliasedNames, Does.Contain("Object"));
        }

        [Test]
        public void Analyze_WhenUsingVarHasCommentBeforeVar_ShouldTreatAsTopLevelStatement()
        {
            // Verifies a comment between "using" and "var" still resolves to a using-statement,
            // not a using-directive (regression guard for the SkipWhitespaceAndComments fix).
            SourceShapeResult result = SourceShaper.Analyze(
                "using /* comment */ var scope = new System.IO.MemoryStream();\nreturn null;");

            Assert.IsTrue(result.HasTopLevelStatements);
            Assert.AreEqual(0, result.UsingDirectives.Count);
        }
    }
}
