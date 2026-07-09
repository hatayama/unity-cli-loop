using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Characterizes dynamic-code response construction before factory extraction.
    /// </summary>
    [TestFixture]
    public sealed class DynamicCodeExecutionResponseFactoryTests
    {
        /// <summary>
        /// Verifies successful execution results preserve result, logs, and timings.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenSuccessful_MapsResultLogsAndTimings()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = true,
                Result = 42,
                Logs = new List<string> { "execution log" },
                Timings = new List<string> { "compile_ms=1" }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Result, Is.EqualTo("42"));
            Assert.That(response.Logs, Is.EqualTo(new[] { "execution log" }));
            Assert.That(response.CompilationErrors, Is.Empty);
            Assert.That(response.ErrorMessage, Is.Empty);
            Assert.That(response.Timings, Is.EqualTo(new[] { "compile_ms=1" }));
        }

        /// <summary>
        /// Verifies compilation failures expose deduplicated diagnostics, summary, hints, suggestions, and source context.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenCompilationFails_MapsStructuredDiagnostics()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Compilation error occurred",
                UpdatedCode = "return MissingType.Value;",
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError
                    {
                        ErrorCode = "CS0246",
                        Message = "The type or namespace name 'MissingType' could not be found",
                        Line = 1,
                        Column = 8
                    }
                },
                AmbiguousTypeCandidates = new Dictionary<string, List<string>>
                {
                    { "MissingType", new List<string> { "Namespace.One", "Namespace.Two" } }
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.DiagnosticsSummary,
                Is.EqualTo(
                    "Errors: 1 unique (1 total). First at L1: CS0246 The type or namespace name 'MissingType' could not be found"));
            Assert.That(response.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(response.CompilationErrors, Is.SameAs(response.Diagnostics));
            Assert.That(
                response.Diagnostics[0].Hint,
                Is.EqualTo(
                    "Auto-using resolution found multiple candidates for 'MissingType': Namespace.One, Namespace.Two. Use a fully-qualified name or add the correct using directive."));
            Assert.That(
                response.Diagnostics[0].Suggestions,
                Is.EqualTo(new[] { "Use Namespace.One.MissingType", "Use Namespace.Two.MissingType" }));
            Assert.That(response.Diagnostics[0].Context, Does.Contain("L1:return MissingType.Value;"));
            Assert.That(response.Diagnostics[0].Context, Does.Contain("^"));
            Assert.That(response.Logs, Contains.Item(response.DiagnosticsSummary));
        }

        /// <summary>
        /// Verifies known compilation failures preserve friendly explanations, examples, and solutions.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenKnownFailureOccurs_AddsFriendlyGuidance()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Compilation error occurred",
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError
                    {
                        ErrorCode = "CS8803",
                        Message = "Top-level statements must precede namespace and type declarations."
                    }
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.ErrorMessage, Is.EqualTo("There is an issue with the code structure"));
            Assert.That(
                response.Logs,
                Contains.Item(
                    "Explanation: In the Dynamic Tool, class and namespace declarations are not required. Write Unity API processing directly."));
            Assert.That(response.Logs, Contains.Item("Solutions:"));
            Assert.That(response.Logs, Contains.Item("- Remove class definition and write only the code inside the method"));
        }

        /// <summary>
        /// Verifies exceptions and auto-injected namespaces append their wire-visible log details.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WithExceptionAndInjectedNamespaces_AppendsLogDetails()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = true,
                Result = "ok",
                Logs = new List<string> { "before" },
                Exception = new InvalidOperationException("test exception"),
                AutoInjectedNamespaces = new List<string> { "System.Linq", "UnityEngine" }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Logs, Contains.Item("Exception: test exception"));
            Assert.That(
                response.Logs,
                Contains.Item(
                    "Performance hint: Auto-resolved 2 missing using directive(s): using System.Linq; using UnityEngine; — Include them in your code to skip auto-resolution and improve compilation speed."));
        }

        /// <summary>
        /// Verifies cancelled results are recognized and mapped to the neutral cancellation response.
        /// </summary>
        [Test]
        public void CancelledResult_WhenMapped_ReturnsNeutralCancellationResponse()
        {
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED
            };

            bool isCancelled = DynamicCodeExecutionResponseFactory.IsCancelledResult(result);
            ExecuteDynamicCodeResponse response = DynamicCodeExecutionResponseFactory.CreateCancelledResponse();

            Assert.That(isCancelled, Is.True);
            Assert.That(response.Success, Is.False);
            Assert.That(response.Result, Is.Empty);
            Assert.That(response.Logs, Is.EqualTo(new[] { "Execution cancelled" }));
            Assert.That(response.CompilationErrors, Is.Empty);
            Assert.That(response.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
        }
    }
}
