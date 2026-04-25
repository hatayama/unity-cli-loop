using System;
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Static class for managing custom tools
    /// Allows users to register and manage their own tools
    /// </summary>
    public static class CustomToolManager
    {
        private static UnityToolRegistry _sharedRegistry;

        internal static event Action OnToolsChanged;

        /// <summary>
        /// Get shared registry (lazy initialization)
        /// </summary>
        private static UnityToolRegistry SharedRegistry
        {
            get
            {
                if (_sharedRegistry == null)
                {
                    _sharedRegistry = new UnityToolRegistry();
                    // Standard tools are automatically registered in UnityToolRegistry constructor
                }
                return _sharedRegistry;
            }
        }

        /// <summary>
        /// Register custom tool
        /// </summary>
        /// <param name="tool">Tool to register</param>
        public static void RegisterCustomTool(IUnityTool tool)
        {
            SharedRegistry.RegisterTool(tool);
            
            // Notify tool changes for manual registration
            NotifyToolChanges();
        }

        /// <summary>
        /// Unregister custom tool
        /// </summary>
        /// <param name="toolName">Name of tool to unregister</param>
        public static void UnregisterCustomTool(string toolName)
        {
            SharedRegistry.UnregisterTool(toolName);
            
            // Notify tool changes for manual unregistration
            NotifyToolChanges();
        }

        /// <summary>
        /// Get list of all registered tools (standard + custom)
        /// </summary>
        /// <returns>Array of tool information</returns>
        public static ToolInfo[] GetRegisteredCustomTools()
        {
            return SharedRegistry.GetRegisteredTools();
        }

        /// <summary>
        /// Check if specified tool is registered
        /// </summary>
        /// <param name="toolName">Tool name</param>
        /// <returns>True if registered</returns>
        public static bool IsCustomToolRegistered(string toolName)
        {
            return SharedRegistry.IsToolRegistered(toolName);
        }

        /// <summary>
        /// Get internal registry (for MCP server)
        /// </summary>
        /// <returns>UnityToolRegistry instance</returns>
        internal static UnityToolRegistry GetRegistry()
        {
            return SharedRegistry;
        }

        internal static UnityToolRegistry TryGetRegistry()
        {
            return _sharedRegistry;
        }

        internal static void WarmupRegistry()
        {
            _ = SharedRegistry;
        }

        /// <summary>
        /// Debug: Get detailed registry information
        /// </summary>
        /// <returns>Debug information</returns>
        public static string GetDebugInfo()
        {
            ToolInfo[] tools = SharedRegistry.GetRegisteredTools();
            string[] toolNames = new string[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                toolNames[i] = tools[i].Name;
            }
            return $"Registry instance: {SharedRegistry.GetHashCode()}, Tools: [{string.Join(", ", toolNames)}]";
        }
        
        /// <summary>
        /// Manually notify tool changes to MCP clients
        /// Public API for users to trigger notifications when needed
        /// </summary>
        public static void NotifyToolChanges()
        {
            UnityToolRegistry.TriggerToolsChangedNotification();
            OnToolsChanged?.Invoke();
        }
    }
}
