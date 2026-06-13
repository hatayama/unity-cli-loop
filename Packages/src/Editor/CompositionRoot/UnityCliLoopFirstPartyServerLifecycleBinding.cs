using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Resets bundled tool lifecycle state and proves get-version IPC readiness before the server is published as ready.
    /// </summary>
    internal sealed class UnityCliLoopFirstPartyServerLifecycleBinding :
        IUnityCliLoopServerReadinessProbe,
        IUnityCliLoopServerDomainReloadLifecycle
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

        public void PrepareForDomainReload()
        {
            FirstPartyToolsEditorStartup.ResetServerScopedServicesBeforeDomainReload();
        }

        private async Task ResetServerScopedServicesAndWarmProjectIpcAsync(CancellationToken ct)
        {
            FirstPartyToolsEditorStartup.ResetServerScopedServices();
            string requestJson = CreateGetVersionReadinessRequestJson();

            // Why: after server recovery, the next external CLI request otherwise pays the cold
            // project IPC and editor-thread wakeup cost. The composition root owns this transport
            // readiness work through an internal command so user-disabled tools do not block startup.
            await _projectIpcWarmupClient.SendProjectIpcRequestAsync(
                UnityEngine.Application.dataPath + "/..",
                requestJson,
                ct);
        }

        internal static string CreateGetVersionReadinessRequestJson()
        {
            JObject request = new()
            {
                ["jsonrpc"] = "2.0",
                ["method"] = UnityCliLoopConstants.COMMAND_NAME_GET_VERSION,
                ["id"] = 1,
                ["uloop"] = new JObject
                {
                    ["protocolVersion"] = CliConstants.REQUIRED_CLI_PROTOCOL_VERSION
                }
            };
            return request.ToString(Formatting.None);
        }
    }
}
