using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration resolves the type and namespace that enclose a member.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingTypeScopeRulesTests
    {
        /// <summary>
        /// Verifies a closing angle bracket without a matching opening bracket yields minus one.
        /// </summary>
        [Test]
        public void FindGenericArgumentListStartIndex_WhenNoOpeningAngle_ReturnsMinusOne()
        {
            string source = "count > limit";

            int result = ThirdPartyToolMigrationTimingTypeScopeRules.FindGenericArgumentListStartIndex(
                source,
                source.IndexOf('>'));

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a type declaration inside a comment is not reported as an enclosing type.
        /// </summary>
        [Test]
        public void ReadContainingTypeName_WhenTypeDeclarationIsInsideComment_IgnoresIt()
        {
            string source = "// class Legacy\nclass Runner\n{\n    void Run() { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string result = ThirdPartyToolMigrationTimingTypeScopeRules.ReadContainingTypeName(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo("Runner"));
        }

        /// <summary>
        /// Verifies a positional record parameter list is not inside the record body.
        /// </summary>
        [Test]
        public void ReadContainingTypeName_WhenMemberPrecedesTypeBody_ReturnsEmpty()
        {
            string source = "record Runner(int Value)\n{\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string result = ThirdPartyToolMigrationTimingTypeScopeRules.ReadContainingTypeName(
                source,
                codeTextMask,
                source.IndexOf('('));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a member inside a namespace but outside any type has no containing type.
        /// </summary>
        [Test]
        public void ReadContainingTypeName_WhenMemberIsOutsideAnyType_ReturnsEmpty()
        {
            string source = "namespace Tools\n{\n    delegate void Run(int value);\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string result = ThirdPartyToolMigrationTimingTypeScopeRules.ReadContainingTypeName(
                source,
                codeTextMask,
                source.IndexOf('('));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a namespace declaration inside a comment does not change the enclosing namespace.
        /// </summary>
        [Test]
        public void ReadNamespaceName_WhenNamespaceDeclarationIsInsideComment_IgnoresIt()
        {
            string source = "namespace Tools\n{\n    // namespace Legacy;\n    class Runner { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string result = ThirdPartyToolMigrationTimingTypeScopeRules.ReadNamespaceName(
                source,
                codeTextMask,
                source.IndexOf("Runner", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo("Tools"));
        }

        /// <summary>
        /// Verifies a file-scoped namespace declaration applies to the members after it.
        /// </summary>
        [Test]
        public void ReadNamespaceName_WhenNamespaceIsFileScoped_ReturnsNamespace()
        {
            string source = "namespace Tools;\nclass Runner { }\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            string result = ThirdPartyToolMigrationTimingTypeScopeRules.ReadNamespaceName(
                source,
                codeTextMask,
                source.IndexOf("Runner", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo("Tools"));
        }

        /// <summary>
        /// Verifies a brace inside a comment in a type header is not taken as the type body.
        /// </summary>
        [Test]
        public void FindTypeBodyOpenBraceIndex_WhenCommentContainsBrace_ReturnsCodeBrace()
        {
            string source = "class Runner /* { */ : RunnerBase\n{\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingTypeScopeRules.FindTypeBodyOpenBraceIndex(
                source,
                codeTextMask,
                "class Runner".Length);

            Assert.That(result, Is.EqualTo(source.IndexOf("\n{", StringComparison.Ordinal) + 1));
        }

        /// <summary>
        /// Verifies a positional record without a body has no type body even when a later type has one.
        /// </summary>
        [Test]
        public void FindTypeBodyOpenBraceIndex_WhenDeclarationEndsWithSemicolon_ReturnsMinusOne()
        {
            string source = "record Point(int X);\nclass Runner { }\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingTypeScopeRules.FindTypeBodyOpenBraceIndex(
                source,
                codeTextMask,
                "record Point".Length);

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a type header at the end of the source has no type body.
        /// </summary>
        [Test]
        public void FindTypeBodyOpenBraceIndex_WhenSourceEndsBeforeBody_ReturnsMinusOne()
        {
            string source = "class Runner";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingTypeScopeRules.FindTypeBodyOpenBraceIndex(
                source,
                codeTextMask,
                "class".Length);

            Assert.That(result, Is.EqualTo(-1));
        }
    }
}
