using System;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests compile result conversion before results are stored for CLI polling.
    /// </summary>
    [TestFixture]
    public sealed class CompileResponseFactoryTests
    {
        [Test]
        public void CreateResponse_WhenNormalCompileCompletes_MapsDetailedIssues()
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
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCount, Is.EqualTo(1));
            Assert.That(response.WarningCount, Is.EqualTo(1));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(response.Warnings, Has.Length.EqualTo(1));
            Assert.That(response.Errors[0].Message, Is.EqualTo("error"));
        }

        [Test]
        public void CreateResponse_WhenForceCompileIsUnknown_ExplainsNullDetails()
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
                CompileResponseFactory.CreateResponse(result, forceRecompile: true);

            Assert.That(response.Success, Is.Null);
            Assert.That(response.ErrorCount, Is.Null);
            Assert.That(response.WarningCount, Is.Null);
            Assert.That(response.Errors, Is.Null);
            Assert.That(response.Warnings, Is.Null);
            Assert.That(response.Message, Is.EqualTo(ForceCompileUnknownResult.MessageText));
        }

        [Test]
        public void CreateResponse_WhenForceCompileHasOutcome_ExplainsNullDetails()
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
                CompileResponseFactory.CreateResponse(result, forceRecompile: true);

            Assert.That(response.Success, Is.True);
            Assert.That(response.ErrorCount, Is.Null);
            Assert.That(response.WarningCount, Is.Null);
            Assert.That(response.Errors, Is.Null);
            Assert.That(response.Warnings, Is.Null);
            Assert.That(response.Message, Is.EqualTo(ForceCompileUnknownResult.MessageText));
        }

        [Test]
        public void CreateResponse_WhenIndeterminateNonForceCompileHasCounts_PreservesCountsWithoutIssues()
        {
            // Verifies indeterminate non-force results keep observed counts while withholding unreliable issue lists.
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
                success: null,
                errorCount: 1,
                warningCount: 1,
                completedAt: DateTime.Now,
                messages: new[] { error, warning },
                errors: new[] { error },
                warnings: new[] { warning },
                isIndeterminate: true,
                message: null);

            CompileResponse response =
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.Success, Is.Null);
            Assert.That(response.ErrorCount, Is.EqualTo(1));
            Assert.That(response.WarningCount, Is.EqualTo(1));
            Assert.That(response.Errors, Is.Null);
            Assert.That(response.Warnings, Is.Null);
            Assert.That(
                response.Message,
                Is.EqualTo("Compilation status is unknown. Use get-logs to inspect the compiler output."));
        }

        [Test]
        public void CreateResponse_WhenForceCompileHasPreservedFailure_MapsDetailedIssues()
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
                CompileResponseFactory.CreateResponse(result, forceRecompile: true);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCount, Is.EqualTo(1));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(response.Errors[0].File, Is.EqualTo("Assets/Scenes/SampleScene.unity"));
            Assert.That(response.Message, Does.Contain("externally"));
        }

        [Test]
        public void CreateResponse_WhenExternalSceneCannotBeResolved_AddsNextActions()
        {
            // Verifies unresolved external Scene changes include execute-dynamic-code reload guidance.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "Compilation cannot resolve externally changed Scene files before compile.",
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
                message: error.message);

            CompileResponse response =
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.NextActions, Is.Not.Null);
            Assert.That(response.NextActions, Has.Length.EqualTo(2));
            Assert.That(response.NextActions[0], Does.Contain("execute-dynamic-code"));
            Assert.That(response.NextActions[0], Does.Contain("EditorSceneManager.OpenScene"));
            Assert.That(response.NextActions[0], Does.Contain("OpenSceneMode.Single discards unsaved"));
            Assert.That(response.NextActions[1], Does.Contain("unsaved in-editor changes"));
            Assert.That(response.NextActions[1], Does.Not.Contain("stop-on-external-scene-changes"));
        }

        [Test]
        public void CreateResponse_WhenExternalSceneCannotBeReloaded_AddsNextActions()
        {
            // Verifies reload-failure messages from ExternalSceneChangeResolver also surface reload guidance.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message =
                    "Compilation cannot reload externally changed Scene files before compile. " +
                    "Scenes that could not be reloaded: Assets/Scenes/SampleScene.unity.",
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
                message: error.message);

            CompileResponse response =
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.NextActions, Is.Not.Null);
            Assert.That(response.NextActions, Has.Length.EqualTo(2));
        }

        [Test]
        public void CreateResponse_WhenExternalSceneStopMessageUsesDifferentWording_DoesNotAddNextActions()
        {
            // Verifies the stopped variant ("changed externally") does not match the NextActions substring gate.
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message =
                    "Compilation stopped because open Scene files changed externally. " +
                    "External Scene changes: Assets/Scenes/SampleScene.unity.",
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
                message: error.message);

            CompileResponse response =
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.NextActions, Is.Null);
        }

        [Test]
        public void CreateResponse_WhenUnityTestFrameworkSymbolIsMissing_AddsTestAsmdefHint()
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
                CompileResponseFactory.CreateResponse(result, forceRecompile: false);

            Assert.That(response.Message, Does.Contain("TestAssemblies"));
            Assert.That(response.Message, Does.Contain("com.unity.test-framework"));
        }
    }
}
