using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Resolves Tool Settings visibility and parent-toggle behavior for auxiliary tools.
    /// </summary>
    internal static class ToolSettingsToolLinkPolicy
    {
        private static readonly HashSet<string> PausePointAuxiliaryToolNames = new(System.StringComparer.Ordinal)
        {
            UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT,
            UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT,
            UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS
        };

        internal static bool IsUserFacingToolSettingsTool(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            return !PausePointAuxiliaryToolNames.Contains(toolName);
        }

        internal static bool IsToolEnabled(string toolName, IToolSettingsPort toolSettingsService)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            Debug.Assert(toolSettingsService != null, "toolSettingsService must not be null");

            return toolSettingsService.IsToolEnabled(GetSettingsToolName(toolName));
        }

        internal static string GetSettingsToolName(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            if (PausePointAuxiliaryToolNames.Contains(toolName))
            {
                return UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT;
            }

            return toolName;
        }
    }
}
