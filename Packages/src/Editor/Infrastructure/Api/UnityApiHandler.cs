using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{

    /// <summary>
    /// Class specialized in handling Unity API calls
    /// Supports new command-based structure
    /// 
    /// Design document reference: Packages/src/Editor/ARCHITECTURE.md
    /// 
    /// Related classes:
    /// - UnityCommandRegistry: Registry that manages all available Unity commands
    /// - CustomCommandManager: Provides access to the command registry singleton
    /// - JsonRpcProcessor: Receives JSON-RPC requests and delegates to this handler
    /// - IUnityCommand: Interface implemented by all command classes
    /// - AbstractUnityCommand: Base class for all Unity commands
    /// - BaseCommandResponse: Base response type for all commands
    /// - MainThreadSwitcher: Ensures command execution on Unity's main thread
    /// 
    /// Command execution flow:
    /// 1. JsonRpcProcessor receives request from the CLI client
    /// 2. Delegates to ExecuteCommand method with command name and parameters
    /// 3. Looks up command in registry and executes asynchronously
    /// 4. Returns command response or error information
    /// </summary>
    public static class UnityApiHandler
    {
        /// <summary>
        /// Get command registry
        /// Use this registry when adding new commands
        /// </summary>
        public static UnityCliLoopToolRegistry CommandRegistry => ApplicationRegistrar.GetRegistry();

        /// <summary>
        /// Generic command execution method
        /// Uses new command-based structure
        /// </summary>
        /// <param name="commandName">Command name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <returns>Execution result</returns>
        public static async Task<UnityCliLoopToolResponse> ExecuteCommandAsync(
            string commandName,
            JToken paramsToken,
            CancellationToken ct)
        {
            UnityCliLoopToolResponse response;
            if (InternalBridgeCommandRouter.IsInternalCommand(commandName))
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                response = InternalBridgeCommandRouter.Execute(commandName, paramsToken);
                return response;
            }

            response = await ApplicationRegistrar.ExecuteToolAsync(
                commandName,
                paramsToken,
                ct);
            return response;
        }
    }
}
