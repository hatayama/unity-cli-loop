using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Execute Dynamic Code Use Case behavior.
    /// </summary>
    [TestFixture]
    public class ExecuteDynamicCodeUseCaseTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeForegroundWarmupState.Reset();
        }

        [Test]
        public void ExecuteDynamicCodeResponse_WhenSerializedWithoutTimings_DoesNotExposeTimingControlFields()
        {
            // Tests that timing control remains an internal interface concern by default.
            ExecuteDynamicCodeResponse response = new()
            {
                Success = true,
                Result = "ok",
                EmitTimingsInJsonResponse = false
            };

            JObject serializedResponse = JObject.Parse(
                JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));

            Assert.That(serializedResponse["timings"], Is.Null);
            Assert.That(serializedResponse["emitTimingsInJsonResponse"], Is.Null);
            Assert.That(serializedResponse["emitsTimingsInJsonResponse"], Is.Null);
            Assert.That(serializedResponse["domainReloadWaitRequired"], Is.Null);
            Assert.That(serializedResponse["DomainReloadWaitRequired"], Is.Null);
        }

        [Test]
        public void ExecuteDynamicCodeResponse_WhenSerializedWithInternalSignals_KeepsLegacyControlFieldNames()
        {
            // Tests that protocol-2 CLIs can still strip internal wait and timing fields from stdout.
            ExecuteDynamicCodeResponse response = new()
            {
                Success = true,
                Result = "ok",
                DomainReloadWaitRequired = true,
                EmitTimingsInJsonResponse = true,
                Timings = new List<string> { "[Perf] Build: 1.0ms" }
            };

            JObject serializedResponse = JObject.Parse(
                JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));

            Assert.That(serializedResponse["success"], Is.Not.Null);
            Assert.That(serializedResponse["result"], Is.Not.Null);
            Assert.That(serializedResponse["DomainReloadWaitRequired"], Is.Not.Null);
            Assert.That(serializedResponse["Timings"], Is.Not.Null);
            Assert.That(serializedResponse["domainReloadWaitRequired"], Is.Null);
            Assert.That(serializedResponse["timings"], Is.Null);
        }

        [Test]
        public void DynamicCodeDomainReloadWaitSignal_WhenEditorReportsReloadWork_ShouldRequestWait()
        {
            // Tests that native CLI waits are driven by explicit Unity reload signals.
            ExecuteDynamicCodeSchema schema = new()
            {
                WaitForDomainReload = true,
                CompileOnly = false
            };

            bool shouldWait = DynamicCodeDomainReloadWaitSignal.ShouldRequestWait(
                schema,
                editorIsCompiling: true,
                reloadSignalObserved: false);
            bool reloadSignalShouldWait = DynamicCodeDomainReloadWaitSignal.ShouldRequestWait(
                schema,
                editorIsCompiling: false,
                reloadSignalObserved: true);

            Assert.That(shouldWait, Is.True);
            Assert.That(reloadSignalShouldWait, Is.True);
        }

        [Test]
        public void DynamicCodeDomainReloadWaitSignal_WhenRequestCannotWait_ShouldNotRequestWait()
        {
            // Tests that compile-only and explicit no-wait requests keep their fast paths.
            ExecuteDynamicCodeSchema compileOnlySchema = new()
            {
                WaitForDomainReload = true,
                CompileOnly = true
            };
            ExecuteDynamicCodeSchema noWaitSchema = new()
            {
                WaitForDomainReload = false,
                CompileOnly = false
            };

            bool compileOnlyShouldWait = DynamicCodeDomainReloadWaitSignal.ShouldRequestWait(
                compileOnlySchema,
                editorIsCompiling: true,
                reloadSignalObserved: true);
            bool noWaitShouldWait = DynamicCodeDomainReloadWaitSignal.ShouldRequestWait(
                noWaitSchema,
                editorIsCompiling: true,
                reloadSignalObserved: true);

            Assert.That(compileOnlyShouldWait, Is.False);
            Assert.That(noWaitShouldWait, Is.False);
        }

        [Test]
        public void DynamicCodeDomainReloadWaitSignal_WhenDisposedOffMainThread_QueuesEditorEventCleanup()
        {
            // Tests that timeout continuations do not remove UnityEditor event handlers off the editor thread.
            QueuedMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);
            DynamicCodeDomainReloadWaitSignal signal = DynamicCodeDomainReloadWaitSignal.Start(
                new ExecuteDynamicCodeSchema
                {
                    WaitForDomainReload = true,
                    CompileOnly = false
                });

            try
            {
                signal.Dispose();

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));

                dispatcher.RunQueuedContinuationsAsMainThread();

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(0));
            }
            finally
            {
                signal.Dispose();
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenInitialCompilationLooksLikeMissingReturn_ShouldRetryOnce()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenMissingReturnFailureHasUpdatedCode_ShouldRetryOnce()
        {
            // Tests that wrapped compiler output does not block the missing-return recovery retry.
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    UpdatedCode = "if (condition) return \"x\";",
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "if (condition) return \"x\";",
                    CompileOnly = false
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(runtime.Requests, Has.Count.EqualTo(2));
            Assert.That(runtime.Requests[1].Code, Does.Contain("return null;"));
        }

        [Test]
        public async Task ExecuteAsync_WhenRawTypeDeclarationHasMissingReturnFailure_ShouldNotRetry()
        {
            // Tests that missing-return recovery stays limited to script-style top-level snippets.
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    UpdatedCode = "public class Sample { public string Run(bool condition) { if (condition) return \"x\"; } }",
                    CompilationErrors = new List<CompilationError>
                    {
                        new CompilationError
                        {
                            ErrorCode = "CS0161",
                            Message = "Not all code paths return a value"
                        }
                    }
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "public class Sample { public string Run(bool condition) { if (condition) return \"x\"; } }",
                    CompileOnly = false
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(runtime.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_WhenYieldingRequestNeedsMissingReturnRetry_ShouldPreserveYieldingOnRetry()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenYieldingStartupProbeSucceeds_ShouldSkipNextForegroundWarmup()
        {
            // Tests that tool readiness warmup prevents the next user request from paying hidden warmup cost.
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = true,
                    Result = "probe"
                },
                new ExecutionResult
                {
                    Success = true,
                    Result = "user"
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse probeResponse = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return \"probe\";",
                    YieldToForegroundRequests = true
                },
                CancellationToken.None);
            ExecuteDynamicCodeResponse userResponse = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return \"user\";"
                },
                CancellationToken.None);

            Assert.That(probeResponse.Success, Is.True);
            Assert.That(userResponse.Success, Is.True);
            Assert.That(runtime.TryExecuteRequests, Has.Count.EqualTo(1));
            Assert.That(runtime.Requests, Has.Count.EqualTo(1));
            Assert.That(runtime.Requests[0].Code, Is.EqualTo("return \"user\";"));
        }

        [Test]
        public async Task ExecuteAsync_WhenInitialExecutionSucceeds_ShouldNotRetry()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenFirstForegroundExecutionRuns_ShouldWarmHiddenPathBeforeUserCode()
        {
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = true,
                    Result = "warm"
                },
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return 1;"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(runtime.Requests, Has.Count.EqualTo(3));
            AssertPrewarmCodeMatchesLiteralReturnShape(
                runtime.Requests[0].Code,
                "return \"user value\";");
            AssertPrewarmCodeMatchesLiteralReturnShape(
                runtime.Requests[1].Code,
                "return\n  \"user value\";");
            Assert.That(runtime.Requests[2].Code, Is.EqualTo("return 1;"));
        }

        [Test]
        public async Task ExecuteAsync_WhenForegroundWarmupAlreadyCompleted_ShouldNotRepeatIt()
        {
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = true,
                    Result = "warm"
                },
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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
            Assert.That(runtime.Requests, Has.Count.EqualTo(4));
            Assert.That(runtime.Requests[3].Code, Is.EqualTo("return 2;"));
        }

        [Test]
        public async Task ExecuteAsync_WhenWarmupFailsButForegroundExecutionSucceeds_ShouldNotRepeatWarmupOnNextRequest()
        {
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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
            AssertPrewarmCodeMatchesLiteralReturnShape(
                runtime.Requests[0].Code,
                "return \"user value\";");
            Assert.That(runtime.Requests[1].Code, Is.EqualTo("return 1;"));
            Assert.That(runtime.Requests[2].Code, Is.EqualTo("return 2;"));
        }

        [Test]
        public async Task ExecuteAsync_WhenRequestIsCompileOnly_ShouldSkipForegroundWarmup()
        {
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = true,
                    Result = "ok"
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenRetryAfterMissingReturnStillFails_ShouldReturnRetryDiagnostics()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenDiagnosticLineUsesTwoDigits_ShouldAlignCaretWithRenderedPrefix()
        {
            MarkForegroundWarmupCompleted();
            string updatedCode = string.Join(
                "\n",
                Enumerable.Range(1, 12).Select(index => index == 10 ? "abcd" : $"line{index}"));
            FakeDynamicCodeExecutionRuntime runtime = new(
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);
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

        [Test]
        public async Task ExecuteAsync_WhenRuntimeThrowsOperationCanceledException_ShouldReturnNeutralCancelledResponse()
        {
            MarkForegroundWarmupCompleted();
            CancellingDynamicCodeExecutionRuntime runtime = new();
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return 1;"
                },
                cancellationTokenSource.Token);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
            Assert.That(response.Logs, Contains.Item("Execution cancelled"));
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeReturnsCancelledResult_ShouldPreserveNeutralCancelledResponse()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                    Logs = new List<string> { "Execution cancelled" },
                    Timings = new List<string> { "compile_ms=1" }
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return 1;"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
            Assert.That(response.Logs, Contains.Item("Execution cancelled"));
            Assert.That(response.Timings, Contains.Item("compile_ms=1"));
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeFailsAfterProducingLogs_ShouldPreserveOriginalLogs()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Object reference not set to an instance of an object",
                    Logs = new List<string> { "partial log" }
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "Debug.Log(\"partial log\"); throw new NullReferenceException();"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Logs, Contains.Item("partial log"));
        }

        [Test]
        public async Task ExecuteAsync_WhenRuntimeReturnsKnownCompilePattern_ShouldReturnFriendlyGuidance()
        {
            // Tests that known dynamic-code compile failures keep the CLI-facing guidance text.
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Compilation error occurred",
                    Logs = new List<string> { "Top-level statements must precede namespace and type declarations." }
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "namespace Bad { class Wrapped {} }"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo("There is an issue with the code structure"));
            Assert.That(response.Logs, Contains.Item("Solutions:"));
        }

        [Test]
        public async Task ExecuteAsync_WhenKnownCompilePatternExistsOnlyInCompilationErrors_ShouldReturnFriendlyGuidance()
        {
            // Tests that friendly guidance is derived from structured compiler diagnostics before logs.
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
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
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);
            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "namespace Bad { class Wrapped {} }"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo("There is an issue with the code structure"));
            Assert.That(response.Logs, Contains.Item("Solutions:"));
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
            private readonly Queue<ExecutionResult> _results;

            public FakeDynamicCodeExecutionRuntime(params ExecutionResult[] results)
            {
                _results = new Queue<ExecutionResult>(results);
            }

            public List<DynamicCodeExecutionRequest> Requests { get; } = new List<DynamicCodeExecutionRequest>();
            public List<DynamicCodeExecutionRequest> TryExecuteRequests { get; } = new List<DynamicCodeExecutionRequest>();

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

        private static void AssertPrewarmCodeMatchesLiteralReturnShape(
            string prewarmCode,
            string userCode)
        {
            PreparedDynamicCode prewarm = DynamicCodeSourcePreparer.Prepare(
                prewarmCode,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);
            PreparedDynamicCode userReturn = DynamicCodeSourcePreparer.Prepare(
                userCode,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(prewarm.PreparedSource, Is.EqualTo(userReturn.PreparedSource));
        }

        private static void RestoreEditorMainThreadDispatcher()
        {
            io.github.hatayama.UnityCliLoop.Infrastructure.EditorMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }

        private sealed class QueuedMainThreadDispatcher : IMainThreadDispatcher
        {
            private readonly Queue<Action> _continuations = new();
            private bool _isMainThread;

            public bool IsMainThread => _isMainThread;

            public int PendingContinuationCount => _continuations.Count;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
                _continuations.Enqueue(continuation);
            }

            public void RunQueuedContinuationsAsMainThread()
            {
                _isMainThread = true;
                try
                {
                    while (_continuations.Count > 0)
                    {
                        Action continuation = _continuations.Dequeue();
                        continuation();
                    }
                }
                finally
                {
                    _isMainThread = false;
                }
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class CancellingDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
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
