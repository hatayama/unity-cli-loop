using System;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Runtime policy gate for tool execution.
    /// The checker reads tool metadata from the registry so callers do not need to know where each tool is implemented.
    /// </summary>
    public static class UnityCliLoopSecurityChecker
    {
        public static bool IsToolAllowed(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            ToolAttributeInfo? toolInfo = GetToolSecurityInfoFromRegistry(toolName);
            
            if (!toolInfo.HasValue)
            {
                return false;
            }

            return IsToolAllowedByAttribute(toolInfo.Value);
        }

        private static ToolAttributeInfo? GetToolSecurityInfoFromRegistry(string toolName)
        {
            UnityCliLoopToolRegistry registry = UnityCliLoopToolRegistrar.GetRegistry();
            if (registry == null)
            {
                return null;
            }

            Type toolType = registry.GetToolType(toolName);
            if (toolType == null)
            {
                return null;
            }

            UnityCliLoopToolAttribute attribute = toolType.GetCustomAttribute<UnityCliLoopToolAttribute>();
            if (attribute == null)
            {
                return new ToolAttributeInfo(toolName, UnityCliLoopSecuritySetting.None);
            }

            return new ToolAttributeInfo(toolName, attribute.RequiredSecuritySetting);
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

        public static bool ValidateTool(string toolName, bool throwOnBlock = true)
        {
            if (IsToolAllowed(toolName))
            {
                return true;
            }

            string reason = GetBlockReason(toolName);

            if (throwOnBlock)
            {
                throw new UnityCliLoopSecurityException(toolName, reason);
            }

            return false;
        }

        public static string GetBlockReason(string toolName)
        {
            ToolAttributeInfo? toolInfo = GetToolSecurityInfoFromRegistry(toolName);
            if (!toolInfo.HasValue)
            {
                return $"Tool '{toolName}' is not allowed by security policy.";
            }

            return $"Tool '{toolName}' is not allowed by security policy.";
        }

        public static ToolSecurityInfo GetToolSecurityInfo(string toolName)
        {
            bool isAllowed = IsToolAllowed(toolName);
            string reason = isAllowed ? "Tool is allowed" : GetBlockReason(toolName);
            
            return new ToolSecurityInfo(toolName, isAllowed, reason);
        }

    }

    /// <summary>
    /// DTO returned to callers that need to display or serialize the result of the security policy check.
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
    /// Internal value object that keeps registry metadata separate from presentation-facing security information.
    /// </summary>
    internal readonly struct ToolAttributeInfo
    {
        public readonly string ToolName;
        public readonly UnityCliLoopSecuritySetting RequiredSecuritySetting;

        public ToolAttributeInfo(string toolName, UnityCliLoopSecuritySetting requiredSecuritySetting)
        {
            ToolName = toolName;
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
