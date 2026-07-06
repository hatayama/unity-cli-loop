using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests compile response persistence for delayed CLI polling.
    /// </summary>
    [TestFixture]
    public sealed class CompileSessionResultStoreTests
    {
        [Test]
        public void StoreCompileResult_WhenResultIsPersisted_UsesPascalCaseJson()
        {
            // Verifies delayed compile polling reads the same PascalCase response contract as immediate tool responses.
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            UnityCliLoopEditorSessionStateSnapshot originalSnapshot =
                UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();

            try
            {
                CompileResponse response = new CompileResponse
                {
                    Success = true,
                    ErrorCount = 0,
                    WarningCount = 0,
                    Errors = Array.Empty<CompileIssue>(),
                    Warnings = Array.Empty<CompileIssue>(),
                    Message = "Compilation completed."
                };

                CompileSessionResultStore.StoreCompileResult(
                    compileResultSessionRepository,
                    pendingCompileSessionRepository,
                    "compile_test_request",
                    forceRecompile: false,
                    response,
                    "compile_test_request");

                UnityCliLoopStoredCompileResult storedResult =
                    compileResultSessionRepository.GetCompileResult("compile_test_request");

                // Pins every property name of the stored payload because the CLI parses this JSON
                // and CompileResponse no longer has a dedicated storage DTO guarding the shape.
                Assert.That(storedResult.ResultJson, Does.Contain("\"Success\":true"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"ErrorCount\":0"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"WarningCount\":0"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"Errors\":[]"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"Warnings\":[]"));
                Assert.That(storedResult.ResultJson, Does.Contain("\"Message\":\"Compilation completed.\""));
                Assert.That(storedResult.ResultJson, Does.Contain("\"ProjectRoot\":"));
                Assert.That(storedResult.ResultJson, Does.Not.Contain("\"success\""));
            }
            finally
            {
                originalSnapshot.Restore();
            }
        }
    }
}
