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

        void StartServer(bool clearServerStartingLockWhenReady = true);

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
    /// </summary>
    public interface IUnityCliLoopServerLifecycleSource
    {
        event Action ServerStarted;

        event Action ServerStopping;

        event Action ServerLoopExited;
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
    }

    /// <summary>
    /// Provides Unity CLI Loop Server Lifecycle Registry operations for its owning module.
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
                AddHandler(ref _serverStartedHandlers, value, source => source.ServerStarted += value);
            }
            remove
            {
                RemoveHandler(ref _serverStartedHandlers, value, source => source.ServerStarted -= value);
            }
        }

        public event Action ServerStopping
        {
            add
            {
                AddHandler(ref _serverStoppingHandlers, value, source => source.ServerStopping += value);
            }
            remove
            {
                RemoveHandler(ref _serverStoppingHandlers, value, source => source.ServerStopping -= value);
            }
        }

        public event Action ServerLoopExited
        {
            add
            {
                AddHandler(ref _serverLoopExitedHandlers, value, source => source.ServerLoopExited += value);
            }
            remove
            {
                RemoveHandler(ref _serverLoopExitedHandlers, value, source => source.ServerLoopExited -= value);
            }
        }

        public void RegisterSource(IUnityCliLoopServerLifecycleSource source)
        {
            System.Diagnostics.Debug.Assert(source != null, "source must not be null");

            lock (_syncRoot)
            {
                if (_source != null)
                {
                    UnwireHandlers(_source);
                }

                _source = source;
                WireHandlers(_source);
            }
        }

        private void AddHandler(
            ref Action handlers,
            Action value,
            Action<IUnityCliLoopServerLifecycleSource> wireHandler)
        {
            System.Diagnostics.Debug.Assert(value != null, "value must not be null");
            System.Diagnostics.Debug.Assert(wireHandler != null, "wireHandler must not be null");

            lock (_syncRoot)
            {
                handlers += value;
                if (_source != null)
                {
                    wireHandler(_source);
                }
            }
        }

        private void RemoveHandler(
            ref Action handlers,
            Action value,
            Action<IUnityCliLoopServerLifecycleSource> unwireHandler)
        {
            System.Diagnostics.Debug.Assert(value != null, "value must not be null");
            System.Diagnostics.Debug.Assert(unwireHandler != null, "unwireHandler must not be null");

            lock (_syncRoot)
            {
                handlers -= value;
                if (_source != null)
                {
                    unwireHandler(_source);
                }
            }
        }

        private void WireHandlers(IUnityCliLoopServerLifecycleSource source)
        {
            if (_serverStartedHandlers != null)
            {
                source.ServerStarted += _serverStartedHandlers;
            }

            if (_serverStoppingHandlers != null)
            {
                source.ServerStopping += _serverStoppingHandlers;
            }

            if (_serverLoopExitedHandlers != null)
            {
                source.ServerLoopExited += _serverLoopExitedHandlers;
            }
        }

        private void UnwireHandlers(IUnityCliLoopServerLifecycleSource source)
        {
            if (_serverStartedHandlers != null)
            {
                source.ServerStarted -= _serverStartedHandlers;
            }

            if (_serverStoppingHandlers != null)
            {
                source.ServerStopping -= _serverStoppingHandlers;
            }

            if (_serverLoopExitedHandlers != null)
            {
                source.ServerLoopExited -= _serverLoopExitedHandlers;
            }
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
    }

    /// <summary>
    /// Presentation boundary for server lifecycle state.
    /// UI code depends on this facade so transport and controller internals can move behind the application boundary.
    /// </summary>
    public static class UnityCliLoopServerApplicationFacade
    {
        private static UnityCliLoopServerApplicationService ServiceValue;

        internal static void RegisterService(UnityCliLoopServerApplicationService service)
        {
            System.Diagnostics.Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        private static UnityCliLoopServerApplicationService GetService()
        {
            if (ServiceValue == null)
            {
                throw new InvalidOperationException("Unity CLI Loop server application service is not registered.");
            }

            return ServiceValue;
        }

        public static void AddServerStateChangedHandler(Action handler)
        {
            GetService().AddServerStateChangedHandler(handler);
        }

        public static void RemoveServerStateChangedHandler(Action handler)
        {
            GetService().RemoveServerStateChangedHandler(handler);
        }

        public static bool IsServerRunning => GetService().IsServerRunning;

        public static Task RecoveryTask => GetService().RecoveryTask;

        public static void StartServer()
        {
            GetService().StartServer();
        }

        public static void StopServer()
        {
            GetService().StopServer();
        }
    }
}
