using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the single compile result recording seam used by delayed CLI polling.
    /// </summary>
    [TestFixture]
    public sealed class CompileResultSessionRecorderTests
    {
        [Test]
        public void RecordCompileResult_WhenPendingRequestExists_StoresResponseAndClearsPendingRequest()
        {
            // Verifies raw compile results are shaped and persisted through the recorder seam once.
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
                    "compile_test_request",
                    forceRecompile: false,
                    markedAtUtc: DateTime.UtcNow);
                CompilerMessage warning = new CompilerMessage
                {
                    type = CompilerMessageType.Warning,
                    message = "warning",
                    file = "Assets/Test.cs",
                    line = 15
                };
                CompileResult result = new CompileResult(
                    success: true,
                    errorCount: 0,
                    warningCount: 1,
                    completedAt: DateTime.Now,
                    messages: new[] { warning },
                    errors: Array.Empty<CompilerMessage>(),
                    warnings: new[] { warning });

                CompileResponse response = CompileResultSessionRecorder.RecordCompileResult(
                    compileResultSessionRepository,
                    pendingCompileSessionRepository,
                    "compile_test_request",
                    forceRecompile: false,
                    result,
                    "compile_test_request");

                UnityCliLoopStoredCompileResult storedResult =
                    compileResultSessionRepository.GetCompileResult("compile_test_request");
                UnityCliLoopPendingCompileRequest pendingRequest =
                    UnityCliLoopEditorSessionStateTestFactory.GetSinglePendingCompileRequest(
                        pendingCompileSessionRepository);

                Assert.That(response.Success, Is.True);
                Assert.That(response.WarningCount, Is.EqualTo(1));
                Assert.That(storedResult.ResultJson, Does.Contain("\"Success\":true"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"Warnings\":["));
                Assert.That(pendingRequest.HasRequest, Is.False);
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }
    }
}
