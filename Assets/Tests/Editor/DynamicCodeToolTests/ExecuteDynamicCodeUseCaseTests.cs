using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class ExecuteDynamicCodeUseCaseTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeForegroundWarmupState.Reset();
        }

        [Test]
        public async Task ExecuteAsync_WhenInitialCompilationLooksLikeMissingReturn_ShouldRetryOnce()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0161",
                            Message = "Not all code paths return a value"
                        }
                    }
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "int x = 1",
                        CompileOnly = false
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(2));
                Assert.That(runtime.Requests[1].Code, Does.Contain("return null;"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenYieldingRequestNeedsMissingReturnRetry_ShouldPreserveYieldingOnRetry()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0161",
                            Message = "Not all code paths return a value"
                        }
                    }
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "int x = 1",
                        YieldToForegroundRequests = true
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.True);
                Assert.That(runtime.TryExecuteRequests, Has.Count.EqualTo(2));
                Assert.That(runtime.TryExecuteRequests[0].YieldToForegroundRequests, Is.True);
                Assert.That(runtime.TryExecuteRequests[1].YieldToForegroundRequests, Is.True);
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenInitialExecutionSucceeds_ShouldNotRetry()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;",
                        CompileOnly = false
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(1));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenFirstForegroundExecutionRuns_ShouldWarmHiddenPathBeforeUserCode()
        {
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = true,
                    Result = "warm"
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;"
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(2));
                Assert.That(runtime.Requests[0].Code, Does.Contain("Unity CLI Loop dynamic code prewarm"));
                Assert.That(runtime.Requests[1].Code, Is.EqualTo("return 1;"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenForegroundWarmupAlreadyCompleted_ShouldNotRepeatIt()
        {
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = true,
                    Result = "warm"
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "first"
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "second"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse firstResponse = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;"
                    },
                    CancellationToken.None);
                ExecuteDynamicCodeResponse secondResponse = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 2;"
                    },
                    CancellationToken.None);

                Assert.That(firstResponse.Success, Is.True);
                Assert.That(secondResponse.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(3));
                Assert.That(runtime.Requests[2].Code, Is.EqualTo("return 2;"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenWarmupFailsButForegroundExecutionSucceeds_ShouldNotRepeatWarmupOnNextRequest()
        {
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "first"
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "second"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse firstResponse = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;"
                    },
                    CancellationToken.None);
                ExecuteDynamicCodeResponse secondResponse = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 2;"
                    },
                    CancellationToken.None);

                Assert.That(firstResponse.Success, Is.True);
                Assert.That(secondResponse.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(3));
                Assert.That(runtime.Requests[0].Code, Does.Contain("Unity CLI Loop dynamic code prewarm"));
                Assert.That(runtime.Requests[1].Code, Is.EqualTo("return 1;"));
                Assert.That(runtime.Requests[2].Code, Is.EqualTo("return 2;"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenRequestIsCompileOnly_ShouldSkipForegroundWarmup()
        {
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;",
                        CompileOnly = true
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.True);
                Assert.That(runtime.Requests, Has.Count.EqualTo(1));
                Assert.That(runtime.Requests[0].Code, Is.EqualTo("return 1;"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenRetryAfterMissingReturnStillFails_ShouldReturnRetryDiagnostics()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Compilation error occurred",
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0161",
                            Message = "Not all code paths return a value"
                        }
                    },
                    Logs = new List<string> { "initial failure" },
                    Timings = new List<string> { "initial timing" }
                },
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Compilation error occurred",
                    UpdatedCode = "int x = 1;\nreturn null;",
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0029",
                            Message = "Cannot implicitly convert type 'string' to 'int'",
                            Line = 2,
                            Column = 8
                        }
                    },
                    Logs = new List<string> { "retry failure" },
                    Timings = new List<string> { "retry timing" }
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "int x = 1",
                        CompileOnly = false
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.False);
                Assert.That(runtime.Requests, Has.Count.EqualTo(2));
                Assert.That(response.Timings, Contains.Item("retry timing"));
                Assert.That(response.Diagnostics, Has.Count.EqualTo(1));
                Assert.That(response.Diagnostics[0].ErrorCode, Is.EqualTo("CS0029"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenDiagnosticLineUsesTwoDigits_ShouldAlignCaretWithRenderedPrefix()
        {
            MarkForegroundWarmupCompleted();
            string updatedCode = string.Join(
                "\n",
                Enumerable.Range(1, 12).Select(index => index == 10 ? "abcd" : $"line{index}"));
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Compilation error occurred",
                    UpdatedCode = updatedCode,
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0103",
                            Message = "CS0103: The name 'x' does not exist in the current context",
                            Line = 10,
                            Column = 2
                        }
                    }
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return x;"
                    },
                    CancellationToken.None);

                Assert.That(response.Diagnostics, Has.Count.EqualTo(1));

                string[] contextLines = response.Diagnostics[0].Context
                    .Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                int targetLineIndex = System.Array.FindIndex(contextLines, line => line.StartsWith("L10:"));

                Assert.That(targetLineIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(contextLines[targetLineIndex + 1].IndexOf('^'), Is.EqualTo("L10:".Length + 1));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeThrowsOperationCanceledException_ShouldReturnNeutralCancelledResponse()
        {
            MarkForegroundWarmupCompleted();
            CancellingDynamicCodeExecutionRuntime runtime = new CancellingDynamicCodeExecutionRuntime();
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;"
                    },
                    cancellationTokenSource.Token);

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorMessage, Is.EqualTo(McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
                Assert.That(response.Logs, Contains.Item("Execution cancelled"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeReturnsCancelledResult_ShouldPreserveNeutralCancelledResponse()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                    Logs = new List<string> { "Execution cancelled" },
                    Timings = new List<string> { "compile_ms=1" }
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "return 1;"
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorMessage, Is.EqualTo(McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
                Assert.That(response.Logs, Contains.Item("Execution cancelled"));
                Assert.That(response.Timings, Contains.Item("compile_ms=1"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeFailsAfterProducingLogs_ShouldPreserveOriginalLogs()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new FakeDynamicCodeExecutionRuntime(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Object reference not set to an instance of an object",
                    Logs = new List<string> { "partial log" }
                });
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(runtime);

            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.Restricted);

            try
            {
                ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                    new ExecuteDynamicCodeSchema
                    {
                        Code = "Debug.Log(\"partial log\"); throw new NullReferenceException();"
                    },
                    CancellationToken.None);

                Assert.That(response.Success, Is.False);
                Assert.That(response.Logs, Contains.Item("partial log"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        private sealed class FakeDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
            private readonly Queue<ExecutionResult> _results;

            public FakeDynamicCodeExecutionRuntime(params ExecutionResult[] results)
            {
                _results = new Queue<ExecutionResult>(results);
            }

            public List<DynamicCodeExecutionRequest> Requests { get; } = new List<DynamicCodeExecutionRequest>();
            public List<DynamicCodeExecutionRequest> TryExecuteRequests { get; } = new List<DynamicCodeExecutionRequest>();

            public bool SupportsAutoPrewarm()
            {
                return true;
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                Requests.Add(new DynamicCodeExecutionRequest
                {
                    Code = request.Code,
                    ClassName = request.ClassName,
                    Parameters = request.Parameters,
                    CompileOnly = request.CompileOnly,
                    SecurityLevel = request.SecurityLevel,
                    YieldToForegroundRequests = request.YieldToForegroundRequests
                });
                return Task.FromResult(_results.Dequeue());
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                TryExecuteRequests.Add(new DynamicCodeExecutionRequest
                {
                    Code = request.Code,
                    ClassName = request.ClassName,
                    Parameters = request.Parameters,
                    CompileOnly = request.CompileOnly,
                    SecurityLevel = request.SecurityLevel,
                    YieldToForegroundRequests = request.YieldToForegroundRequests
                });
                return Task.FromResult<(bool, ExecutionResult)>((true, _results.Dequeue()));
            }
        }

        private static void MarkForegroundWarmupCompleted()
        {
            bool started = DynamicCodeForegroundWarmupState.TryBegin();
            if (!started)
            {
                return;
            }

            DynamicCodeForegroundWarmupState.MarkCompleted();
        }

        private sealed class CancellingDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
            public bool SupportsAutoPrewarm()
            {
                return true;
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}
