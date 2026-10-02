using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration finds the body range of a method implementation.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingMethodBodyRulesTests
    {
        /// <summary>
        /// Verifies a brace inside a comment before the body is ignored when locating a block body.
        /// </summary>
        [Test]
        public void FindMethodImplementationUsageRange_WhenCommentBeforeBodyContainsBrace_ReturnsBlockBodyRange()
        {
            string source = "void Run() /* { */ { Stop(); }";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingMethodBodyRules.FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    source.IndexOf(')') + 1);

            Assert.That(startIndex, Is.EqualTo(source.IndexOf("{ Stop", StringComparison.Ordinal) + 1));
            Assert.That(endIndex, Is.EqualTo(source.LastIndexOf('}')));
        }

        /// <summary>
        /// Verifies a block body that never closes yields no usage range.
        /// </summary>
        [Test]
        public void FindMethodImplementationUsageRange_WhenBlockBodyIsUnterminated_ReturnsMinusOnes()
        {
            string source = "void Run() { Stop();";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingMethodBodyRules.FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    source.IndexOf(')') + 1);

            Assert.That(startIndex, Is.EqualTo(-1));
            Assert.That(endIndex, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies an expression-bodied member yields the range from after the arrow to its terminating semicolon.
        /// </summary>
        [Test]
        public void FindMethodImplementationUsageRange_WhenExpressionBodyIsTerminated_ReturnsExpressionRange()
        {
            string source = "void Run() => Stop();";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingMethodBodyRules.FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    source.IndexOf(')') + 1);

            Assert.That(startIndex, Is.EqualTo(source.IndexOf("=>", StringComparison.Ordinal) + 2));
            Assert.That(endIndex, Is.EqualTo(source.LastIndexOf(';')));
        }

        /// <summary>
        /// Verifies an expression body without a terminating semicolon yields no usage range.
        /// </summary>
        [Test]
        public void FindMethodImplementationUsageRange_WhenExpressionBodyIsUnterminated_ReturnsMinusOnes()
        {
            string source = "void Run() => Stop()";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingMethodBodyRules.FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    source.IndexOf(')') + 1);

            Assert.That(startIndex, Is.EqualTo(-1));
            Assert.That(endIndex, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a declaration that ends with the source after its parameter list yields no usage range.
        /// </summary>
        [Test]
        public void FindMethodImplementationUsageRange_WhenSourceEndsAfterParameterList_ReturnsMinusOnes()
        {
            string source = "void Run()  ";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingMethodBodyRules.FindMethodImplementationUsageRange(
                    source,
                    codeTextMask,
                    source.IndexOf(')') + 1);

            Assert.That(startIndex, Is.EqualTo(-1));
            Assert.That(endIndex, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a semicolon inside a comment does not end an expression-bodied member.
        /// </summary>
        [Test]
        public void FindExpressionBodiedMemberSemicolonIndex_WhenCommentContainsSemicolon_ReturnsCodeSemicolon()
        {
            string source = " Compute(1) /* ; */ + 2;";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingMethodBodyRules.FindExpressionBodiedMemberSemicolonIndex(
                source,
                codeTextMask,
                0);

            Assert.That(result, Is.EqualTo(source.LastIndexOf(';')));
        }

        /// <summary>
        /// Verifies a semicolon inside a brace block of an expression body does not end the member.
        /// </summary>
        [Test]
        public void FindExpressionBodiedMemberSemicolonIndex_WhenBracesContainSemicolon_ReturnsTerminatingSemicolon()
        {
            string source = " () => { a; };";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingMethodBodyRules.FindExpressionBodiedMemberSemicolonIndex(
                source,
                codeTextMask,
                0);

            Assert.That(result, Is.EqualTo(source.Length - 1));
        }
    }
}
