using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies type-name detection ignores declarations that appear only in comments.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCodeTextDetectionRulesTests
    {
        /// <summary>
        /// Verifies a type declaration inside a comment is not reported while the real declaration is.
        /// </summary>
        [Test]
        public void GetDeclaredTypeNames_WhenDeclarationIsInComment_ReturnsOnlyCodeDeclarations()
        {
            string source = "// class HiddenTool\nclass VisibleTool {}";

            string[] typeNames = ThirdPartyToolMigrationCodeTextDetectionRules.GetDeclaredTypeNames(source);

            Assert.That(typeNames, Is.EqualTo(new[] { "VisibleTool" }));
        }
    }
}
