using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration resolves the declared type of an identifier used as a call target.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingTypeResolutionRulesTests
    {
        /// <summary>
        /// Verifies an undeclared identifier outside any type has no type name candidates.
        /// </summary>
        [Test]
        public void ReadIdentifierTypeNameCandidates_WhenUndeclaredOutsideType_ReturnsEmpty()
        {
            string source = "runner.Run();\n";

            string[] result = ThirdPartyToolMigrationTimingTypeResolutionRules.ReadIdentifierTypeNameCandidates(
                source,
                "runner",
                source.IndexOf("Run(", StringComparison.Ordinal));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies an undeclared identifier inside a type has no type name candidates even with imported namespaces.
        /// </summary>
        [Test]
        public void ReadIdentifierTypeNameCandidates_WhenUndeclaredInsideType_ReturnsEmpty()
        {
            string source =
                "using Imported;\nclass Caller\n{\n    void Call()\n    {\n        runner.Run();\n    }\n}\n";

            string[] result = ThirdPartyToolMigrationTimingTypeResolutionRules.ReadIdentifierTypeNameCandidates(
                source,
                "runner",
                source.IndexOf("runner.Run", StringComparison.Ordinal));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a qualified declared type is returned alone without namespace-based candidates.
        /// </summary>
        [Test]
        public void ReadIdentifierTypeNameCandidates_WhenDeclaredTypeIsQualified_ReturnsOnlyThatType()
        {
            string source =
                "using Imported;\nclass Caller\n{\n    void Call(Tools.Runner runner)\n    {\n        runner.Run();\n    }\n}\n";

            string[] result = ThirdPartyToolMigrationTimingTypeResolutionRules.ReadIdentifierTypeNameCandidates(
                source,
                "runner",
                source.IndexOf("runner.Run", StringComparison.Ordinal));

            Assert.That(result, Is.EqualTo(new[] { "Tools.Runner" }));
        }

        /// <summary>
        /// Verifies a brace inside a comment does not make a field declaration look nested.
        /// </summary>
        [Test]
        public void IsTopLevelTypeMemberMatch_WhenCommentContainsBrace_ReturnsTrue()
        {
            string searchSource = "/* { */ Runner runner;";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(searchSource);

            bool result = ThirdPartyToolMigrationTimingTypeResolutionRules.IsTopLevelTypeMemberMatch(
                searchSource,
                0,
                codeTextMask,
                searchSource.IndexOf("Runner", StringComparison.Ordinal));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a type declaration inside a comment does not narrow the containing type body.
        /// </summary>
        [Test]
        public void ReadInnermostContainingTypeBodyRange_WhenTypeDeclarationIsInsideComment_ReturnsEnclosingTypeBody()
        {
            string source = "class Runner\n{\n    // class Legacy\n    void Run() { Stop(); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingTypeResolutionRules.ReadInnermostContainingTypeBodyRange(
                    source,
                    codeTextMask,
                    source.IndexOf("Stop", StringComparison.Ordinal));

            Assert.That(startIndex, Is.EqualTo(source.IndexOf('{') + 1));
            Assert.That(endIndex, Is.EqualTo(source.LastIndexOf('}')));
        }

        /// <summary>
        /// Verifies a positional record parameter list is not inside the record body.
        /// </summary>
        [Test]
        public void ReadInnermostContainingTypeBodyRange_WhenMemberPrecedesTypeBody_ReturnsMinusOnes()
        {
            string source = "record Runner(int Value)\n{\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            (int startIndex, int endIndex) =
                ThirdPartyToolMigrationTimingTypeResolutionRules.ReadInnermostContainingTypeBodyRange(
                    source,
                    codeTextMask,
                    source.IndexOf('('));

            Assert.That(startIndex, Is.EqualTo(-1));
            Assert.That(endIndex, Is.EqualTo(-1));
        }
    }
}
