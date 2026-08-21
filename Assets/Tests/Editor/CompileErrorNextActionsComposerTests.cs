using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests language-version NextAction detection, wording, append behavior, and factory wiring.
    /// </summary>
    [TestFixture]
    public sealed class CompileErrorNextActionsComposerTests
    {
        private const string FileScopedNamespaceError =
            "error CS8370: Feature 'file-scoped namespace' is not available in C# 9.0. Please use language version 10.0 or greater.";

        private const string PrefixlessFileScopedNamespaceError =
            "CS8370: Feature 'file-scoped namespace' is not available in C# 9.0. Please use language version 10.0 or greater.";

        private const string FileScopedNamespaceNextAction =
            "error CS8370: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'file-scoped namespace' feature so the code compiles under C# 9.0.";

        private const string RecordsError =
            "error CS8400: Feature 'records' is not available in C# 8.0. Please use language version 9.0 or greater.";

        private const string RecordsNextAction =
            "error CS8400: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'records' feature so the code compiles under C# 8.0.";

        private const string RequiredMembersError =
            "error CS8652: Feature 'required members' is not available in C# 10. Please use language version 11.0 or greater.";

        private const string RequiredMembersNextAction =
            "error CS8652: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'required members' feature so the code compiles under C# 10.";

        private const string RawStringLiteralsError =
            "error CS8936: Feature 'raw string literals' is not available in C# 10.0. Please use language version 11.0 or greater.";

        private const string RawStringLiteralsNextAction =
            "error CS8936: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the 'raw string literals' feature so the code compiles under C# 10.0.";

        private const string UnrelatedError = "error CS0000: sample compile error";

        private const string ExistingNextAction =
            "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.";

        private const string ApiUpdaterNextAction =
            "Fix the obsolete API usages reported in Errors, or ask the user to accept the Script Updating Consent dialog in an interactive Unity session.";

        /// <summary>
        /// What: a language-version error produces the pinned-rewrite NextAction as an exact literal.
        /// </summary>
        [Test]
        public void Build_WhenLanguageVersionError_ReturnsPinnedRewriteAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { FileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: a prefix-less CS#### message still produces the same pinned-rewrite NextAction.
        /// </summary>
        [Test]
        public void Build_WhenPrefixlessErrorCode_ReturnsPinnedRewriteAction()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { PrefixlessFileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: unmatched error messages produce no NextActions.
        /// </summary>
        [Test]
        public void Build_WhenNoMatch_ReturnsEmpty()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(new[] { UnrelatedError });

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: a language-version sentence without a CS#### code is skipped (fail-open).
        /// </summary>
        [Test]
        public void Build_WhenMessageHasNoErrorCode_ReturnsEmpty()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { "Feature 'file-scoped namespace' is not available in C# 9.0." });

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: identical generated NextActions are appended only once.
        /// </summary>
        [Test]
        public void Build_WhenDuplicateGeneratedActions_Dedups()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[] { FileScopedNamespaceError, PrefixlessFileScopedNamespaceError });

            Assert.That(nextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: at most three generated NextActions are returned even when more messages match.
        /// </summary>
        [Test]
        public void Build_WhenMoreThanThreeMatches_ReturnsAtMostThree()
        {
            string[] nextActions = CompileErrorNextActionsBuilder.Build(
                new[]
                {
                    FileScopedNamespaceError,
                    RecordsError,
                    RequiredMembersError,
                    RawStringLiteralsError
                });

            Assert.That(
                nextActions,
                Is.EqualTo(new[]
                {
                    FileScopedNamespaceNextAction,
                    RecordsNextAction,
                    RequiredMembersNextAction
                }));
        }

        /// <summary>
        /// What: only the first ten error messages are scanned for NextAction generation.
        /// </summary>
        [Test]
        public void Build_WhenMatchIsAfterFirstTenMessages_ReturnsEmpty()
        {
            string[] errorMessages = new string[11];
            for (int index = 0; index < 10; index++)
            {
                errorMessages[index] = UnrelatedError;
            }

            errorMessages[10] = FileScopedNamespaceError;

            string[] nextActions = CompileErrorNextActionsBuilder.Build(errorMessages);

            Assert.That(nextActions, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: a successful compile leaves NextActions unchanged even when errors look matchable.
        /// </summary>
        [Test]
        public void Apply_WhenSuccess_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: true);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(FileScopedNamespaceError) });

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: a null error list leaves NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenErrorsNull_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, errors: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: unmatched errors leave existing NextActions unchanged.
        /// </summary>
        [Test]
        public void Apply_WhenNoMatch_LeavesNextActionsUnchanged()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(UnrelatedError) });

            Assert.That(response.NextActions, Is.EqualTo(new[] { ExistingNextAction }));
        }

        /// <summary>
        /// What: existing NextActions are kept and the language-version action is appended at the end.
        /// </summary>
        [Test]
        public void Apply_WhenExistingNextActions_AppendsLanguageVersionAction()
        {
            CompileResponse response = CreateResponse(success: false);
            response.NextActions = new[] { ExistingNextAction };

            CompileErrorNextActionsComposer.Apply(response, new[] { CreateError(FileScopedNamespaceError) });

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ExistingNextAction, FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: CreateResponse emits the language-version NextAction as the entire NextActions array.
        /// </summary>
        [Test]
        public void CreateResponse_WhenLanguageVersionError_ReturnsExactNextActions()
        {
            CompileResult result = CreateFailedResult(CreateError(FileScopedNamespaceError));

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                pausePointWarning: null);

            Assert.That(response.NextActions, Is.EqualTo(new[] { FileScopedNamespaceNextAction }));
        }

        /// <summary>
        /// What: CreateResponse appends the language-version NextAction after the API Updater action.
        /// </summary>
        [Test]
        public void CreateResponse_WhenLanguageVersionErrorAndConsentDeclined_AppendsAfterExistingNextActions()
        {
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: new[] { CreateError(FileScopedNamespaceError) },
                warnings: Array.Empty<CompilerMessage>(),
                apiUpdaterConsentDeclined: true);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                pausePointWarning: null);

            Assert.That(
                response.NextActions,
                Is.EqualTo(new[] { ApiUpdaterNextAction, FileScopedNamespaceNextAction }));
        }

        private static CompileResponse CreateResponse(bool success)
        {
            return new CompileResponse(
                success: success,
                errorCount: success ? 0 : 1,
                warningCount: 0,
                errors: null,
                warnings: null,
                message: null);
        }

        private static CompileResult CreateFailedResult(CompilerMessage error)
        {
            return new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { error },
                errors: new[] { error },
                warnings: Array.Empty<CompilerMessage>());
        }

        private static CompilerMessage CreateError(string message)
        {
            return new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = message,
                file = "Assets/Sample.cs",
                line = 1
            };
        }
    }
}
