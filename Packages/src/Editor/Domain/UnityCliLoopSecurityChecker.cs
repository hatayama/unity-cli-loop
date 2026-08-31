using System;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Runtime policy gate for tool execution.
    /// The checker reads tool metadata from the registry so callers do not need to know where each tool is implemented.
    /// </summary>
    internal static class UnityCliLoopSecurityChecker
    {
        internal static bool IsToolAllowed(UnityCliLoopToolRegistry registry, string toolName)
        {
            if (registry == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            ToolAttributeInfo? toolInfo = GetToolSecurityInfoFromRegistry(registry, toolName);
            
            if (!toolInfo.HasValue)
            {
                return false;
            }

            return IsToolAllowedByAttribute(toolInfo.Value);
        }

        private static ToolAttributeInfo? GetToolSecurityInfoFromRegistry(UnityCliLoopToolRegistry registry, string toolName)
        {
            Type toolType = registry.GetToolType(toolName);
            if (toolType == null)
            {
                return null;
            }

            UnityCliLoopToolAttribute attribute = toolType.GetCustomAttribute<UnityCliLoopToolAttribute>();
            if (attribute == null)
            {
                return new ToolAttributeInfo(UnityCliLoopSecuritySetting.None);
            }

            return new ToolAttributeInfo(attribute.RequiredSecuritySetting);
        }

        private static bool IsToolAllowedByAttribute(ToolAttributeInfo toolInfo)
        {
            switch (toolInfo.RequiredSecuritySetting)
            {
                case UnityCliLoopSecuritySetting.None:
                    return true;
                default:
                    return false;
            }
        }

    }

    /// <summary>
    /// Internal value object that keeps registry metadata separate from presentation-facing security information.
    /// </summary>
    internal readonly struct ToolAttributeInfo
    {
        public readonly UnityCliLoopSecuritySetting RequiredSecuritySetting;

        public ToolAttributeInfo(UnityCliLoopSecuritySetting requiredSecuritySetting)
        {
            RequiredSecuritySetting = requiredSecuritySetting;
        }
    }

    /// <summary>
    /// Exception raised when a caller tries to execute a tool blocked by the security policy.
    /// </summary>
    public class UnityCliLoopSecurityException : Exception
    {
        public string ToolName { get; }
        public string SecurityReason { get; }

        public UnityCliLoopSecurityException(string toolName, string reason)
            : base($"Tool '{toolName}' is blocked by security settings: {reason}")
        {
            ToolName = toolName;
            SecurityReason = reason;
        }
    }
}
