using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the execution router holds an operating-system activity for exactly the length of each
    /// tool or internal bridge command, and never for the editor status answer.
    /// </summary>
    public sealed class UnityCliLoopExecutionRouterActivityTests
    {
        /// <summary>
        /// Verifies the activity stays held while a tool's task is still pending, and ends only once the
        /// tool completes, so an asynchronous command is covered for its whole length.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Tool_HoldsTheActivityWhileTheToolIsPending_AndReleasesItWhenItCompletes()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            PendingTool tool = new PendingTool(api);
            UnityCliLoopExecutionRouter router = CreateRouter(api, tool);

            Task<UnityCliLoopToolResponse> run = router.ExecuteAsync(
                PendingTool.Name,
                new JObject(),
                CancellationToken.None);
            try
            {
                // Why these are read before the tool completes: a router that released the hold before
                // awaiting the tool would already show the activity as ended here. A tool that completes
                // synchronously cannot tell the two apart, because the whole call finishes before it returns.
                Assert.That(run.IsCompleted, Is.False);
                Assert.That(tool.LiveCountWhileRunning, Is.EqualTo(1));
                Assert.That(api.LiveCount, Is.EqualTo(1));
                Assert.That(api.EndedTokens, Is.Empty);
            }
            finally
            {
                // Why in finally: a failed assertion must not leave the tool's task pending.
                tool.Complete(new ActivityTestResponse());
            }

            UnityCliLoopToolResponse response = await run;

            Assert.That(response, Is.InstanceOf<ActivityTestResponse>());
            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.EndedTokens.Count, Is.EqualTo(1));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a tool that throws still releases its activity, and the caller sees the tool's exception.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_ToolThrows_ReleasesTheActivity_AndRethrows()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            UnityCliLoopExecutionRouter router = CreateRouter(api, new ThrowingTool());

            Exception caught = null;
            try
            {
                await router.ExecuteAsync(ThrowingTool.Name, new JObject(), CancellationToken.None);
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.InstanceOf<InvalidOperationException>());
            Assert.That(caught.Message, Is.EqualTo(ThrowingTool.FailureMessage));
            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.EndedTokens.Count, Is.EqualTo(1));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an internal bridge command canceled before it starts releases its activity, and the
        /// caller still sees the cancellation.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_InternalCommandCanceledBeforeItStarts_ReleasesTheActivity()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            UnityCliLoopExecutionRouter router = CreateRouter(api);

            // Why the exception is caught into a variable: a test that ends Canceled is recorded as passed.
            Exception caught = null;
            try
            {
                await router.ExecuteAsync(
                    UnityCliLoopConstants.COMMAND_NAME_GET_VERSION,
                    new JObject(),
                    new CancellationToken(true));
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.EndedTokens.Count, Is.EqualTo(1));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the editor status answer starts no activity, because it must answer even while the
        /// Editor main thread is blocked and so stays free of anything that can block or fail.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_EditorStatus_DoesNotBeginAnActivity()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            UnityCliLoopExecutionRouter router = CreateRouter(api);

            UnityCliLoopToolResponse response = await router.ExecuteAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_EDITOR_STATUS,
                new JObject(),
                CancellationToken.None);

            Assert.That(response, Is.InstanceOf<GetEditorStatusResponse>());
            Assert.That(api.BeginCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an internal bridge command holds one activity and ends it when the command returns.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_InternalCommand_HoldsAndReleasesAnActivity()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            UnityCliLoopExecutionRouter router = CreateRouter(api);

            UnityCliLoopToolResponse response = await router.ExecuteAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_VERSION,
                new JObject(),
                CancellationToken.None);

            Assert.That(response, Is.InstanceOf<GetVersionResponse>());
            Assert.That(api.BeginCount, Is.EqualTo(1));
            Assert.That(api.EndedTokens.Count, Is.EqualTo(1));
            Assert.That(api.LiveCount, Is.EqualTo(0));
        }

        private static UnityCliLoopExecutionRouter CreateRouter(
            RecordingProcessActivityApi api,
            params IUnityCliLoopTool[] tools)
        {
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AlwaysEnabledToolSettingsPort(),
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                () => tools);
            return new UnityCliLoopExecutionRouter(registrarService, new EditorExecutionActivity(api));
        }

        /// <summary>
        /// Tool whose task stays pending until the test completes it, and that records how many
        /// activities were live when it started.
        /// </summary>
        private sealed class PendingTool : IUnityCliLoopTool
        {
            public const string Name = "activity-pending-test";

            private readonly RecordingProcessActivityApi _api;
            private readonly TaskCompletionSource<UnityCliLoopToolResponse> _completion =
                new TaskCompletionSource<UnityCliLoopToolResponse>();

            public PendingTool(RecordingProcessActivityApi api)
            {
                _api = api;
            }

            public int? LiveCountWhileRunning { get; private set; }

            public string ToolName => Name;

            public ToolParameterSchema ParameterSchema => new ToolParameterSchema();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                LiveCountWhileRunning = _api.LiveCount;
                return _completion.Task;
            }

            public void Complete(UnityCliLoopToolResponse response)
            {
                _completion.TrySetResult(response);
            }
        }

        /// <summary>
        /// Tool whose execution fails.
        /// </summary>
        private sealed class ThrowingTool : IUnityCliLoopTool
        {
            public const string Name = "activity-throwing-test";
            public const string FailureMessage = "tool failed in this test";

            public string ToolName => Name;

            public ToolParameterSchema ParameterSchema => new ToolParameterSchema();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromException<UnityCliLoopToolResponse>(new InvalidOperationException(FailureMessage));
            }
        }

        /// <summary>
        /// Response returned by the test tools.
        /// </summary>
        private sealed class ActivityTestResponse : UnityCliLoopToolResponse
        {
        }
    }
}
