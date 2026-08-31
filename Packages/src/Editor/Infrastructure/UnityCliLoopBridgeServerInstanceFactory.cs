using System;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Creates Unity CLI Loop Bridge Server Instance instances with the dependencies required by this module.
    /// </summary>
    public sealed class UnityCliLoopBridgeServerInstanceFactory :
        IUnityCliLoopServerInstanceFactory,
        IUnityCliLoopServerLifecycleSource
    {
        public event Action ServerLoopExited;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;

        internal UnityCliLoopBridgeServerInstanceFactory(
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            _domainReloadDetectionService = domainReloadDetectionService
                ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            _toolRegistrarService = toolRegistrarService
                ?? throw new ArgumentNullException(nameof(toolRegistrarService));
        }

        public IUnityCliLoopServerInstance Create()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();
            UnityCliLoopBridgeClientDisconnectMonitor clientDisconnectMonitor = new();
            UnityCliLoopExecutionRouter executionRouter = new(_toolRegistrarService);
            JsonRpcRequestProcessor jsonRpcRequestProcessor = new(executionRouter);
            UnityCliLoopBridgeServer server = new(
                _domainReloadDetectionService,
                jsonRpcRequestProcessor,
                heartbeatService,
                clientDisconnectMonitor);
            server.ServerLoopExited += NotifyServerLoopExited;

            return server;
        }

        private void NotifyServerLoopExited()
        {
            ServerLoopExited?.Invoke();
        }
    }
}
