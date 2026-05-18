using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        public async Task ProcessRequest_WhenCliVersionSatisfiesMinimum_AllowsRequest()
        {
            // Verifies compatible CLI clients can execute bridge commands.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest(CliConstants.MINIMUM_REQUIRED_CLI_VERSION),
                CancellationToken.None);
            JObject parsed = JObject.Parse(response);

            Assert.That(parsed["error"], Is.Null);
            Assert.That(parsed["result"], Is.Not.Null);
        }

        [Test]
        public async Task ProcessRequest_WhenCliVersionIsTooOld_ReturnsCliUpdateRequiredError()
        {
            // Verifies old CLI clients receive an exact update command before any tool runs.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest("3.0.0-beta.5"),
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentCliVersion"]?.ToString(), Is.EqualTo("3.0.0-beta.5"));
            Assert.That(data["requiredCliVersion"]?.ToString(), Is.EqualTo(CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
            Assert.That(
                data["updateCommand"]?.ToString(),
                Is.EqualTo("uloop update"));
            Assert.That(
                data["targetUpdateCommand"]?.ToString(),
                Is.EqualTo($"uloop update --to-version {CliConstants.MINIMUM_REQUIRED_CLI_VERSION}"));
            Assert.That(data["retryableAfterUpdate"]?.ToObject<bool>(), Is.True);
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
            Assert.That(data["currentCliVersion"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public async Task ProcessRequest_WhenCliVersionIsInvalid_ReturnsCliUpdateRequiredError()
        {
            // Verifies malformed CLI versions cannot bypass the compatibility gate.
            string response = await JsonRpcProcessor.ProcessRequest(
                BuildGetVersionRequest("not-a-version"),
                CancellationToken.None);
            JObject data = ParseErrorData(response);

            Assert.That(data["type"]?.ToString(), Is.EqualTo("cli_update_required"));
            Assert.That(data["currentCliVersion"]?.ToString(), Is.EqualTo("not-a-version"));
        }

        [Test]
        public async Task ProcessRequest_WhenFirstToolWaitsForMainThread_ReturnsServerBusyForSecondTool()
        {
            // Verifies the single-flight gate is checked before queuing on Unity's main-thread dispatcher.
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

            Task<string> firstResponseTask = null;
            Task<string> secondResponseTask = null;
            try
            {
                firstResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildSingleFlightToolRequest(1),
                    CancellationToken.None,
                    _ => Task.CompletedTask);

                Assert.That(dispatcher.PendingContinuationCount, Is.EqualTo(1));

                LogAssert.Expect(
                    LogType.Error,
                    new Regex("\\[JsonRpcProcessor\\] Error: Unity tool execution is busy running 'single-flight-test'"));

                secondResponseTask = JsonRpcProcessor.ProcessRequestWithEarlyResponseAsync(
                    BuildSingleFlightToolRequest(2),
                    CancellationToken.None,
                    _ => Task.CompletedTask);

                string secondResponse = await AwaitWithTimeout(secondResponseTask, TimeSpan.FromMilliseconds(200));
                JObject data = ParseErrorData(secondResponse);

                Assert.That(data["type"]?.ToString(), Is.EqualTo("server_busy"));
                Assert.That(data["runningToolName"]?.ToString(), Is.EqualTo(SingleFlightTestTool.Name));
                Assert.That(data["requestedToolName"]?.ToString(), Is.EqualTo(SingleFlightTestTool.Name));
            }
            finally
            {
                dispatcher.RunContinuations();
                await DrainTaskIfNeeded(firstResponseTask);
                await DrainTaskIfNeeded(secondResponseTask);
                UnityCliLoopToolRegistrar.RegisterService(previousService);
                RestoreEditorMainThreadDispatcher();
            }
        }

        private static string BuildGetVersionRequest(string cliVersion)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"get-version\",\"params\":{},\"id\":1,\"uloop\":{\"cliVersion\":\"" +
                cliVersion +
                "\"}}";
        }

        private static string BuildSingleFlightToolRequest(int id)
        {
            return
                "{\"jsonrpc\":\"2.0\",\"method\":\"" +
                SingleFlightTestTool.Name +
                "\",\"params\":{},\"id\":" +
                id +
                ",\"uloop\":{\"cliVersion\":\"" +
                CliConstants.MINIMUM_REQUIRED_CLI_VERSION +
                "\",\"acceptsDispatchAck\":true}}";
        }

        private static JObject ParseErrorData(string response)
        {
            JObject parsed = JObject.Parse(response);
            JObject error = parsed["error"] as JObject;
            Assert.That(error, Is.Not.Null);
            JObject data = error["data"] as JObject;
            Assert.That(data, Is.Not.Null);
            return data;
        }

        private static async Task<string> AwaitWithTimeout(Task<string> task, TimeSpan timeout)
        {
            Task timeoutTask = Task.Delay(timeout);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            Assert.That(completedTask, Is.SameAs(task), $"Task did not complete within {timeout.TotalMilliseconds}ms.");
            return await task;
        }

        private static async Task DrainTaskIfNeeded(Task<string> task)
        {
            if (task == null || task.IsCompleted)
            {
                return;
            }

            await AwaitWithTimeout(task, TimeSpan.FromSeconds(1));
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

        private sealed class SingleFlightTestResponse : UnityCliLoopToolResponse
        {
            public bool Success { get; set; } = true;
        }
    }
}
