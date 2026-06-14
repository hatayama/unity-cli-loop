using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the JSON-RPC CLI compatibility gate before Unity tool execution.
    /// </summary>
    public class JsonRpcProcessorCliVersionGateTests
    {
        [Test]
        public async Task ProcessRequest_WhenProtocolVersionMatches_AllowsRequest()
        {
            // Verifies CLI clients that speak the required IPC protocol can execute bridge commands.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION),
                CancellationToken.None);
            JObject parsed = JObject.Parse(response);

            Assert.That(parsed["error"], Is.Null);
            Assert.That(parsed["result"], Is.Not.Null);
        }

        [Test]
        public async Task ProcessRequest_WhenProtocolVersionIsNewer_ReturnsCliProtocolMismatchError()
        {
            // Verifies future protocol clients are rejected because IPC compatibility is exact-match.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION + 1),
                CancellationToken.None);
            JObject error = ParseError(response);
            JObject data = ParseErrorData(response);

            Assert.That(error["message"]?.ToString(), Does.Contain("does not match"));
            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(
                data["currentProtocolVersion"]?.ToObject<int>(),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION + 1));
            Assert.That(
                data["requiredProtocolVersion"]?.ToObject<int>(),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION));
            Assert.That(data["updateCommand"], Is.Null);
        }

        [Test]
        public async Task ProcessRequest_WhenProtocolVersionIsTooOld_ReturnsCliUpdateRequiredError()
        {
            // Verifies CLIs on an older IPC protocol receive CLI update instructions before any tool runs.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION - 1),
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(
                data["currentProtocolVersion"]?.ToObject<int>(),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION - 1));
            Assert.That(
                data["requiredProtocolVersion"]?.ToObject<int>(),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION));
            Assert.That(data["updateCommand"]?.ToString(), Is.EqualTo(ExpectedCliUpdateCommand()));
            Assert.That(data["retryableAfterUpdate"]?.ToObject<bool>(), Is.True);
        }

        [Test]
        public async Task ProcessRequest_WhenClientNegotiatesHeartbeat_WritesAcceptedHandshakeShape()
        {
            // Verifies the request metadata drives the accepted response and heartbeat frame contract.
            string acceptedResponse = null;
            Func<string> createHeartbeatJson = null;

            string response = await JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                BuildHeartbeatNegotiatedToolRequest(UnityCliLoopConstants.COMMAND_NAME_GET_VERSION, 1),
                CancellationToken.None,
                (earlyResponse, _, heartbeatFactory) =>
                {
                    acceptedResponse = earlyResponse;
                    createHeartbeatJson = heartbeatFactory;
                    return Task.CompletedTask;
                });
            JObject parsed = JObject.Parse(response);
            JObject accepted = JObject.Parse(acceptedResponse);
            JObject heartbeat = JObject.Parse(createHeartbeatJson());

            Assert.That(parsed["error"], Is.Null);
            Assert.That(accepted["uloop"]?["phase"]?.ToString(), Is.EqualTo(JsonRpcResponsePhases.Accepted));
            Assert.That(
                accepted["uloop"]?["heartbeatIntervalSeconds"]?.ToObject<int>(),
                Is.EqualTo(UnityCliLoopServerConfig.HEARTBEAT_INTERVAL_SECONDS));
            Assert.That(heartbeat["uloop"]?["phase"]?.ToString(), Is.EqualTo(JsonRpcResponsePhases.Heartbeat));
            Assert.That(heartbeat["uloop"]?["mainThreadStallSeconds"]?.Type, Is.EqualTo(JTokenType.Float));
        }

        [Test]
        public async Task ProcessRequest_WhenCliMetadataIsMissing_ReturnsCliUpdateRequiredError()
        {
            // Verifies legacy clients without metadata are stopped with upgrade instructions.
            string response = await JsonRpcProcessor.ProcessRequest(
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1}",
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentProtocolVersion"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["currentCliVersion"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["updateCommand"]?.ToString(), Is.EqualTo(ExpectedCliUpdateCommand()));
        }

        [Test]
        public async Task ProcessRequest_WhenClientSendsOnlySemverVersion_ReturnsCliUpdateRequiredError()
        {
            // Verifies CLIs released before the protocol handshake are treated as outdated.
            string response = await JsonRpcProcessor.ProcessRequest(
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1," +
                "\"uloop\":{\"cliVersion\":\"3.0.0-beta.24\"}}",
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentProtocolVersion"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["currentCliVersion"]?.ToString(), Is.EqualTo("3.0.0-beta.24"));
            Assert.That(data["updateCommand"]?.ToString(), Is.EqualTo(ExpectedCliUpdateCommand()));
        }

        [Test]
        public async Task ProcessRequest_WhenProtocolVersionIsNotAnInteger_ReturnsCliUpdateRequiredError()
        {
            // Verifies malformed protocol values cannot bypass the compatibility gate.
            string response = await JsonRpcProcessor.ProcessRequest(
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1," +
                "\"uloop\":{\"protocolVersion\":\"not-a-number\"}}",
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentProtocolVersion"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["updateCommand"]?.ToString(), Is.EqualTo(ExpectedCliUpdateCommand()));
        }

        [Test]
        public async Task ProcessRequest_WhenProtocolVersionIsOutsideIntRange_ReturnsCliUpdateRequiredError()
        {
            // Verifies oversized protocol values are treated as missing metadata instead of parser failures.
            string response = await JsonRpcProcessor.ProcessRequest(
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1," +
                "\"uloop\":{\"protocolVersion\":2147483648}}",
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentProtocolVersion"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["updateCommand"]?.ToString(), Is.EqualTo(ExpectedCliUpdateCommand()));
        }

        [Test]
        public async Task ProcessRequest_WhenFirstToolWaitsForMainThread_ReturnsServerBusyForSecondTool()
        {
            // Verifies the single-flight gate is checked before queuing on Unity's main-thread dispatcher.
            CapturingMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);
            UnityCliLoopEditorStateSnapshot.SetPlayStateForTesting(isPlaying: false, isPaused: false);

            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new SingleFlightTestTool());

            Task<string> firstResponseTask = null;
            Task<string> secondResponseTask = null;
            try
            {
                firstResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(SingleFlightTestTool.Name, 1),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));

                secondResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(SingleFlightTestTool.Name, 2),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                string secondResponse = await AwaitWithTimeout(secondResponseTask, TimeSpan.FromMilliseconds(200));
                JObject error = ParseError(secondResponse);
                JObject data = ParseErrorData(secondResponse);

                Assert.That(error["message"]?.ToString(), Does.Contain(SingleFlightTestTool.Name));
                Assert.That(data["type"]?.ToString(), Is.EqualTo("server_busy"));
                Assert.That(data["runningToolName"]?.ToString(), Is.EqualTo(SingleFlightTestTool.Name));
                Assert.That(data["requestedToolName"]?.ToString(), Is.EqualTo(SingleFlightTestTool.Name));
                Assert.That(data["isPlaying"]?.ToObject<bool>(), Is.False);
                Assert.That(data["isPaused"]?.ToObject<bool>(), Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(firstResponseTask);
                await DrainTaskIfNeeded(secondResponseTask);
                UnityCliLoopEditorStateSnapshot.ClearForTesting();
                UnityCliLoopToolRegistrar.RegisterService(previousService);
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ProcessRequest_WhenExecuteDynamicCodeWaitsForMainThread_AllowsSecondExecuteDynamicCode()
        {
            // Verifies dynamic-code handoff stays inside the dynamic-code scheduler while other tools stay single-flight.
            CapturingMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);
            UnityCliLoopEditorStateSnapshot.SetPlayStateForTesting(isPlaying: false, isPaused: false);

            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new ExecuteDynamicCodeTestTool());
            service.RegisterCustomTool(new SingleFlightTestTool());

            Task<string> firstDynamicCodeTask = null;
            Task<string> secondDynamicCodeTask = null;
            Task<string> otherToolTask = null;
            try
            {
                firstDynamicCodeTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, 1),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));

                secondDynamicCodeTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, 2),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(2));
                Assert.That(secondDynamicCodeTask.IsCompleted, Is.False);

                otherToolTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(SingleFlightTestTool.Name, 3),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                string otherToolResponse = await AwaitWithTimeout(otherToolTask, TimeSpan.FromMilliseconds(200));
                JObject error = ParseError(otherToolResponse);
                JObject data = ParseErrorData(otherToolResponse);

                Assert.That(error["message"]?.ToString(), Does.Contain(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
                Assert.That(error["message"]?.ToString(), Does.Contain(SingleFlightTestTool.Name));
                Assert.That(data["type"]?.ToString(), Is.EqualTo("server_busy"));
                Assert.That(data["runningToolName"]?.ToString(), Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
                Assert.That(data["requestedToolName"]?.ToString(), Is.EqualTo(SingleFlightTestTool.Name));
                Assert.That(data["isPlaying"]?.ToObject<bool>(), Is.False);
                Assert.That(data["isPaused"]?.ToObject<bool>(), Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(firstDynamicCodeTask);
                await DrainTaskIfNeeded(secondDynamicCodeTask);
                await DrainTaskIfNeeded(otherToolTask);
                UnityCliLoopEditorStateSnapshot.ClearForTesting();
                UnityCliLoopToolRegistrar.RegisterService(previousService);
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ProcessRequest_WhenCompileWaitsForDomainReload_KeepsAcceptedRequestAliveAfterDisconnect()
        {
            // Verifies long compile waits are allowed to persist their result after the CLI response deadline closes.
            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new CompileDispatchPolicyTestTool());

            bool cancelOnClientDisconnect = true;
            try
            {
                string response = await JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequestWithParams(
                        UnityCliLoopConstants.TOOL_NAME_COMPILE,
                        "{\"WaitForDomainReload\":true}",
                        1),
                    CancellationToken.None,
                    (_, shouldCancelOnClientDisconnect, _) =>
                    {
                        cancelOnClientDisconnect = shouldCancelOnClientDisconnect;
                        return Task.CompletedTask;
                    });
                JObject parsed = JObject.Parse(response);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
                Assert.That(cancelOnClientDisconnect, Is.False);
            }
            finally
            {
                UnityCliLoopToolRegistrar.RegisterService(previousService);
            }
        }

        [Test]
        public async Task ProcessRequest_WhenCompileOmitsReloadWait_KeepsAcceptedRequestAliveAfterDisconnect()
        {
            // Verifies missing compile reload-wait params preserve the default wait contract.
            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new CompileDispatchPolicyTestTool());

            bool cancelOnClientDisconnect = true;
            try
            {
                string response = await JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(UnityCliLoopConstants.TOOL_NAME_COMPILE, 1),
                    CancellationToken.None,
                    (_, shouldCancelOnClientDisconnect, _) =>
                    {
                        cancelOnClientDisconnect = shouldCancelOnClientDisconnect;
                        return Task.CompletedTask;
                    });
                JObject parsed = JObject.Parse(response);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
                Assert.That(cancelOnClientDisconnect, Is.False);
            }
            finally
            {
                UnityCliLoopToolRegistrar.RegisterService(previousService);
            }
        }

        [Test]
        public async Task ProcessRequest_WhenCompileDoesNotWaitForDomainReload_CancelsOnClientDisconnect()
        {
            // Verifies fire-and-forget compile requests still cancel when the CLI connection goes away.
            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new CompileDispatchPolicyTestTool());

            bool cancelOnClientDisconnect = false;
            try
            {
                string response = await JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequestWithParams(
                        UnityCliLoopConstants.TOOL_NAME_COMPILE,
                        "{\"WaitForDomainReload\":false}",
                        1),
                    CancellationToken.None,
                    (_, shouldCancelOnClientDisconnect, _) =>
                    {
                        cancelOnClientDisconnect = shouldCancelOnClientDisconnect;
                        return Task.CompletedTask;
                    });
                JObject parsed = JObject.Parse(response);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
                Assert.That(cancelOnClientDisconnect, Is.True);
            }
            finally
            {
                UnityCliLoopToolRegistrar.RegisterService(previousService);
            }
        }

        [Test]
        public async Task ProcessRequest_WhenCompileUsesCamelCaseNoReloadWait_CancelsOnClientDisconnect()
        {
            // Verifies JSON-RPC compile dispatch policy matches the camelCase tool deserializer contract.
            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new CompileDispatchPolicyTestTool());

            bool cancelOnClientDisconnect = false;
            try
            {
                string response = await JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequestWithParams(
                        UnityCliLoopConstants.TOOL_NAME_COMPILE,
                        "{\"waitForDomainReload\":false}",
                        1),
                    CancellationToken.None,
                    (_, shouldCancelOnClientDisconnect, _) =>
                    {
                        cancelOnClientDisconnect = shouldCancelOnClientDisconnect;
                        return Task.CompletedTask;
                    });
                JObject parsed = JObject.Parse(response);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
                Assert.That(cancelOnClientDisconnect, Is.True);
            }
            finally
            {
                UnityCliLoopToolRegistrar.RegisterService(previousService);
            }
        }

        [Test]
        public async Task ProcessRequest_AfterGetHierarchyReturns_AllowsImmediateGetLogs()
        {
            // Verifies a completed get-hierarchy response releases the single-flight gate before the next tool request.
            CapturingMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);

            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);

            string hierarchyResponse = null;
            Task<string> hierarchyResponseTask = null;
            Task<string> logsResponseTask = null;
            try
            {
                hierarchyResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequestWithParams(
                        UnityCliLoopConstants.TOOL_NAME_GET_HIERARCHY,
                        "{\"MaxDepth\":0,\"IncludeComponents\":false}",
                        1),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));
                dispatcher.RunContinuations();
                hierarchyResponse = await AwaitWithTimeout(hierarchyResponseTask, TimeSpan.FromSeconds(1));
                JObject parsedHierarchy = JObject.Parse(hierarchyResponse);

                Assert.That(parsedHierarchy["error"], Is.Null);
                Assert.That(parsedHierarchy["result"]?["hierarchyFilePath"]?.ToString(), Is.Not.Empty);

                logsResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequestWithParams(
                        UnityCliLoopConstants.TOOL_NAME_GET_LOGS,
                        "{\"MaxCount\":0}",
                        2),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));
                dispatcher.RunContinuations();
                string logsResponse = await AwaitWithTimeout(logsResponseTask, TimeSpan.FromSeconds(1));
                JObject parsedLogs = JObject.Parse(logsResponse);

                Assert.That(parsedLogs["error"], Is.Null);
                Assert.That(parsedLogs["result"]?["DisplayedCount"]?.ToObject<int>(), Is.EqualTo(0));
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(hierarchyResponseTask);
                await DrainTaskIfNeeded(logsResponseTask);
                DeleteHierarchyFileFromResponse(hierarchyResponse);
                UnityCliLoopToolRegistrar.RegisterService(previousService);
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ProcessRequest_WhenInternalBridgeCommandRuns_SwitchesToMainThread()
        {
            // Verifies CLI-only bridge commands keep Unity API access on the editor thread.
            CapturingMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);

            Task<string> responseTask = null;
            try
            {
                responseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(UnityCliLoopConstants.COMMAND_NAME_GET_VERSION, 1),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));
                Assert.That(responseTask.IsCompleted, Is.False);

                dispatcher.RunContinuations();
                string response = await AwaitWithTimeout(responseTask, TimeSpan.FromMilliseconds(200));
                JObject parsed = JObject.Parse(response);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(responseTask);
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ProcessRequest_WhenMainThreadSwitchIsCanceled_ReleasesExecutionGateWithoutError()
        {
            // Verifies client disconnects stop waiting requests before Unity pumps delayed editor continuations.
            CapturingMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);

            UnityCliLoopToolRegistrarService previousService = UnityCliLoopToolRegistrar.Service;
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            UnityCliLoopToolRegistrar.RegisterService(service);
            service.RegisterCustomTool(new SingleFlightTestTool());

            using CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Task<string> canceledResponseTask = null;
            Task<string> secondResponseTask = null;
            try
            {
                canceledResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(SingleFlightTestTool.Name, 1),
                    cancellationSource.Token,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));

                cancellationSource.Cancel();
                await AwaitCancellationWithTimeout(canceledResponseTask, TimeSpan.FromMilliseconds(200));

                secondResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildToolRequest(SingleFlightTestTool.Name, 2),
                    CancellationToken.None,
                    (_, _, _) => Task.CompletedTask);

                Assert.That(secondResponseTask.IsCompleted, Is.False);

                dispatcher.RunContinuations();
                string secondResponse = await AwaitWithTimeout(secondResponseTask, TimeSpan.FromMilliseconds(200));
                JObject parsed = JObject.Parse(secondResponse);

                Assert.That(parsed["error"], Is.Null);
                Assert.That(parsed["result"], Is.Not.Null);
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(canceledResponseTask);
                await DrainTaskIfNeeded(secondResponseTask);
                UnityCliLoopToolRegistrar.RegisterService(previousService);
                RestoreEditorMainThreadDispatcher();
            }
        }

        private static string BuildGetVersionRequest(int protocolVersion)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1,\"uloop\":{\"protocolVersion\":" +
                protocolVersion +
                "}}";
        }

        private static string ExpectedCliUpdateCommand()
        {
            return "uloop update";
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
                ",\"acceptsDispatchAck\":true}}";
        }

        private static string BuildToolRequestWithParams(string toolName, string paramsJson, int id)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"" +
                toolName +
                "\",\"params\":" +
                paramsJson +
                ",\"id\":" +
                id +
                ",\"uloop\":{\"protocolVersion\":" +
                CliConstants.REQUIRED_CLI_PROTOCOL_VERSION +
                ",\"acceptsDispatchAck\":true}}";
        }

        private static string BuildHeartbeatNegotiatedToolRequest(string toolName, int id)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"" +
                toolName +
                "\",\"params\":{},\"id\":" +
                id +
                ",\"uloop\":{\"protocolVersion\":" +
                CliConstants.REQUIRED_CLI_PROTOCOL_VERSION +
                ",\"acceptsDispatchAck\":true,\"acceptsHeartbeat\":true}}";
        }

        private static JObject ParseErrorData(string response)
        {
            JObject error = ParseError(response);
            JObject data = error["data"] as JObject;
            Assert.That(data, Is.Not.Null);
            return data;
        }

        private static JObject ParseError(string response)
        {
            JObject parsed = JObject.Parse(response);
            JObject error = parsed["error"] as JObject;
            Assert.That(error, Is.Not.Null);
            return error;
        }

        private static async Task<string> AwaitWithTimeout(Task<string> task, TimeSpan timeout)
        {
            Task timeoutTask = Task.Delay(timeout);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            Assert.That(completedTask, Is.SameAs(task), $"Task did not complete within {timeout.TotalMilliseconds}ms.");
            return await task;
        }

        private static async Task AwaitCancellationWithTimeout(Task<string> task, TimeSpan timeout)
        {
            Task timeoutTask = Task.Delay(timeout);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            Assert.That(completedTask, Is.SameAs(task), $"Task did not cancel within {timeout.TotalMilliseconds}ms.");
            Assert.That(task.IsCanceled, Is.True, "Request cancellation should bubble out without becoming a JSON-RPC error.");
        }

        private static async Task DrainTaskIfNeeded(Task<string> task)
        {
            if (task == null || task.IsCompleted)
            {
                return;
            }

            await AwaitWithTimeout(task, TimeSpan.FromSeconds(1));
        }

        private static void DeleteHierarchyFileFromResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return;
            }

            JObject parsed = JObject.Parse(response);
            string relativePath = parsed["result"]?["hierarchyFilePath"]?.ToString();
            if (string.IsNullOrEmpty(relativePath))
            {
                return;
            }

            string projectRoot = Path.GetFullPath(UnityCliLoopPathResolver.GetProjectRoot());
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
            string projectRootPrefix = projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;
            if (!string.Equals(absolutePath, projectRoot, StringComparison.Ordinal)
                && !absolutePath.StartsWith(projectRootPrefix, StringComparison.Ordinal))
            {
                return;
            }

            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }

        private static void RestoreEditorMainThreadDispatcher()
        {
            EditorMainThreadDispatcher dispatcher = new();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }

        private sealed class CapturingMainThreadDispatcher : IMainThreadDispatcher
        {
            private readonly Queue<Action> _continuations = new();

            public bool IsMainThread => false;

            public int PendingContinuationCount => _continuations.Count;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
                _continuations.Enqueue(continuation);
            }

            public void RunContinuations()
            {
                while (_continuations.Count > 0)
                {
                    Action continuation = _continuations.Dequeue();
                    continuation();
                }
            }
        }

        private sealed class SingleFlightTestTool : IUnityCliLoopTool
        {
            public const string Name = "single-flight-test";

            public string ToolName => Name;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new SingleFlightTestResponse());
            }
        }

        private sealed class ExecuteDynamicCodeTestTool : IUnityCliLoopTool
        {
            public string ToolName => UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new SingleFlightTestResponse());
            }
        }

        private sealed class CompileDispatchPolicyTestTool : IUnityCliLoopTool
        {
            public string ToolName => UnityCliLoopConstants.TOOL_NAME_COMPILE;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new SingleFlightTestResponse());
            }
        }

        private sealed class SingleFlightTestResponse : UnityCliLoopToolResponse
        {
            public bool Success { get; set; } = true;
        }
    }
}
