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
    /// Verifies JSON-RPC notification handling in JsonRpcRequestProcessor, and how it tells the
    /// execution path whether uloop cancel may stop a request.
    /// </summary>
    public sealed class JsonRpcRequestProcessorTests
    {
        /// <summary>
        /// Verifies a JSON-RPC notification produces no response frame, even without CLI protocol metadata.
        /// </summary>
        [TestCase("{\"jsonrpc\":\"2.0\",\"method\":\"focus-window\"}")]
        [TestCase("{\"jsonrpc\":\"2.0\",\"method\":\"unknown-notification\",\"id\":null}")]
        public void ProcessRequest_WhenRequestIsNotification_ReturnsNoResponse(string notificationJson)
        {
            JsonRpcRequestProcessor processor = CreateProcessor();

            Task<string> responseTask = processor.ProcessRequest(notificationJson, CancellationToken.None);

            Assert.That(responseTask.IsCompleted, Is.True);
            Assert.That(responseTask.GetAwaiter().GetResult(), Is.Null);
        }

        /// <summary>
        /// Verifies uloop cancel may stop exactly the requests a client disconnect would cancel:
        /// compile only when it does not wait for the domain reload, run-tests unless it runs in
        /// PlayMode respecting the Enter Play Mode settings, with or without a dispatch ack.
        /// </summary>
        [TestCase("compile", "{}", false, "NotCancellable")]
        [TestCase("compile", "{\"WaitForDomainReload\":true}", false, "NotCancellable")]
        [TestCase("compile", "{\"WaitForDomainReload\":false}", false, "Cancelled")]
        [TestCase("compile", "{\"WaitForDomainReload\":false}", true, "Cancelled")]
        [TestCase("compile", "{}", true, "NotCancellable")]
        [TestCase("run-tests", "{\"TestMode\":\"PlayMode\",\"RespectEnterPlayModeSettings\":true}", false, "NotCancellable")]
        [TestCase("run-tests", "{\"TestMode\":\"PlayMode\",\"RespectEnterPlayModeSettings\":true}", true, "NotCancellable")]
        [TestCase("run-tests", "{\"TestMode\":\"EditMode\"}", false, "Cancelled")]
        [TestCase("run-tests", "{\"TestMode\":\"EditMode\"}", true, "Cancelled")]
        public async Task ProcessRequest_WhileToolRuns_CancelOutcomeFollowsClientDisconnectPolicy(
            string toolName,
            string paramsJson,
            bool acceptsDispatchAck,
            string expectedOutcome)
        {
            PendingCancellableTool tool = new PendingCancellableTool(toolName);
            UnityCliLoopToolRegistrarService registrarService = CreateRegistrarService(tool);
            JsonRpcRequestProcessor processor = CreateProcessor(registrarService);
            string requestJson = BuildToolRequest(toolName, paramsJson, acceptsDispatchAck);

            Task<string> responseTask = processor.ProcessRequestWithEarlyResponseAsync(
                requestJson,
                CancellationToken.None,
                (responseJson, cancelOnClientDisconnect, createHeartbeatJson) => Task.CompletedTask);
            Assert.That(responseTask.IsCompleted, Is.False);

            UnityCliLoopCancelRequestResult cancelResult = registrarService.CancelRunningTool();
            // Why complete the tool either way: a NotCancellable request keeps running and the test
            // must not leave it pending.
            tool.Complete();
            await AwaitWithTimeout(responseTask);

            Assert.That(cancelResult.Outcome, Is.EqualTo(expectedOutcome));
        }

        /// <summary>
        /// Verifies a request stopped by uloop cancel is answered with a tool_cancelled error that
        /// names the tool, and that the expected cancel is not logged as an error.
        /// </summary>
        [Test]
        public async Task ProcessRequest_WhenToolIsCancelledByUser_AnswersToolCancelledErrorWithoutLogging()
        {
            PendingCancellableTool tool = new PendingCancellableTool("cancel-target-tool");
            UnityCliLoopToolRegistrarService registrarService = CreateRegistrarService(tool);
            JsonRpcRequestProcessor processor = CreateProcessor(registrarService);

            Task<string> responseTask = processor.ProcessRequest(
                BuildToolRequest("cancel-target-tool", "{}", false),
                CancellationToken.None);
            registrarService.CancelRunningTool();
            await AwaitWithTimeout(responseTask);

            JObject error = (JObject)JObject.Parse(await responseTask)["error"];
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Value<string>("message"), Is.EqualTo("'cancel-target-tool' was cancelled by uloop cancel."));
            JObject data = (JObject)error["data"];
            Assert.That(data.Value<string>("type"), Is.EqualTo("tool_cancelled"));
            Assert.That(data.Value<string>("runningToolName"), Is.EqualTo("cancel-target-tool"));
            Assert.That(data.Value<string>("message"), Is.EqualTo("'cancel-target-tool' was cancelled by uloop cancel."));
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        private static async Task AwaitWithTimeout(Task<string> task)
        {
            Task completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.That(completedTask, Is.SameAs(task), "The request did not finish before the test timeout.");
        }

        private static string BuildToolRequest(string toolName, string paramsJson, bool acceptsDispatchAck)
        {
            string dispatchAck = acceptsDispatchAck ? ",\"acceptsDispatchAck\":true" : string.Empty;
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"" + toolName +
                "\",\"params\":" + paramsJson +
                ",\"id\":1,\"uloop\":{\"protocolVersion\":" + CliConstants.REQUIRED_CLI_PROTOCOL_VERSION +
                dispatchAck + "}}";
        }

        private static UnityCliLoopToolRegistrarService CreateRegistrarService(IUnityCliLoopTool tool)
        {
            return new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AllToolsEnabledSettingsPort(),
                new UnityCliLoopToolExecutionService(new IdleEditorRuntimeStatePort(), new ToolExecutionSession()),
                () => new[] { tool });
        }

        private static JsonRpcRequestProcessor CreateProcessor(UnityCliLoopToolRegistrarService registrarService)
        {
            return new JsonRpcRequestProcessor(new UnityCliLoopExecutionRouter(registrarService, new EditorExecutionActivity(new InertProcessActivityApi()), new EditorDialogTracker()));
        }

        // Stays pending until its token fires or the test completes it, so the test can cancel it
        // while it holds the execution slot.
        private sealed class PendingCancellableTool : IUnityCliLoopTool
        {
            private readonly TaskCompletionSource<UnityCliLoopToolResponse> _completion =
                new TaskCompletionSource<UnityCliLoopToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingCancellableTool(string toolName)
            {
                ToolName = toolName;
            }

            public string ToolName { get; }

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                ct.Register(() => _completion.TrySetCanceled(ct));
                return _completion.Task;
            }

            public void Complete()
            {
                _completion.TrySetResult(new PendingCancellableResponse());
            }
        }

        private sealed class PendingCancellableResponse : UnityCliLoopToolResponse
        {
        }

        private static JsonRpcRequestProcessor CreateProcessor()
        {
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AllToolsEnabledSettingsPort(),
                new UnityCliLoopToolExecutionService(new IdleEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
            return new JsonRpcRequestProcessor(new UnityCliLoopExecutionRouter(registrarService, new EditorExecutionActivity(new InertProcessActivityApi()), new EditorDialogTracker()));
        }

        private sealed class AllToolsEnabledSettingsPort : IToolSettingsPort
        {
            public bool IsToolEnabled(string toolName)
            {
                return true;
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
            }

            public string[] GetDisabledTools()
            {
                return Array.Empty<string>();
            }

            public void InvalidateCache()
            {
            }
        }

        private sealed class IdleEditorRuntimeStatePort : IEditorRuntimeStatePort
        {
            public bool IsCompiling => false;
            public bool IsUpdating => false;
            public bool IsPlaying => false;
            public bool IsPaused => false;
        }
    }
}
