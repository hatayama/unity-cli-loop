using System;
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
    public sealed class UnityCliLoopToolRegistrarService
    {
        private readonly IInternalToolNameProvider _internalToolNameProvider;
        private readonly UnityCliLoopToolExecutionService _toolExecutionService;
        private readonly ToolSettingsService _toolSettingsService;
        private UnityCliLoopToolRegistry _sharedRegistry;

        internal event Action OnToolsChanged;

        internal UnityCliLoopToolRegistrarService(
            IInternalToolNameProvider internalToolNameProvider,
            ToolSettingsService toolSettingsService,
            UnityCliLoopToolExecutionService toolExecutionService)
        {
            UnityEngine.Debug.Assert(internalToolNameProvider != null, "internalToolNameProvider must not be null");
            UnityEngine.Debug.Assert(toolSettingsService != null, "toolSettingsService must not be null");
            UnityEngine.Debug.Assert(toolExecutionService != null, "toolExecutionService must not be null");

            _internalToolNameProvider = internalToolNameProvider ?? throw new ArgumentNullException(nameof(internalToolNameProvider));
            _toolSettingsService = toolSettingsService ?? throw new ArgumentNullException(nameof(toolSettingsService));
            _toolExecutionService = toolExecutionService ?? throw new ArgumentNullException(nameof(toolExecutionService));
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
                    _sharedRegistry = new UnityCliLoopToolRegistry(
                        _toolSettingsService,
                        _internalToolNameProvider);
                    // Standard tools are automatically registered in UnityCliLoopToolRegistry constructor
                }
                return _sharedRegistry;
            }
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

        public static UnityCliLoopToolRegistry GetRegistry()
        {
            return Service.GetRegistry();
        }

        public static Task<UnityCliLoopToolResponse> ExecuteToolAsync(string toolName, JToken paramsToken, CancellationToken ct)
        {
            return Service.ExecuteToolAsync(toolName, paramsToken, ct);
        }

        public static void WarmupRegistry()
        {
            Service.WarmupRegistry();
        }
    }
}
