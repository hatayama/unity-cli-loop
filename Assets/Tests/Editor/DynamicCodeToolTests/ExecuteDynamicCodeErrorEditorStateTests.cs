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
    /// Verifies error responses serialize EditorPlaying always and omit EditorPaused when false.
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
        /// What: a compile-failure UseCase response JSON always has EditorPlaying and omits EditorPaused.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCompilationFails_SerializesEditorPlayingAndOmitsPausedFields()
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
            ExecuteDynamicCodeUseCase useCase = new(runtime);

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "return Nope;",
                    CompileOnly = true
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(SerializedEditorState(response), Is.EqualTo("{\"EditorPlaying\":false}"));
        }

        /// <summary>
        /// What: a runtime-exception UseCase response JSON always has EditorPlaying and omits EditorPaused.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenRuntimeThrows_SerializesEditorPlayingAndOmitsPausedFields()
        {
            MarkForegroundWarmupCompleted();
            FakeDynamicCodeExecutionRuntime runtime = new(
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Object reference not set to an instance of an object"
                });
            ExecuteDynamicCodeUseCase useCase = new(runtime);

            ExecuteDynamicCodeResponse response = await useCase.ExecuteAsync(
                new ExecuteDynamicCodeSchema
                {
                    Code = "object value = null; return value.ToString();"
                },
                CancellationToken.None);

            Assert.That(response.Success, Is.False);
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
    }
}
