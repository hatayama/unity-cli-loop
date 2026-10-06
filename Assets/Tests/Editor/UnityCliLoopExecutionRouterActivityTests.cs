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
        /// Verifies a tool runs while one activity is held, and the activity ends once the tool returns.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Tool_HoldsAnActivityWhileTheToolRuns_AndReleasesItAfterwards()
        {
            RecordingProcessActivityApi api = new RecordingProcessActivityApi();
            ActivityObservingTool tool = new ActivityObservingTool(api);
            UnityCliLoopExecutionRouter router = CreateRouter(api, tool);

            UnityCliLoopToolResponse response = await router.ExecuteAsync(
                ActivityObservingTool.Name,
                new JObject(),
                CancellationToken.None);

            Assert.That(response, Is.InstanceOf<ActivityTestResponse>());
            Assert.That(tool.LiveCountWhileRunning, Is.EqualTo(1));
            Assert.That(api.BeginCount, Is.EqualTo(1));
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
        /// Tool that records how many activities were live while it ran.
        /// </summary>
        private sealed class ActivityObservingTool : IUnityCliLoopTool
        {
            public const string Name = "activity-observing-test";

            private readonly RecordingProcessActivityApi _api;

            public ActivityObservingTool(RecordingProcessActivityApi api)
            {
                _api = api;
            }

            public int? LiveCountWhileRunning { get; private set; }

            public string ToolName => Name;

            public ToolParameterSchema ParameterSchema => new ToolParameterSchema();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                LiveCountWhileRunning = _api.LiveCount;
                return Task.FromResult<UnityCliLoopToolResponse>(new ActivityTestResponse());
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
