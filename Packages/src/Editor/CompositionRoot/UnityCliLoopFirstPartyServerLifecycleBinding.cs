using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Binds platform server lifecycle notifications to bundled tool lifecycle hooks.
    /// </summary>
    internal sealed class UnityCliLoopFirstPartyServerLifecycleBinding
    {
        private readonly ProjectIpcWarmupClient _projectIpcWarmupClient = new();

        internal void Initialize()
        {
            UnityCliLoopServerApplicationFacade.AddServerStartedHandler(OnServerStarted);
        }

        private void OnServerStarted()
        {
            ResetServerScopedServicesAndWarmProjectIpcAsync(CancellationToken.None).Forget();
        }

        private async Task ResetServerScopedServicesAndWarmProjectIpcAsync(CancellationToken ct)
        {
            FirstPartyToolsEditorStartup.ResetServerScopedServices();
            string requestJson = CreateExecuteDynamicCodeReadinessRequestJson(
                FirstPartyToolsEditorStartup.CreateExecuteDynamicCodeReadinessProbeCode());

            // Why: after server recovery, the next external CLI request otherwise pays the cold
            // project IPC and editor-thread wakeup cost. The composition root owns this transport
            // readiness work so execute-dynamic-code stays focused on executing user code.
            await _projectIpcWarmupClient.SendProjectIpcRequestAsync(
                UnityEngine.Application.dataPath + "/..",
                requestJson,
                ct);
        }

        private static string CreateExecuteDynamicCodeReadinessRequestJson(string code)
        {
            JObject request = new()
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "execute-dynamic-code",
                ["id"] = 1,
                ["params"] = new JObject
                {
                    ["Code"] = code,
                    ["CompileOnly"] = false,
                    ["YieldToForegroundRequests"] = false
                }
            };
            return request.ToString(Formatting.None);
        }
    }
}
