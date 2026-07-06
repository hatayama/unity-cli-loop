using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Routes CLI-only bridge commands that must not appear in the extension-facing tool registry.
    /// </summary>
    internal static class InternalBridgeCommandRouter
    {
        public static bool IsInternalCommand(string commandName)
        {
            return commandName == UnityCliLoopConstants.COMMAND_NAME_GET_VERSION ||
                   commandName == UnityCliLoopConstants.COMMAND_NAME_GET_COMPILE_STATUS ||
                   commandName == UnityCliLoopConstants.COMMAND_NAME_GET_PAUSE_POINT_STATUS ||
                   commandName == UnityCliLoopConstants.COMMAND_NAME_CLEAR_PAUSE_POINT_STATUS ||
                   commandName == UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS;
        }

        public static UnityCliLoopToolResponse Execute(
            string commandName,
            JToken paramsToken,
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            Debug.Assert(IsInternalCommand(commandName), $"Unknown internal bridge command: {commandName}");
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_VERSION)
            {
                return GetVersionBridgeCommand.Execute();
            }

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS)
            {
                return GetToolDetailsBridgeCommand.Execute(paramsToken, toolRegistrarService);
            }

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_COMPILE_STATUS)
            {
                return CompileStatusBridgeCommand.Execute(paramsToken);
            }

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_PAUSE_POINT_STATUS)
            {
                return PausePointStatusBridgeCommand.Execute(paramsToken);
            }

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_CLEAR_PAUSE_POINT_STATUS)
            {
                return PausePointStatusBridgeCommand.Clear(paramsToken);
            }

            throw new ArgumentException($"Unknown internal bridge command: {commandName}", nameof(commandName));
        }
    }
}
