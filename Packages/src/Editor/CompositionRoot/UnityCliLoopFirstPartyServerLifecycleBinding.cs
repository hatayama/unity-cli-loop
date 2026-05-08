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
        internal void Initialize()
        {
            UnityCliLoopServerApplicationFacade.AddServerStartedHandler(OnServerStarted);
        }

        private void OnServerStarted()
        {
            ResetServerScopedServicesAndWarmToolPathAsync(CancellationToken.None).Forget();
        }

        private static async Task ResetServerScopedServicesAndWarmToolPathAsync(CancellationToken ct)
        {
            FirstPartyToolsEditorStartup.ResetServerScopedServices();
            string[] warmupCodes = FirstPartyToolsEditorStartup.CreateExecuteDynamicCodeWarmupCodes();
            if (warmupCodes.Length == 0)
            {
                return;
            }

            string requestJson = CreateExecuteDynamicCodeWarmupRequestJson(warmupCodes[0]);
            await BridgeTransportWarmupClient.SendProjectIpcRequestAsync(
                UnityEngine.Application.dataPath + "/..",
                requestJson,
                ct);
        }

        private static string CreateExecuteDynamicCodeWarmupRequestJson(string code)
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
