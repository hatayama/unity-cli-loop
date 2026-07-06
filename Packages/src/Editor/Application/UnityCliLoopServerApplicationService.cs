using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Application-owned handle for the running server instance.
    /// Server internals implement this handle so application use cases do not expose transport classes.
    /// </summary>
    public interface IUnityCliLoopServerInstance : IDisposable
    {
        bool IsRunning { get; }

        string Endpoint { get; }

        void StartServer();

        void StopServer();
    }

    /// <summary>
    /// Defines how Unity CLI Loop server instances are created without exposing concrete construction.
    /// </summary>
    public interface IUnityCliLoopServerInstanceFactory
    {
        IUnityCliLoopServerInstance Create();
    }

    /// <summary>
    /// Defines the event source used to observe Unity CLI Loop server lifecycle behavior.
    /// Why only ServerLoopExited: it is the one lifecycle signal raised by the server itself
    /// (from the thread pool when the accept loop dies). ServerStarted/ServerStopping describe
    /// controller-level readiness and are published manually via the lifecycle registry.
    /// </summary>
    public interface IUnityCliLoopServerLifecycleSource
    {
        event Action ServerLoopExited;
    }

    /// <summary>
    /// Handles server-scoped cleanup that must happen before Unity tears down editor assemblies.
    /// </summary>
    public interface IUnityCliLoopServerDomainReloadLifecycle
    {
        void PrepareForDomainReload();
    }

    /// <summary>
    /// Defines the control operations needed for Unity CLI Loop Server behavior.
    /// </summary>
    public interface IUnityCliLoopServerController
    {
        bool IsServerRunning { get; }

        Task RecoveryTask { get; }

        void StartServer();

        void StopServer();

        void AddServerStateChangedHandler(Action handler);

        void RemoveServerStateChangedHandler(Action handler);

        void AddServerStartedHandler(Action handler);

        void RemoveServerStartedHandler(Action handler);
    }

    /// <summary>
    /// Provides Unity CLI Loop Server Lifecycle Registry operations for its owning module.
    /// ServerStarted/ServerStopping are delivered only through the Publish* methods;
    /// ServerLoopExited is delivered only by the registered lifecycle source.
    /// </summary>
    public sealed class UnityCliLoopServerLifecycleRegistryService
    {
        private readonly object _syncRoot = new object();
        private IUnityCliLoopServerLifecycleSource _source;
        private Action _serverStartedHandlers;
        private Action _serverStoppingHandlers;
        private Action _serverLoopExitedHandlers;

        public event Action ServerStateChanged
        {
            add
            {
                ServerStarted += value;
                ServerStopping += value;
            }
            remove
            {
                ServerStarted -= value;
                ServerStopping -= value;
            }
        }

        public event Action ServerStarted
        {
            add
            {
                lock (_syncRoot)
                {
                    _serverStartedHandlers += value;
                }
            }
            remove
            {
                lock (_syncRoot)
                {
                    _serverStartedHandlers -= value;
                }
            }
        }

        public event Action ServerStopping
        {
            add
            {
                lock (_syncRoot)
                {
                    _serverStoppingHandlers += value;
                }
            }
            remove
            {
                lock (_syncRoot)
                {
                    _serverStoppingHandlers -= value;
                }
            }
        }

        public event Action ServerLoopExited
        {
            add
            {
                lock (_syncRoot)
                {
                    _serverLoopExitedHandlers += value;
                    if (_source != null)
                    {
                        _source.ServerLoopExited += value;
                    }
                }
            }
            remove
            {
                lock (_syncRoot)
                {
                    _serverLoopExitedHandlers -= value;
                    if (_source != null)
                    {
                        _source.ServerLoopExited -= value;
                    }
                }
            }
        }

        public void RegisterSource(IUnityCliLoopServerLifecycleSource source)
        {
            System.Diagnostics.Debug.Assert(source != null, "source must not be null");

            lock (_syncRoot)
            {
                if (_source != null && _serverLoopExitedHandlers != null)
                {
                    _source.ServerLoopExited -= _serverLoopExitedHandlers;
                }

                _source = source;
                if (_serverLoopExitedHandlers != null)
                {
                    _source.ServerLoopExited += _serverLoopExitedHandlers;
                }
            }
        }

        public void PublishServerStarted()
        {
            Action handlers;
            lock (_syncRoot)
            {
                handlers = _serverStartedHandlers;
            }

            handlers?.Invoke();
        }

        public void PublishServerStopping()
        {
            Action handlers;
            lock (_syncRoot)
            {
                handlers = _serverStoppingHandlers;
            }

            handlers?.Invoke();
        }
    }

    /// <summary>
    /// Provides Unity CLI Loop Server Application operations for its owning module.
    /// </summary>
    public sealed class UnityCliLoopServerApplicationService
    {
        private readonly IUnityCliLoopServerController _controller;

        public UnityCliLoopServerApplicationService(IUnityCliLoopServerController controller)
        {
            System.Diagnostics.Debug.Assert(controller != null, "controller must not be null");

            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public bool IsServerRunning => _controller.IsServerRunning;

        public Task RecoveryTask => _controller.RecoveryTask;

        public void StartServer()
        {
            _controller.StartServer();
        }

        public void StopServer()
        {
            _controller.StopServer();
        }

        public void AddServerStateChangedHandler(Action handler)
        {
            _controller.AddServerStateChangedHandler(handler);
        }

        public void RemoveServerStateChangedHandler(Action handler)
        {
            _controller.RemoveServerStateChangedHandler(handler);
        }

        public void AddServerStartedHandler(Action handler)
        {
            _controller.AddServerStartedHandler(handler);
        }

        public void RemoveServerStartedHandler(Action handler)
        {
            _controller.RemoveServerStartedHandler(handler);
        }
    }
}
