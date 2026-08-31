using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that a Script Updating Consent decline reaches both delayed and immediate compile responses.
    /// </summary>
    [TestFixture]
    public sealed class CompileApiUpdaterConsentPropagationTests
    {
        private const string WarningText =
            "Unity's API Updater requested consent to rewrite source files ('Script Updating Consent' dialog). uloop declines this automatically: source files are not rewritten without explicit user consent. The obsolete-API compile errors it would have fixed are reported in Errors.";

        private const string NextActionText =
            "Fix the obsolete API usages reported in Errors, or ask the user to accept the Script Updating Consent dialog in an interactive Unity session.";

        /// <summary>
        /// What: the delayed SessionState path stores the decline Warning and NextActions.
        /// </summary>
        [Test]
        public void RecordCompileResult_WhenConsentWasDeclined_PersistsDisclosure()
        {
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            UnityCliLoopEditorSessionStateSnapshot originalSnapshot =
                UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();

            try
            {
                compileSessionLifecycleService.MarkPendingCompileRequest(
                    "compile_consent_delayed",
                    forceRecompile: false,
                    markedAtUtc: DateTime.UtcNow);
                CompileResult result = CreateDeclinedResult();

                CompileResponse response = CompileResultSessionRecorder.RecordCompileResult(
                    compileResultSessionRepository,
                    pendingCompileSessionRepository,
                    "compile_consent_delayed",
                    forceRecompile: false,
                    result,
                    "compile_consent_delayed");
                UnityCliLoopStoredCompileResult storedResult =
                    compileResultSessionRepository.GetCompileResult("compile_consent_delayed");

                Assert.That(response.Warning, Is.EqualTo(WarningText));
                Assert.That(response.NextActions, Is.EqualTo(new[] { NextActionText }));
                Assert.That(storedResult.ResultJson, Does.Contain(WarningText));
                Assert.That(storedResult.ResultJson, Does.Contain(NextActionText));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }

        /// <summary>
        /// What: the immediate UseCase path shapes the same decline Warning and NextActions.
        /// </summary>
        [Test]
        public async Task CompileAsync_WhenConsentWasDeclined_ReturnsDisclosure()
        {
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService =
                new(
                    UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository(),
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
            UnityCliLoopEditorSessionStateSnapshot originalSnapshot =
                UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();

            try
            {
                CompileResult executionResult = CreateDeclinedResult();
                CompileUseCase useCase = new(
                    compileSessionLifecycleService,
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
                useCase.SetCompilationStateValidationForTesting(() => ValidationResult.Success());
                useCase.SetCompilationExecutionForTesting((compileRequest, playModeStopWarning, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(executionResult);
                });

                CompileResponse response = await useCase.CompileAsync(
                    new CompileSchema
                    {
                        WaitForDomainReload = false,
                        RequestId = "compile_consent_immediate",
                        ForceRecompile = false,
                        ReloadExternalSceneChanges = true
                    },
                    CancellationToken.None);

                Assert.That(response.Warning, Is.EqualTo(WarningText));
                Assert.That(response.NextActions, Is.EqualTo(new[] { NextActionText }));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }

        /// <summary>
        /// What: CreateResponse appends the decline disclosure onto an existing pause-point Warning.
        /// </summary>
        [Test]
        public void CreateResponse_WhenDeclinedWithExistingWarning_AppendsFixedWarning()
        {
            CompileResult result = CreateDeclinedResult();

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: false,
                playModeStopWarning: "Play Mode was active with 2 enabled pause point(s).");

            Assert.That(
                response.Warning,
                Is.EqualTo(
                    "Play Mode was active with 2 enabled pause point(s).\n" + WarningText));
            Assert.That(response.NextActions, Is.EqualTo(new[] { NextActionText }));
        }

        /// <summary>
        /// What: CreateResponse appends the decline NextAction after force-compile NextActions.
        /// </summary>
        [Test]
        public void CreateResponse_WhenDeclinedForceCompile_AppendsFixedNextAction()
        {
            CompileResult result = new CompileResult(
                success: null,
                errorCount: 0,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: true,
                apiUpdaterConsentDeclined: true);

            CompileResponse response = CompileResponseFactory.CreateResponse(
                result,
                forceRecompile: true,
                playModeStopWarning: null);

            Assert.That(response.Warning, Is.EqualTo(WarningText));
            Assert.That(
                response.NextActions,
                Is.EqualTo(new[]
                {
                    "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.",
                    NextActionText
                }));
        }

        private static CompileResult CreateDeclinedResult()
        {
            return new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>(),
                apiUpdaterConsentDeclined: true);
        }
    }
}
