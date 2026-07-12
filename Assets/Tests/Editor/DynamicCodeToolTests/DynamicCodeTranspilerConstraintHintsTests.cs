using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies transpiler constraint hint generation.
    /// </summary>
    [TestFixture]
    public class DynamicCodeTranspilerConstraintHintsTests
    {
        /// <summary>
        /// Verifies CS8421 hoisted-literal failures suggest removing the static modifier.
        /// </summary>
        [Test]
        public void TryBuildHint_WhenCs8421ReferencesHoistedLiteral_ShouldReturnStaticLocalFunctionGuidance()
        {
            (bool matched, string hint, string suggestion) = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                "CS8421",
                "CS8421: A static local function cannot contain a reference to '__uloop_literal_0'.");

            Assert.That(matched, Is.True);
            Assert.That(hint, Does.Contain("recognized static local function bodies"));
            Assert.That(suggestion, Is.EqualTo("Remove the `static` modifier from the local function."));
        }

        /// <summary>
        /// Verifies CS8820 static lambda failures suggest removing the static modifier.
        /// </summary>
        [Test]
        public void TryBuildHint_WhenCs8820ReferencesHoistedLiteral_ShouldReturnStaticLambdaGuidance()
        {
            (bool matched, string hint, string suggestion) = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                "CS8820",
                "CS8820: A static anonymous function cannot contain a reference to '__uloop_literal_0'.");

            Assert.That(matched, Is.True);
            Assert.That(hint, Does.Contain("Static lambdas"));
            Assert.That(suggestion, Is.EqualTo("Remove the `static` modifier from the lambda."));
        }

        /// <summary>
        /// Verifies int-to-byte conversion failures include explicit cast guidance for Color32.
        /// </summary>
        [Test]
        public void TryBuildHint_WhenCs1503CannotConvertIntToByte_ShouldReturnExplicitCastGuidance()
        {
            (bool matched, string hint, string suggestion) = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                "CS1503",
                "CS1503: Argument 1: cannot convert from 'int' to 'byte'");

            Assert.That(matched, Is.True);
            Assert.That(hint, Does.Contain("Color32"));
            Assert.That(suggestion, Does.Contain("(byte)255"));
        }
    }
}
