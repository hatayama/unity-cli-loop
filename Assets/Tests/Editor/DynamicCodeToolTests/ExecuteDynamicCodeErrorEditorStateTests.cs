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
        /// What: EditorPlaying=false still appears in JSON instead of being omitted.
        /// </summary>
        [Test]
        public void ExecuteDynamicCodeResponse_WhenNotPlaying_StillSerializesEditorPlayingFalse()
        {
            ExecuteDynamicCodeResponse response = new()
            {
                Success = false,
                EditorPlaying = false
            };

            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":false}"));
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

        /// <summary>
        /// What: the dynamic-code Warning is omitted when empty and serialized when present.
        /// </summary>
        [Test]
        public void ExecuteDynamicCodeResponse_WhenWarningChanges_UsesOptionalJsonContract()
        {
            ExecuteDynamicCodeResponse response = new();
            JObject omitted = JObject.Parse(JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));
            Assert.That(omitted.Property("Warning"), Is.Null);

            response.Warning = "focus editor";
            JObject populated = JObject.Parse(JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));
            Assert.That(populated["Warning"]?.Value<string>(), Is.EqualTo("focus editor"));
        }

        /// <summary>
        /// What: opted-in partial results flow from ExecutionResult through the response serializer.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenPartialResultsExist_SerializesTheirValues()
        {
            DynamicCodeExecutionResponseFactory factory = new();
            ExecutionResult result = new()
            {
                Success = false,
                ErrorMessage = "execution failed",
                PartialResults = new Dictionary<string, string>
                {
                    ["completedSteps"] = "2"
                }
            };

            ExecuteDynamicCodeResponse response = factory.ConvertExecutionResultToResponse(result);
            JObject serialized = JObject.Parse(
                JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));

            Assert.That(response.PartialResults["completedSteps"], Is.EqualTo("2"));
            Assert.That(serialized["PartialResults"]?["completedSteps"]?.Value<string>(), Is.EqualTo("2"));
        }

        /// <summary>
        /// What: empty partial results remain absent from the dynamic-code JSON contract.
        /// </summary>
        [Test]
        public void ExecuteDynamicCodeResponse_WhenPartialResultsAreEmpty_OmitsTheirJsonKey()
        {
            ExecuteDynamicCodeResponse response = new();

            JObject serialized = JObject.Parse(
                JsonConvert.SerializeObject(response, JsonRpcResponseSerializer.Settings));

            Assert.That(serialized.Property("PartialResults"), Is.Null);
        }

        /// <summary>
        /// What: an injected unfocused provider adds the exact hint to a Play Mode response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenPlayingAndEditorIsUnfocused_ReturnsExactHint()
        {
            MarkForegroundWarmupCompleted();
            ExecuteDynamicCodeUseCase useCase = new ExecuteDynamicCodeUseCase(
                new FakeDynamicCodeExecutionRuntime(new ExecutionResult { Success = true, Result = "ok" }),
                new FakeDynamicCodeEditorStateReader(isPlaying: true, isPaused: false),
                new FakeEditorFocusStateProvider(false));

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema { Code = "return 1;" },
                CancellationToken.None);

            Assert.That(response.Warning, Is.EqualTo("The Unity Editor is unfocused while Play Mode is running, so Play Mode progress may be throttled. Run `uloop focus-window`, or use the `pause-point --await`/`--trigger` flow instead of polling for progress."));
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

        private sealed class FakeEditorFocusStateProvider : IEditorFocusStateProvider
        {
            public FakeEditorFocusStateProvider(bool isFocused)
            {
                IsFocused = isFocused;
            }

            public bool IsFocused { get; }
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
