using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Manages the shared editor tool registry and custom tool registrations.
    /// </summary>
    public sealed class UnityCliLoopToolRegistrarService
    {
        private readonly IInternalToolNameProvider _internalToolNameProvider;
        private UnityCliLoopToolRegistry _sharedRegistry;

        internal event Action OnToolsChanged;

        public UnityCliLoopToolRegistrarService(IInternalToolNameProvider internalToolNameProvider)
        {
            UnityEngine.Debug.Assert(internalToolNameProvider != null, "internalToolNameProvider must not be null");

            _internalToolNameProvider = internalToolNameProvider ?? throw new ArgumentNullException(nameof(internalToolNameProvider));
        }

        /// <summary>
        /// Get shared registry (lazy initialization)
        /// </summary>
        private UnityCliLoopToolRegistry SharedRegistry
        {
            get
            {
                if (_sharedRegistry == null)
                {
                    _sharedRegistry = new UnityCliLoopToolRegistry(_internalToolNameProvider);
                    // Standard tools are automatically registered in UnityCliLoopToolRegistry constructor
                }
                return _sharedRegistry;
            }
        }

        /// <summary>
        /// Register custom tool
        /// </summary>
        /// <param name="tool">Tool to register</param>
        public void RegisterCustomTool(IUnityCliLoopTool tool)
        {
            SharedRegistry.RegisterTool(tool);
            
            // Notify tool changes for manual registration
            NotifyToolChanges();
        }

        /// <summary>
        /// Unregister custom tool
        /// </summary>
        /// <param name="toolName">Name of tool to unregister</param>
        public void UnregisterCustomTool(string toolName)
        {
            SharedRegistry.UnregisterTool(toolName);
            
            // Notify tool changes for manual unregistration
            NotifyToolChanges();
        }

        /// <summary>
        /// Get list of all registered tools (standard + custom)
        /// </summary>
        /// <returns>Array of tool information</returns>
        public ToolInfo[] GetRegisteredCustomTools()
        {
            return SharedRegistry.GetRegisteredTools();
        }

        /// <summary>
        /// Check if specified tool is registered
        /// </summary>
        /// <param name="toolName">Tool name</param>
        /// <returns>True if registered</returns>
        public bool IsCustomToolRegistered(string toolName)
        {
            return SharedRegistry.IsToolRegistered(toolName);
        }

        /// <summary>
        /// Get internal registry for the Unity CLI bridge.
        /// </summary>
        /// <returns>UnityCliLoopToolRegistry instance</returns>
        public UnityCliLoopToolRegistry GetRegistry()
        {
            return SharedRegistry;
        }

        public UnityCliLoopToolRegistry TryGetRegistry()
        {
            return _sharedRegistry;
        }

        public void WarmupRegistry()
        {
            _ = SharedRegistry;
        }

        /// <summary>
        /// Debug: Get detailed registry information
        /// </summary>
        /// <returns>Debug information</returns>
        public string GetDebugInfo()
        {
            ToolInfo[] tools = SharedRegistry.GetRegisteredTools();
            string[] toolNames = new string[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                toolNames[i] = tools[i].Name;
            }
            return $"Registry instance: {SharedRegistry.GetHashCode()}, Tools: [{string.Join(", ", toolNames)}]";
        }
        
        public void NotifyToolChanges()
        {
            OnToolsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Compatibility entrypoint for callers that have not received UnityCliLoopToolRegistrarService through DI yet.
    /// </summary>
    public static class UnityCliLoopToolRegistrar
    {
        private static UnityCliLoopToolRegistrarService ServiceValue;

        internal static void RegisterService(UnityCliLoopToolRegistrarService service)
        {
            UnityEngine.Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        internal static void AddToolsChangedHandler(Action handler)
        {
            Service.OnToolsChanged += handler;
        }

        internal static void RemoveToolsChangedHandler(Action handler)
        {
            Service.OnToolsChanged -= handler;
        }

        public static UnityCliLoopToolRegistrarService Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop tool registrar service is not registered.");
                }

                return ServiceValue;
            }
        }

        public static void RegisterCustomTool(IUnityCliLoopTool tool)
        {
            Service.RegisterCustomTool(tool);
        }

        public static void UnregisterCustomTool(string toolName)
        {
            Service.UnregisterCustomTool(toolName);
        }

        public static ToolInfo[] GetRegisteredCustomTools()
        {
            return Service.GetRegisteredCustomTools();
        }

        public static bool IsCustomToolRegistered(string toolName)
        {
            return Service.IsCustomToolRegistered(toolName);
        }

        public static UnityCliLoopToolRegistry GetRegistry()
        {
            return Service.GetRegistry();
        }

        public static UnityCliLoopToolRegistry TryGetRegistry()
        {
            return Service.TryGetRegistry();
        }

        public static void WarmupRegistry()
        {
            Service.WarmupRegistry();
        }

        public static string GetDebugInfo()
        {
            return Service.GetDebugInfo();
        }

        public static void NotifyToolChanges()
        {
            Service.NotifyToolChanges();
        }
    }
}
