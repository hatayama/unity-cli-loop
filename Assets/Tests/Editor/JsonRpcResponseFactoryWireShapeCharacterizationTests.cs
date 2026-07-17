using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Freezes the exact serialized JSON string for every JSON-RPC response shape
    /// (success / error / dispatch-accepted / heartbeat / protocol-mismatch) before the
    /// R3-5 move to JsonRpcResponseFactory, so the move can be verified byte-equal.
    /// </summary>
    public sealed class JsonRpcResponseFactoryWireShapeCharacterizationTests
    {
        [Test]
        public async Task ProcessRequest_WhenToolSucceeds_ProducesFrozenSuccessJson()
        {
            // Verifies the success response wire shape is byte-equal to the frozen baseline.
            UnityCliLoopToolRegistrarService service = UnityCliLoopToolRegistrarTestFactory.Create(UnityCliLoopToolDiscovery.DiscoverTools);
            service.RegisterCustomTool(new DeterministicSuccessTool());
            JsonRpcRequestProcessor processor = CreateProcessor(service);

            string response = await processor.ProcessRequest(BuildToolRequest(DeterministicSuccessTool.Name, 1), CancellationToken.None);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"Success\":true}}"));
        }

        [Test]
        public async Task ProcessRequest_WhenToolIsDisabled_ProducesFrozenErrorJson()
        {
            // Verifies the internal_error response wire shape is byte-equal to the frozen baseline.
            InMemoryToolSettingsPort toolSettingsPort = new();
            toolSettingsPort.SetToolEnabled(DeterministicSuccessTool.Name, false);
            UnityCliLoopToolRegistrarService service = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            service.RegisterCustomTool(new DeterministicSuccessTool());
            JsonRpcRequestProcessor processor = CreateProcessor(service);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[JsonRpcRequestProcessor\] Error: Tool 'deterministic-success' is disabled"));
            string response = await processor.ProcessRequest(BuildToolRequest(DeterministicSuccessTool.Name, 1), CancellationToken.None);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603,\"message\":\"Tool 'deterministic-success' is disabled. To enable it, go to Window > Unity CLI Loop > Settings\",\"data\":{\"type\":\"internal_error\",\"message\":\"This tool has been disabled in project settings.\"}}}"));
        }

        [Test]
        public void CreateDispatchAcceptedResponse_WithHeartbeatNegotiated_ProducesFrozenJson()
        {
            // Verifies the dispatch-accepted-with-heartbeat wire shape is byte-equal to the frozen baseline.
            string response = JsonRpcResponseFactory.CreateDispatchAcceptedResponse(1, 10);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"accepted\":true},\"uloop\":{\"phase\":\"accepted\",\"heartbeatIntervalSeconds\":10}}"));
        }

        [Test]
        public void CreateDispatchAcceptedResponse_WithoutHeartbeat_ProducesFrozenJson()
        {
            // Verifies the dispatch-accepted-without-heartbeat wire shape is byte-equal to the frozen baseline.
            string response = JsonRpcResponseFactory.CreateDispatchAcceptedResponse(1, 0);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"accepted\":true},\"uloop\":{\"phase\":\"accepted\"}}"));
        }

        [Test]
        public void CreateHeartbeatResponse_WhenSerialized_ProducesFrozenJson()
        {
            // Verifies the heartbeat frame wire shape is byte-equal to the frozen baseline.
            string response = JsonRpcResponseFactory.CreateHeartbeatResponse(7, 12.5);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"alive\":true},\"uloop\":{\"phase\":\"heartbeat\",\"mainThreadStallSeconds\":12.5}}"));
        }

        [Test]
        public void CreateErrorResponse_ForBusyException_IncludesSecondsSinceLastMainThreadTick()
        {
            // Verifies busy error responses carry secondsSinceLastMainThreadTick so clients can
            // distinguish a live main thread from a frozen Editor while BUSY.
            UnityCliLoopToolBusyException busyException = new(
                "running-tool", "requested-tool", isPlaying: true, isPaused: true);

            string response = JsonRpcResponseFactory.CreateErrorResponse(1, busyException);

            JObject json = JObject.Parse(response);
            JToken dataToken = json["error"]!["data"]!;
            Assert.That(dataToken["type"]!.Value<string>(), Is.EqualTo(JsonRpcErrorTypes.ServerBusy));
            Assert.That(dataToken["secondsSinceLastMainThreadTick"], Is.Not.Null);
            Assert.That(dataToken["secondsSinceLastMainThreadTick"]!.Value<double>(), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task ProcessRequest_WhenProtocolVersionIsTooOld_ProducesFrozenMismatchJson()
        {
            // Verifies the CLI-update-required wire shape is byte-equal to the frozen baseline.
            UnityCliLoopToolRegistrarService service = UnityCliLoopToolRegistrarTestFactory.Create(UnityCliLoopToolDiscovery.DiscoverTools);
            JsonRpcRequestProcessor processor = CreateProcessor(service);

            string request =
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1,\"uloop\":{\"protocolVersion\":" +
                (CliConstants.REQUIRED_CLI_PROTOCOL_VERSION - 1) +
                "}}";
            string response = await processor.ProcessRequest(request, CancellationToken.None);

            Assert.That(response, Is.EqualTo(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603,\"message\":\"The installed uloop CLI uses an IPC protocol that does not match this Unity package.\",\"data\":{\"type\":\"cli_update_required\",\"currentCliVersion\":null,\"currentProtocolVersion\":" +
                (CliConstants.REQUIRED_CLI_PROTOCOL_VERSION - 1) +
                ",\"requiredProtocolVersion\":" +
                CliConstants.REQUIRED_CLI_PROTOCOL_VERSION +
                ",\"updateCommand\":\"uloop update\",\"retryableAfterUpdate\":true,\"message\":\"Install matching uloop CLI and Unity package versions, then retry the original command.\"}}}"));
        }

        private static JsonRpcRequestProcessor CreateProcessor(UnityCliLoopToolRegistrarService service)
        {
            UnityCliLoopExecutionRouter executionRouter = new(service);
            return new JsonRpcRequestProcessor(executionRouter);
        }

        private static string BuildToolRequest(string toolName, int id)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"" +
                toolName +
                "\",\"params\":{},\"id\":" +
                id +
                ",\"uloop\":{\"protocolVersion\":" +
                CliConstants.REQUIRED_CLI_PROTOCOL_VERSION +
                "}}";
        }

        private sealed class DeterministicSuccessResponse : UnityCliLoopToolResponse
        {
        }

        private sealed class DeterministicSuccessTool : IUnityCliLoopTool
        {
            public const string Name = "deterministic-success";

            public string ToolName => Name;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new DeterministicSuccessResponse());
            }
        }

        private sealed class InMemoryToolSettingsPort : IToolSettingsPort
        {
            private readonly System.Collections.Generic.HashSet<string> _disabledTools = new();

            public bool IsToolEnabled(string toolName)
            {
                return !_disabledTools.Contains(toolName);
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
                if (enabled)
                {
                    _disabledTools.Remove(toolName);
                    return;
                }

                _disabledTools.Add(toolName);
            }

            public string[] GetDisabledTools()
            {
                string[] disabledTools = new string[_disabledTools.Count];
                _disabledTools.CopyTo(disabledTools);
                return disabledTools;
            }

            public void InvalidateCache()
            {
            }
        }
    }
}
