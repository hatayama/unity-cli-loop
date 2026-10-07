using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies JSON-RPC notification handling in JsonRpcRequestProcessor without executing tools.
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

        private static JsonRpcRequestProcessor CreateProcessor()
        {
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AllToolsEnabledSettingsPort(),
                new UnityCliLoopToolExecutionService(new IdleEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
            return new JsonRpcRequestProcessor(new UnityCliLoopExecutionRouter(registrarService, new EditorExecutionActivity(new InertProcessActivityApi())));
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
