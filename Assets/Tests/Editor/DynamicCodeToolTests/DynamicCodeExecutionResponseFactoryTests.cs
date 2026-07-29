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
        /// Verifies wrapped UpdatedCode diagnostics render context from the user snippet region.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenWrappedSourceFails_UsesUserSnippetContext()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            string userCode = "int a=1;\nint b=2;\nint c=3;\nint d=4;\nint e= ;\nreturn a;";
            string wrappedSource = WrapperTemplate.Build(
                Array.Empty<string>(),
                Array.Empty<string>(),
                "TestNs",
                "TestClass",
                userCode);
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Compilation error occurred",
                UpdatedCode = wrappedSource,
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError
                    {
                        ErrorCode = "CS1525",
                        Message = "Invalid expression term ';'",
                        Line = 5,
                        Column = 20
                    }
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result, userCode);

            Assert.That(response.Diagnostics[0].Column, Is.EqualTo(8));
            Assert.That(response.Diagnostics[0].PointerColumn, Is.EqualTo(8));
            Assert.That(response.Diagnostics[0].Context, Does.Contain("L5:int e= ;"));
            Assert.That(response.Diagnostics[0].Context, Does.Contain("int b=2;"));
            Assert.That(response.Diagnostics[0].Context, Does.Not.Contain("__uloop_literal"));
            Assert.That(response.Diagnostics[0].Context, Does.Not.Contain("using System.Collections.Generic"));
            Assert.That(response.Diagnostics[0].Context, Does.Not.Contain("L7:"));
            string[] contextLines = response.Diagnostics[0].Context
                .Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            int caretLineIndex = System.Array.FindIndex(contextLines, line => line.Contains('^'));
            Assert.That(caretLineIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(contextLines[caretLineIndex].IndexOf('^'), Is.EqualTo("L5:".Length + 7));
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
        /// Verifies a stack frame naming user-snippet.cs prepends the user-snippet line log.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenExceptionStackHasUserSnippet_PrependsLineLog()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            Exception exception = ExceptionWithStackTrace(
                new NullReferenceException("Object reference not set to an instance of an object."),
                "  at DynamicCode.GeneratedClass.Execute () [0x00000] in user-snippet.cs:line 3\n"
                + "  at io.github.hatayama.UnityCliLoop.FirstPartyTools.CommandRunner.Run ()");
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Runtime exception",
                Exception = exception,
                Logs = new List<string> { "prior" }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Logs[0], Is.EqualTo(
                "Exception at user snippet line 3: Object reference not set to an instance of an object."));
            Assert.That(response.Logs, Contains.Item("Exception: Object reference not set to an instance of an object."));
        }

        /// <summary>
        /// Verifies CommandRunner-style Logs (no Exception field) still get a snippet-line header.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenLogsContainUnitySnippetFrame_PrependsLineLog()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Object reference not set to an instance of an object",
                Logs = new List<string>
                {
                    "Execution exception: Object reference not set to an instance of an object",
                    "Stack trace:   at UnityCliLoop.Dynamic.DynamicCommand.ExecuteAsync () "
                    + "[0x00017] in /tmp/UnityCliLoopCompilation/user-snippet.cs:3 "
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Logs[0], Is.EqualTo(
                "Exception at user snippet line 3: Object reference not set to an instance of an object"));
        }

        /// <summary>
        /// Verifies stacks without user-snippet.cs leave Logs without a snippet-line header.
        /// </summary>
        [Test]
        public void TryExtractUserSnippetLineNumber_WhenStackHasNoUserSnippet_ReturnsFalse()
        {
            bool extracted = DynamicCodeExecutionResponseFactory.TryExtractUserSnippetLineNumber(
                "  at System.String.ToString ()\n  at Some.Other.Type.Method ()",
                out int lineNumber);

            Assert.That(extracted, Is.False);
            Assert.That(lineNumber, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the first user-snippet.cs:line N frame is extracted from a stack string.
        /// </summary>
        [Test]
        public void TryExtractUserSnippetLineNumber_WhenStackContainsUserSnippet_ReturnsLine()
        {
            bool extracted = DynamicCodeExecutionResponseFactory.TryExtractUserSnippetLineNumber(
                "  at Foo.Bar () in /tmp/wrapper.cs:line 40\n"
                + "  at DynamicCode.GeneratedClass.Execute () in user-snippet.cs:line 3\n"
                + "  at DynamicCode.GeneratedClass.Execute () in user-snippet.cs:line 7",
                out int lineNumber);

            Assert.That(extracted, Is.True);
            Assert.That(lineNumber, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies Unity/Mono frames that omit the "line" keyword still extract the snippet line.
        /// </summary>
        [Test]
        public void TryExtractUserSnippetLineNumber_WhenUnityFormatOmitsLineKeyword_ReturnsLine()
        {
            bool extracted = DynamicCodeExecutionResponseFactory.TryExtractUserSnippetLineNumber(
                "  at UnityCliLoop.Dynamic.DynamicCommand.ExecuteAsync () "
                + "[0x00017] in /tmp/UnityCliLoopCompilation/user-snippet.cs:3 ",
                out int lineNumber);

            Assert.That(extracted, Is.True);
            Assert.That(lineNumber, Is.EqualTo(3));
        }

        private static Exception ExceptionWithStackTrace(Exception exception, string stackTrace)
        {
            return new StackTraceOverrideException(exception.Message, stackTrace);
        }

        private sealed class StackTraceOverrideException : Exception
        {
            private readonly string _stackTrace;

            public StackTraceOverrideException(string message, string stackTrace)
                : base(message)
            {
                _stackTrace = stackTrace;
            }

            public override string StackTrace => _stackTrace;
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

        /// <summary>
        /// Verifies int-to-byte conversion failures align diagnostics with extracted using lines.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenColor32ByteConversionFails_AddsTranspilerConstraintHint()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            string userCode = "using UnityEngine;\nreturn new Color32(255, 0, 0, 255);";
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.Prepare(
                userCode,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "Compilation error occurred",
                UpdatedCode = prepared.PreparedSource,
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError
                    {
                        ErrorCode = "CS1503",
                        Message = "CS1503: Argument 1: cannot convert from 'int' to 'byte'",
                        Line = 2,
                        Column = 20
                    }
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result, userCode);

            Assert.That(response.Diagnostics[0].Line, Is.EqualTo(2));
            Assert.That(response.Diagnostics[0].Hint, Does.Contain("Color32"));
            Assert.That(response.Diagnostics[0].Context, Does.Contain("L2:return new Color32(255, 0, 0, 255);"));
            Assert.That(response.Diagnostics[0].Context, Does.Not.Contain("L1:using UnityEngine;\n                      ^"));
            Assert.That(response.Diagnostics[0].Suggestions, Contains.Item(
                "Cast each component explicitly, for example: new Color32((byte)255, (byte)0, (byte)0, (byte)255)."));
        }
    }
}
