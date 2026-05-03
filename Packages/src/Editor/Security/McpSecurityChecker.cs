using System;
using System.Reflection;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Security checker for Unity CLI tools - provides runtime tool blocking based on user settings
    /// 
    /// Design document reference: Packages/src/Editor/Security/SECURITY.md
    /// 
    /// Related classes:
    /// - ULoopSettings: Persistent security settings storage (.uloop/settings.permissions.json)
    /// - McpSecurityException: Custom exception for security violations
    /// - UnityToolRegistry: Tool registration and execution
    /// - McpBridgeServer: IPC bridge that executes tools
    /// </summary>
    public static class McpSecurityChecker
    {
        /// <summary>
        /// Checks if a tool is allowed based on current security settings
        /// </summary>
        /// <param name="toolName">The name of the tool to check</param>
        /// <returns>True if tool is allowed, false otherwise</returns>
        public static bool IsToolAllowed(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            // Get tool info from registry
            var toolInfo = GetToolSecurityInfoFromRegistry(toolName);
            
            if (!toolInfo.HasValue)
            {
                // Unknown tool - default to block for security
                return false;
            }

            return IsToolAllowedByAttribute(toolInfo.Value);
        }

        /// <summary>
        /// Checks if a command is allowed based on current security settings
        /// </summary>
        /// <param name="commandName">The name of the command to check</param>
        /// <returns>True if command is allowed, false otherwise</returns>
        [System.Obsolete("Use IsToolAllowed instead. This method will be removed in a future version.")]
        public static bool IsCommandAllowed(string commandName)
        {
            return IsToolAllowed(commandName);
        }

        /// <summary>
        /// Gets tool security info from the tool registry
        /// </summary>
        /// <param name="toolName">The name of the tool</param>
        /// <returns>Tool security info or null if not found</returns>
        private static ToolAttributeInfo? GetToolSecurityInfoFromRegistry(string toolName)
        {
            // Get the tool registry instance
            var registry = CustomToolManager.GetRegistry();
            if (registry == null)
            {
                return null;
            }

            // Find the tool and get its attribute
            var toolType = registry.GetToolType(toolName);
            if (toolType == null)
            {
                return null;
            }

            var attribute = toolType.GetCustomAttribute<McpToolAttribute>();
            if (attribute == null)
            {
                return new ToolAttributeInfo(toolName, SecuritySettings.None);
            }

            return new ToolAttributeInfo(toolName, attribute.RequiredSecuritySetting);
        }

        /// <summary>
        /// Checks if tool is allowed based on its attribute settings
        /// </summary>
        /// <param name="toolInfo">Tool attribute information</param>
        /// <returns>True if tool is allowed</returns>
        private static bool IsToolAllowedByAttribute(ToolAttributeInfo toolInfo)
        {
            switch (toolInfo.RequiredSecuritySetting)
            {
                case SecuritySettings.None:
                    return true;
                default:
                    return false; // Unknown setting - block by default
            }
        }

        /// <summary>
        /// Validates tool execution and throws exception if blocked
        /// </summary>
        /// <param name="toolName">The name of the tool to validate</param>
        /// <param name="throwOnBlock">If true, throws exception when blocked. If false, returns validation result.</param>
        /// <returns>True if tool is allowed</returns>
        /// <exception cref="McpSecurityException">Thrown when tool is blocked and throwOnBlock is true</exception>
        public static bool ValidateTool(string toolName, bool throwOnBlock = true)
        {
            if (IsToolAllowed(toolName))
            {
                return true;
            }

            string reason = GetBlockReason(toolName);

            if (throwOnBlock)
            {
                throw new McpSecurityException(toolName, reason);
            }

            return false;
        }

        /// <summary>
        /// Validates command execution and throws exception if blocked
        /// </summary>
        /// <param name="commandName">The name of the command to validate</param>
        /// <param name="throwOnBlock">If true, throws exception when blocked. If false, returns validation result.</param>
        /// <returns>True if command is allowed</returns>
        /// <exception cref="McpSecurityException">Thrown when command is blocked and throwOnBlock is true</exception>
        [System.Obsolete("Use ValidateTool instead. This method will be removed in a future version.")]
        public static bool ValidateCommand(string commandName, bool throwOnBlock = true)
        {
            return ValidateTool(commandName, throwOnBlock);
        }

        /// <summary>
        /// Gets the user-friendly reason why a tool is blocked
        /// </summary>
        /// <param name="toolName">The name of the tool</param>
        /// <returns>Human-readable reason string</returns>
        public static string GetBlockReason(string toolName)
        {
            var toolInfo = GetToolSecurityInfoFromRegistry(toolName);
            if (!toolInfo.HasValue)
            {
                return $"Tool '{toolName}' is not allowed by security policy.";
            }

            return $"Tool '{toolName}' is not allowed by security policy.";
        }

        /// <summary>
        /// Gets security information for a tool
        /// </summary>
        /// <param name="toolName">The name of the tool</param>
        /// <returns>Security information object</returns>
        public static ToolSecurityInfo GetToolSecurityInfo(string toolName)
        {
            bool isAllowed = IsToolAllowed(toolName);
            string reason = isAllowed ? "Tool is allowed" : GetBlockReason(toolName);
            
            return new ToolSecurityInfo(toolName, isAllowed, reason);
        }

    }

    /// <summary>
    /// Security information for a tool
    /// </summary>
    public readonly struct ToolSecurityInfo
    {
        public readonly string ToolName;
        public readonly bool IsAllowed;
        public readonly string Reason;

        public ToolSecurityInfo(string toolName, bool isAllowed, string reason)
        {
            ToolName = toolName;
            IsAllowed = isAllowed;
            Reason = reason;
        }
    }

    /// <summary>
    /// Tool attribute information for security checking
    /// </summary>
    internal readonly struct ToolAttributeInfo
    {
        public readonly string ToolName;
        public readonly SecuritySettings RequiredSecuritySetting;

        public ToolAttributeInfo(string toolName, SecuritySettings requiredSecuritySetting)
        {
            ToolName = toolName;
            RequiredSecuritySetting = requiredSecuritySetting;
        }
    }

    /// <summary>
    /// Exception thrown when a tool is blocked by security settings
    /// </summary>
    public class McpSecurityException : Exception
    {
        public string ToolName { get; }
        public string SecurityReason { get; }
        
        /// <summary>
        /// Backwards compatibility property for CommandName
        /// </summary>
        [System.Obsolete("Use ToolName instead. This property will be removed in a future version.")]
        public string CommandName => ToolName;

        public McpSecurityException(string toolName, string reason)
            : base($"Tool '{toolName}' is blocked by security settings: {reason}")
        {
            ToolName = toolName;
            SecurityReason = reason;
        }

        public McpSecurityException(string toolName, string reason, Exception innerException)
            : base($"Tool '{toolName}' is blocked by security settings: {reason}", innerException)
        {
            ToolName = toolName;
            SecurityReason = reason;
        }
    }
}
