using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies error responses serialize EditorPlaying from the injected editor-state reader.
    /// </summary>
    [TestFixture]
    public sealed class ExecuteDynamicCodeErrorEditorStateTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeForegroundWarmupState.Reset();
        }

        /// <summary>
        /// What: a compile-failure UseCase response JSON reports injected EditorPlaying=true.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCompilationFails_SerializesInjectedEditorPlaying()
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
                            ErrorCode = "CS0103",
                            Message = "The name 'Nope' does not exist in the current context"
                        }
                    }
                });
            ExecuteDynamicCodeUseCase useCase = CreateUseCase(runtime);

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return Nope;",
                    CompileOnly = true
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":true}"));
        }

        /// <summary>
        /// What: a cancelled-result UseCase response JSON reports injected EditorPlaying=true.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenResultIsCancelled_SerializesInjectedEditorPlaying()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED
                });
            ExecuteDynamicCodeUseCase useCase = CreateUseCase(runtime);

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":true}"));
        }

        /// <summary>
        /// What: a runtime-restarting UseCase response JSON reports injected EditorPlaying=true.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenResultIsRuntimeRestarting_SerializesInjectedEditorPlaying()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                DynamicCodeExecutionResponseFactory.CreateRuntimeRestartingExecutionResult());
            ExecuteDynamicCodeUseCase useCase = CreateUseCase(runtime);

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":true}"));
        }

        /// <summary>
        /// What: the OperationCanceledException catch path JSON reports injected EditorPlaying=true.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRuntimeThrowsOperationCanceled_SerializesInjectedEditorPlaying()
        {
            MarkForegroundWarmupCompleted();
            ExecuteDynamicCodeUseCase useCase = CreateUseCase(new OperationCanceledDynamicCodeExecutionRuntime());

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":true}"));
        }

        /// <summary>
        /// What: a paused response still serializes EditorPaused and ActivePausePointId next to EditorPlaying.
        /// </summary>
        [Test]
        public void ExecuteDynamicCodeResponse_WhenPaused_SerializesPlayingPausedAndActiveId()
        {
            ExecuteDynamicCodeResponse response = new()
            {
                Success = false,
                EditorPlaying = true,
                EditorPaused = true,
                ActivePausePointId = "jump"
            };

            Assert.That(
                SerializedEditorState(response),
                Is.EqualTo("{\"EditorPlaying\":true,\"EditorPaused\":true,\"ActivePausePointId\":\"jump\"}"));
        }

        private static ExecuteDynamicCodeUseCase CreateUseCase(IDynamicCodeExecutionRuntime runtime)
        {
            return new ExecuteDynamicCodeUseCase(
                runtime,
                new FakeDynamicCodeEditorStateReader(isPlaying: true, isPaused: false));
        }

        private static string SerializedEditorState(ExecuteDynamicCodeResponse response)
        {
            JObject serialized = JObject.Parse(
                JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));
            JObject state = new JObject();
            if (serialized["EditorPlaying"] != null)
            {
                state["EditorPlaying"] = serialized["EditorPlaying"];
            }

            if (serialized["EditorPaused"] != null)
            {
                state["EditorPaused"] = serialized["EditorPaused"];
            }

            if (serialized["ActivePausePointId"] != null)
            {
                state["ActivePausePointId"] = serialized["ActivePausePointId"];
            }

            return state.ToString(Formatting.None);
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

        private sealed class FakeDynamicCodeEditorStateReader : IDynamicCodeEditorStateReader
        {
            public FakeDynamicCodeEditorStateReader(bool isPlaying, bool isPaused)
            {
                IsPlaying = isPlaying;
                IsPaused = isPaused;
            }

            public bool IsPlaying { get; }

            public bool IsPaused { get; }
        }

        private sealed class FakeDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
            private readonly Queue<ExecutionResult> _results;

            public FakeDynamicCodeExecutionRuntime(params ExecutionResult[] results)
            {
                _results = new Queue<ExecutionResult>(results);
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_results.Dequeue());
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<(bool, ExecutionResult)>((true, _results.Dequeue()));
            }
        }

        private sealed class OperationCanceledDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
        {
            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException();
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new OperationCanceledException();
            }
        }
    }
}
