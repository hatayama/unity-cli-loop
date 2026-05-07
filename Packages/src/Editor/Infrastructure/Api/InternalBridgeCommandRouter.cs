using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

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
                   commandName == UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS;
        }

        public static UnityCliLoopToolResponse Execute(string commandName, JToken paramsToken)
        {
            Debug.Assert(IsInternalCommand(commandName), $"Unknown internal bridge command: {commandName}");

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_VERSION)
            {
                return GetVersionBridgeCommand.Execute();
            }

            if (commandName == UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS)
            {
                return GetToolDetailsBridgeCommand.Execute(paramsToken);
            }

            throw new ArgumentException($"Unknown internal bridge command: {commandName}", nameof(commandName));
        }
    }
}
