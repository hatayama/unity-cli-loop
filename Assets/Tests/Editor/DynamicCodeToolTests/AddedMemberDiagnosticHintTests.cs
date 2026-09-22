using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Covers the missing-member diagnostic explanation for members hot reload added.
    /// </summary>
    [TestFixture]
    public sealed class AddedMemberDiagnosticHintTests
    {
        private const string MissingInstanceMemberMessage =
            "'Player' does not contain a definition for 'ComputeScore' and no accessible extension "
            + "method 'ComputeScore' accepting a first argument of type 'Player' could be found "
            + "(are you missing a using directive or an assembly reference?)";

        private const string MissingStaticMemberMessage =
            "'Player' does not contain a definition for 'DefaultScore'";

        private const string NameNotInContextMessage =
            "The name 'addedCounter' does not exist in the current context";

        private const string ComputeScoreHint =
            "'ComputeScore' matches a member hot reload added, which is active now. An added member "
            + "is not visible to the compilation of a dynamic-code snippet, which compiles against "
            + "the compiled assemblies only, and it is not visible through reflection either. Run "
            + "'uloop compile' to make the added member compiled, then rerun, or read the state "
            + "through a member that was already compiled.";

        private const string CompileSuggestion =
            "Run 'uloop compile' to make the added member compiled, then rerun";

        private const string CompiledMemberSuggestion =
            "Read the state through a member that was already compiled instead of the added one";

        /// <summary>
        /// Verifies a CS1061 whose missing member name is an active added member gets the
        /// added-member hint and both suggestions.
        /// </summary>
        [Test]
        public void TryBuild_WhenMissingInstanceMemberIsAnActiveAddedMember_ExplainsTheAddedMember()
        {
            bool built = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                new List<string> { "ComputeScore" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Is.EqualTo(ComputeScoreHint));
            Assert.That(suggestions, Is.EqualTo(new[] { CompileSuggestion, CompiledMemberSuggestion }));
        }

        /// <summary>
        /// Verifies the member name is read from the second quoted phrase: a set that holds only the
        /// declaring type name of a CS1061 is not treated as a match.
        /// </summary>
        [Test]
        public void TryBuild_WhenOnlyTheDeclaringTypeNameMatches_DoesNotExplain()
        {
            bool built = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                new List<string> { "Player" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }

        /// <summary>
        /// Verifies a CS0117 whose missing static member name is an active added member gets the
        /// added-member hint.
        /// </summary>
        [Test]
        public void TryBuild_WhenMissingStaticMemberIsAnActiveAddedMember_ExplainsTheAddedMember()
        {
            bool built = AddedMemberDiagnosticHint.TryBuild(
                "CS0117",
                MissingStaticMemberMessage,
                new List<string> { "DefaultScore" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Does.StartWith("'DefaultScore' matches a member hot reload added"));
            Assert.That(suggestions, Is.EqualTo(new[] { CompileSuggestion, CompiledMemberSuggestion }));
        }

        /// <summary>
        /// Verifies a CS0103 reads its name from the first quoted phrase, because the message quotes
        /// no declaring type.
        /// </summary>
        [Test]
        public void TryBuild_WhenNameNotInContextIsAnActiveAddedMember_ExplainsTheAddedMember()
        {
            bool built = AddedMemberDiagnosticHint.TryBuild(
                "CS0103",
                NameNotInContextMessage,
                new List<string> { "addedCounter" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Does.StartWith("'addedCounter' matches a member hot reload added"));
            Assert.That(suggestions, Is.EqualTo(new[] { CompileSuggestion, CompiledMemberSuggestion }));
        }

        /// <summary>
        /// Verifies no hint is built when hot reload reports no added members, whether the slot
        /// answered null or an empty set.
        /// </summary>
        [Test]
        public void TryBuild_WhenNoAddedMembersAreActive_DoesNotExplain()
        {
            bool builtWithoutNames = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                null,
                out string hintWithoutNames,
                out List<string> suggestionsWithoutNames);

            bool builtWithEmptyNames = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                new List<string>(),
                out _,
                out _);

            Assert.That(builtWithoutNames, Is.False);
            Assert.That(hintWithoutNames, Is.Empty);
            Assert.That(suggestionsWithoutNames, Is.Null);
            Assert.That(builtWithEmptyNames, Is.False);
        }

        /// <summary>
        /// Verifies the match is exact: a prefix of the added member name and a case-only variant of
        /// it are both rejected.
        /// </summary>
        [Test]
        public void TryBuild_WhenTheAddedMemberNameOnlyPartlyMatches_DoesNotExplain()
        {
            bool builtForPrefix = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                new List<string> { "ComputeScoreDetailed" },
                out _,
                out _);

            bool builtForCaseVariant = AddedMemberDiagnosticHint.TryBuild(
                "CS1061",
                MissingInstanceMemberMessage,
                new List<string> { "computescore" },
                out _,
                out _);

            Assert.That(builtForPrefix, Is.False);
            Assert.That(builtForCaseVariant, Is.False);
        }

        /// <summary>
        /// Verifies an error code this hint does not cover is left to the existing hints, even when
        /// the quoted name matches an added member.
        /// </summary>
        [Test]
        public void TryBuild_WhenTheErrorCodeIsNotAMissingMemberCode_DoesNotExplain()
        {
            bool built = AddedMemberDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'ComputeScore' could not be found",
                new List<string> { "ComputeScore" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }
    }
}
