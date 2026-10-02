using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration reads and compares type names in caller source.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingTypeNameRulesTests
    {
        /// <summary>
        /// Verifies a using directive inside a block comment is not reported as an imported namespace.
        /// </summary>
        [Test]
        public void ReadImportedNamespaceNames_WhenUsingIsInsideComment_IgnoresIt()
        {
            string source = "using Alpha;\n/*\nusing Beta;\n*/\nclass Runner { }\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string[] result = ThirdPartyToolMigrationTimingTypeNameRules.ReadImportedNamespaceNames(
                source,
                codeTextMask,
                source.IndexOf("class", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo(new[] { "Alpha" }));
        }

        /// <summary>
        /// Verifies a using directive after the inspected position is not reported as an imported namespace.
        /// </summary>
        [Test]
        public void ReadImportedNamespaceNames_WhenUsingAppearsAfterPosition_IgnoresIt()
        {
            string source = "using Alpha;\nclass Runner { }\nnamespace Inner\n{\n    using Beta;\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string[] result = ThirdPartyToolMigrationTimingTypeNameRules.ReadImportedNamespaceNames(
                source,
                codeTextMask,
                source.IndexOf("class", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo(new[] { "Alpha" }));
        }

        /// <summary>
        /// Verifies a target expression without a member after its last dot has no last member identifier.
        /// </summary>
        [TestCase("runner")]
        [TestCase("this.")]
        public void ReadLastMemberIdentifier_WhenNoMemberFollowsLastDot_ReturnsEmpty(string targetExpression)
        {
            string result = ThirdPartyToolMigrationTimingTypeNameRules.ReadLastMemberIdentifier(targetExpression);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a declaration without an initializer has no initializer type name.
        /// </summary>
        [TestCase(" ;")]
        [TestCase("")]
        public void ReadVarInitializerTypeName_WhenNoInitializerFollows_ReturnsEmpty(string remainder)
        {
            string source = "var runner" + remainder;

            string result = ThirdPartyToolMigrationTimingTypeNameRules.ReadVarInitializerTypeName(
                source,
                "var runner".Length);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies an initializer that is not an object creation has no initializer type name.
        /// </summary>
        [Test]
        public void ReadVarInitializerTypeName_WhenInitializerIsNotObjectCreation_ReturnsEmpty()
        {
            string source = "var runner = Create();";

            string result = ThirdPartyToolMigrationTimingTypeNameRules.ReadVarInitializerTypeName(
                source,
                "var runner".Length);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a generic object creation keeps its whole type argument list, including spaces inside it.
        /// </summary>
        [Test]
        public void ReadVarInitializerTypeName_WhenObjectCreationIsGeneric_ReturnsGenericTypeName()
        {
            string source = "var lookup = new Dictionary<int, string>();";

            string result = ThirdPartyToolMigrationTimingTypeNameRules.ReadVarInitializerTypeName(
                source,
                "var lookup".Length);

            Assert.That(result, Is.EqualTo("Dictionary<int, string>"));
        }

        /// <summary>
        /// Verifies a target-typed object creation has no initializer type name.
        /// </summary>
        [Test]
        public void ReadVarInitializerTypeName_WhenObjectCreationIsTargetTyped_ReturnsEmpty()
        {
            string source = "var runner = new();";

            string result = ThirdPartyToolMigrationTimingTypeNameRules.ReadVarInitializerTypeName(
                source,
                "var runner".Length);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies empty type names never match exactly, even when both sides are empty.
        /// </summary>
        [TestCase("", "")]
        [TestCase("Runner", "")]
        public void IsExactTypeNameReference_WhenAnyNameIsEmpty_ReturnsFalse(
            string candidateTypeName,
            string expectedTypeName)
        {
            bool result = ThirdPartyToolMigrationTimingTypeNameRules.IsExactTypeNameReference(
                candidateTypeName,
                expectedTypeName);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies empty type names never match as references, even when both sides are empty.
        /// </summary>
        [TestCase("", "")]
        [TestCase("", "Runner")]
        public void IsTypeNameReference_WhenAnyNameIsEmpty_ReturnsFalse(
            string candidateTypeName,
            string expectedTypeName)
        {
            bool result = ThirdPartyToolMigrationTimingTypeNameRules.IsTypeNameReference(
                candidateTypeName,
                expectedTypeName);

            Assert.That(result, Is.False);
        }
    }
}
