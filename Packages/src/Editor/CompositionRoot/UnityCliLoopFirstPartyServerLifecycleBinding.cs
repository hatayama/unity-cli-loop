using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Resets bundled tool lifecycle state and proves execute-dynamic-code readiness before the server is published as ready.
    /// </summary>
    internal sealed class UnityCliLoopFirstPartyServerLifecycleBinding : IUnityCliLoopServerReadinessProbe
    {
        private readonly ProjectIpcWarmupClient _projectIpcWarmupClient;

        internal UnityCliLoopFirstPartyServerLifecycleBinding(ProjectIpcWarmupClient projectIpcWarmupClient)
        {
            System.Diagnostics.Debug.Assert(projectIpcWarmupClient != null, "projectIpcWarmupClient must not be null");

            _projectIpcWarmupClient = projectIpcWarmupClient
                ?? throw new System.ArgumentNullException(nameof(projectIpcWarmupClient));
        }

        public Task ProbeAsync(CancellationToken ct)
        {
            return ResetServerScopedServicesAndWarmProjectIpcAsync(ct);
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
