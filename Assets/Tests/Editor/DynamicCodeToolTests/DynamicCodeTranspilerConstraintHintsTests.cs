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
        [Test]
        public void TryBuildHint_WhenCs8421ReferencesHoistedLiteral_ShouldReturnStaticLocalFunctionGuidance()
        {
            bool built = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                "CS8421",
                "CS8421: A static local function cannot contain a reference to '__uloop_literal_0'.",
                out string hint,
                out string suggestion);

            Assert.That(built, Is.True);
            Assert.That(hint, Does.Contain("Static local functions"));
            Assert.That(suggestion, Does.Contain("inline constants"));
        }

        [Test]
        public void TryBuildHint_WhenCs1503CannotConvertIntToByte_ShouldReturnExplicitCastGuidance()
        {
            bool built = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                "CS1503",
                "CS1503: Argument 1: cannot convert from 'int' to 'byte'",
                out string hint,
                out string suggestion);

            Assert.That(built, Is.True);
            Assert.That(hint, Does.Contain("Color32"));
            Assert.That(suggestion, Does.Contain("(byte)255"));
        }
    }
}
