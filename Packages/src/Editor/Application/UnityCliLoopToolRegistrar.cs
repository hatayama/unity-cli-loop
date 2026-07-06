using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Manages the shared editor tool registry and custom tool registrations.
    /// </summary>
    public sealed class UnityCliLoopToolRegistrarService : IUnityCliLoopToolRegistrar
    {
        private readonly IInternalToolNameProvider _internalToolNameProvider;
        private readonly UnityCliLoopToolExecutionService _toolExecutionService;
        private readonly IToolSettingsPort _toolSettingsPort;
        private readonly Func<IReadOnlyList<IUnityCliLoopTool>> _toolDiscovery;
        private UnityCliLoopToolRegistry _sharedRegistry;

        internal event Action OnToolsChanged;

        internal UnityCliLoopToolRegistrarService(
            IInternalToolNameProvider internalToolNameProvider,
            IToolSettingsPort toolSettingsPort,
            UnityCliLoopToolExecutionService toolExecutionService,
            Func<IReadOnlyList<IUnityCliLoopTool>> toolDiscovery)
        {
            UnityEngine.Debug.Assert(internalToolNameProvider != null, "internalToolNameProvider must not be null");
            UnityEngine.Debug.Assert(toolSettingsPort != null, "toolSettingsPort must not be null");
            UnityEngine.Debug.Assert(toolExecutionService != null, "toolExecutionService must not be null");
            UnityEngine.Debug.Assert(toolDiscovery != null, "toolDiscovery must not be null");

            _internalToolNameProvider = internalToolNameProvider ?? throw new ArgumentNullException(nameof(internalToolNameProvider));
            _toolSettingsPort = toolSettingsPort ?? throw new ArgumentNullException(nameof(toolSettingsPort));
            _toolExecutionService = toolExecutionService ?? throw new ArgumentNullException(nameof(toolExecutionService));
            _toolDiscovery = toolDiscovery ?? throw new ArgumentNullException(nameof(toolDiscovery));
        }

        /// <summary>
        /// Get shared registry (lazy initialization). Tool discovery runs on first access
        /// so the reflection scan stays off the service construction path during editor startup.
        /// </summary>
        private UnityCliLoopToolRegistry SharedRegistry
        {
            get
            {
                if (_sharedRegistry == null)
                {
                    _sharedRegistry = new UnityCliLoopToolRegistry(
                        _toolSettingsPort,
                        _internalToolNameProvider,
                        _toolDiscovery);
                }
                return _sharedRegistry;
            }
        }

        public void RegisterCustomTool(IUnityCliLoopTool tool)
        {
            UnityEngine.Debug.Assert(tool != null, "tool must not be null");

            SharedRegistry.RegisterTool(tool);
            NotifyToolChanges();
        }

        public void UnregisterCustomTool(string toolName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            SharedRegistry.UnregisterTool(toolName);
            NotifyToolChanges();
        }

        public ToolInfo[] GetRegisteredCustomTools()
        {
            return SharedRegistry.GetRegisteredTools();
        }

        public bool IsCustomToolRegistered(string toolName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

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

        public Task<UnityCliLoopToolResponse> ExecuteToolAsync(string toolName, JToken paramsToken, CancellationToken ct)
        {
            return _toolExecutionService.ExecuteToolAsync(SharedRegistry, toolName, paramsToken, ct);
        }

        public void WarmupRegistry()
        {
            _ = SharedRegistry;
        }

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

        public static UnityCliLoopToolRegistry TryGetRegistry()
        {
            return Service.TryGetRegistry();
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
