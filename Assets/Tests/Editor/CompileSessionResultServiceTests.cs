using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests compile result conversion before results are stored for CLI polling.
    /// </summary>
    [TestFixture]
    public sealed class CompileSessionResultServiceTests
    {
        [Test]
        public void CreateCompileResult_WhenNormalCompileCompletes_MapsDetailedIssues()
        {
            // Verifies normal compile results keep detailed error and warning entries.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "error",
                file = "Assets/Test.cs",
                line = 12
            };
            CompilerMessage warning = new CompilerMessage
            {
                type = CompilerMessageType.Warning,
                message = "warning",
                file = "Assets/Test.cs",
                line = 15
            };
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 1,
                completedAt: DateTime.Now,
                messages: new[] { error, warning },
                errors: new[] { error },
                warnings: new[] { warning });

            CompileResponse response =
                CompileSessionResultService.CreateCompileResult(result, forceRecompile: false);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCount, Is.EqualTo(1));
            Assert.That(response.WarningCount, Is.EqualTo(1));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(response.Warnings, Has.Length.EqualTo(1));
            Assert.That(response.Errors[0].Message, Is.EqualTo("error"));
        }

        [Test]
        public void CreateCompileResult_WhenForceCompileIsUnknown_ExplainsNullDetails()
        {
            // Verifies force compile results do not pretend Unity returned detailed issue content.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "error",
                file = "Assets/Test.cs",
                line = 12
            };
            CompileResult result = new CompileResult(
                success: null,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { error },
                errors: new[] { error },
                warnings: Array.Empty<CompilerMessage>(),
                isIndeterminate: true,
                message: null);

            CompileResponse response =
                CompileSessionResultService.CreateCompileResult(result, forceRecompile: true);

            Assert.That(response.Success, Is.Null);
            Assert.That(response.ErrorCount, Is.Null);
            Assert.That(response.WarningCount, Is.Null);
            Assert.That(response.Errors, Is.Null);
            Assert.That(response.Warnings, Is.Null);
            Assert.That(response.Message, Is.EqualTo(ForceCompileUnknownResult.MessageText));
        }

        [Test]
        public void CreateCompileResult_WhenForceCompileHasOutcome_ExplainsNullDetails()
        {
            // Verifies force compile does not report counts or issue lists even when a high-level outcome exists.
            CompileResult result = new CompileResult(
                success: true,
                errorCount: 0,
                warningCount: 2,
                completedAt: DateTime.Now,
                messages: Array.Empty<CompilerMessage>(),
                errors: Array.Empty<CompilerMessage>(),
                warnings: Array.Empty<CompilerMessage>(),
                message: "Internal force compile status message.");

            CompileResponse response =
                CompileSessionResultService.CreateCompileResult(result, forceRecompile: true);

            Assert.That(response.Success, Is.True);
            Assert.That(response.ErrorCount, Is.Null);
            Assert.That(response.WarningCount, Is.Null);
            Assert.That(response.Errors, Is.Null);
            Assert.That(response.Warnings, Is.Null);
            Assert.That(response.Message, Is.EqualTo(ForceCompileUnknownResult.MessageText));
        }

        [Test]
        public void CreateCompileResult_WhenForceCompileHasPreservedFailure_MapsDetailedIssues()
        {
            // Verifies preflight failures keep actionable details even during force compile.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "external Scene changed",
                file = "Assets/Scenes/SampleScene.unity",
                line = 0
            };
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { error },
                errors: new[] { error },
                warnings: Array.Empty<CompilerMessage>(),
                message: "Compilation stopped because open Scene files changed externally.",
                preserveDetailsWhenForceRecompile: true);

            CompileResponse response =
                CompileSessionResultService.CreateCompileResult(result, forceRecompile: true);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCount, Is.EqualTo(1));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(response.Errors[0].File, Is.EqualTo("Assets/Scenes/SampleScene.unity"));
            Assert.That(response.Message, Does.Contain("externally"));
        }

        [Test]
        public void CreateCompileResult_WhenUnityTestFrameworkSymbolIsMissing_AddsTestAsmdefHint()
        {
            // Verifies compile failures from unmarked test asmdefs include the TestAssemblies fix.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "The type or namespace name 'UnityTestAttribute' could not be found",
                file = "Assets/Tests/PlayMode/SampleTest.cs",
                line = 8
            };
            CompileResult result = new CompileResult(
                success: false,
                errorCount: 1,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new[] { error },
                errors: new[] { error },
                warnings: Array.Empty<CompilerMessage>());

            CompileResponse response =
                CompileSessionResultService.CreateCompileResult(result, forceRecompile: false);

            Assert.That(response.Message, Does.Contain("TestAssemblies"));
            Assert.That(response.Message, Does.Contain("com.unity.test-framework"));
        }

        [Test]
        public void StoreCompileResult_WhenResultIsPersisted_UsesPascalCaseJson()
        {
            // Verifies delayed compile polling reads the same PascalCase response contract as immediate tool responses.
            UnityCliLoopEditorSessionStateService sessionStateService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();
            UnityCliLoopEditorSessionStateSnapshot originalSnapshot =
                UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(sessionStateService);
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

                CompileSessionResultService.StoreCompileResult(
                    sessionStateService,
                    "compile_test_request",
                    forceRecompile: false,
                    response,
                    "compile_test_request");

                UnityCliLoopStoredCompileResult storedResult =
                    sessionStateService.GetCompileResult("compile_test_request");

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
                originalSnapshot.Restore(sessionStateService);
            }
        }
    }
}
