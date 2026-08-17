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
    /// Tests compile use case orchestration around delayed result persistence.
    /// </summary>
    [TestFixture]
    public sealed class CompileUseCaseTests
    {
        [Test]
        public async Task CompileAsync_WhenExecutionLayerStoresDelayedSuccess_DoesNotStoreResultAgain()
        {
            // Verifies the UseCase success path preserves the execution layer's single delayed-result write.
            UnityCliLoopCompileResultSessionRepository innerCompileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            CountingCompileResultSessionRepository compileResultSessionRepository =
                new(innerCompileResultSessionRepository);
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
                CompileSchema request = new()
                {
                    WaitForDomainReload = true,
                    RequestId = "compile_test_request",
                    ForceRecompile = false,
                    ReloadExternalSceneChanges = true
                };
                CompileResult executionResult = CreateSuccessfulCompileResult();
                CompileUseCase useCase = new(
                    compileSessionLifecycleService,
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
                useCase.SetCompilationExecutionForTesting((compileRequest, pausePointWarning, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    CompileResultSessionRecorder.RecordCompileResult(
                        compileResultSessionRepository,
                        pendingCompileSessionRepository,
                        compileRequest.RequestId,
                        compileRequest.ForceRecompile,
                        executionResult,
                        compileRequest.RequestId);
                    return Task.FromResult(executionResult);
                });

                CompileResponse response = await useCase.CompileAsync(request, CancellationToken.None);

                Assert.That(compileResultSessionRepository.StoreCount, Is.EqualTo(1));
                Assert.That(response.Success, Is.True);
                Assert.That(response.ProjectRoot, Is.Not.Empty);
                // Verifies no pause-point Warning appears outside Play Mode (the only state an EditMode test can exercise).
                Assert.That(response.Warning, Is.Null);
                UnityCliLoopStoredCompileResult storedResult =
                    compileResultSessionRepository.GetCompileResult("compile_test_request");
                Assert.That(storedResult.ResultJson, Does.Contain("\"ProjectRoot\":"));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }

        /// <summary>
        /// Verifies a compilation-state validation failure copies ErrorCode onto CompileResponse.
        /// </summary>
        [Test]
        public async Task CompileAsync_WhenValidationFailsBecauseCompiling_SetsAlreadyInProgressErrorCode()
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
                CompileUseCase useCase = new(
                    compileSessionLifecycleService,
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
                useCase.SetCompilationStateValidationForTesting(() =>
                    ValidationResult.FailureWithErrorCode(
                        "Compilation is already in progress. Please wait for the current compilation to finish.",
                        CompileStateValidationErrorCodes.AlreadyInProgressErrorCodeText));
                useCase.SetCompilationExecutionForTesting((compileRequest, pausePointWarning, ct) =>
                {
                    throw new InvalidOperationException("validation failure must not start compilation");
                });

                CompileResponse response = await useCase.CompileAsync(
                    new CompileSchema
                    {
                        WaitForDomainReload = false,
                        RequestId = "compile_validation_error_code",
                        ForceRecompile = false,
                        ReloadExternalSceneChanges = true
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.False);
                Assert.That(
                    response.ErrorCode,
                    Is.EqualTo(CompileStateValidationErrorCodes.AlreadyInProgressErrorCodeText));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }

        private static CompileResult CreateSuccessfulCompileResult()
        {
            CompilerMessage warning = new()
            {
                type = CompilerMessageType.Warning,
                message = "warning",
                file = "Assets/Test.cs",
                line = 15
            };
            return new CompileResult(
                success: true,
                errorCount: 0,
                warningCount: 1,
                completedAt: DateTime.Now,
                messages: new[] { warning },
                errors: Array.Empty<CompilerMessage>(),
                warnings: new[] { warning });
        }

        private sealed class CountingCompileResultSessionRepository : ICompileResultSessionRepository
        {
            private readonly ICompileResultSessionRepository _inner;

            internal CountingCompileResultSessionRepository(ICompileResultSessionRepository inner)
            {
                Assert.That(inner, Is.Not.Null);
                _inner = inner;
            }

            internal int StoreCount { get; private set; }

            public void StoreCompileResult(
                string requestId,
                bool forceRecompile,
                string resultJson,
                DateTime completedAtUtc)
            {
                StoreCount++;
                _inner.StoreCompileResult(requestId, forceRecompile, resultJson, completedAtUtc);
            }

            public UnityCliLoopStoredCompileResult GetCompileResult(string requestId)
            {
                return _inner.GetCompileResult(requestId);
            }

            public UnityCliLoopStoredCompileResult GetStoredCompileResult()
            {
                return _inner.GetStoredCompileResult();
            }

            public UnityCliLoopStoredCompileResult[] GetStoredCompileResults()
            {
                return _inner.GetStoredCompileResults();
            }

            public void ClearCompileResult()
            {
                _inner.ClearCompileResult();
            }

            public bool ClearExpiredCompileResult(DateTime utcNow, TimeSpan lifetime)
            {
                return _inner.ClearExpiredCompileResult(utcNow, lifetime);
            }
        }
    }
}
