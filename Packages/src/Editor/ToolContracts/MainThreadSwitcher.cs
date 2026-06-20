using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    // Port for dispatching continuations onto Unity's main thread.
    /// <summary>
    /// Defines the Main Thread Dispatcher contract used by Unity CLI Loop.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }
        void Initialize();
        void AddContinuation(Action continuation);
    }

    /// <summary>
    /// A class that provides functionality equivalent to UniTask's SwitchToMainThread.
    /// Handles switching to the main thread through the registered dispatcher.
    /// Reference: https://github.com/Cysharp/UniTask - PlayerLoopHelper implementation
    /// </summary>
    public static class MainThreadSwitcher
    {
        private static IMainThreadDispatcher ServiceValue;

        /// <summary>
        /// Determines whether the current thread is the main thread.
        /// </summary>
        public static bool IsMainThread => Service.IsMainThread;

        internal static void RegisterService(IMainThreadDispatcher service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        internal static void InitializeForEditorStartup()
        {
            Service.Initialize();
        }

        /// <summary>
        /// Add a continuation to the queue to be executed on the main thread.
        /// </summary>
        internal static void AddContinuation(Action continuation)
        {
            Service.AddContinuation(continuation);
        }

        public static SwitchToMainThreadAwaitable SwitchToMainThread(CancellationToken ct = default)
        {
            return new SwitchToMainThreadAwaitable(ct);
        }

        private static IMainThreadDispatcher Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop main-thread dispatcher is not registered.");
                }

                return ServiceValue;
            }
        }
    }

    /// <summary>
    /// An awaitable for switching to the main thread through MainThreadSwitcher.
    /// </summary>
    public struct SwitchToMainThreadAwaitable
    {
        private readonly CancellationToken cancellationToken;
        
        public SwitchToMainThreadAwaitable(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
        }
        
        public Awaiter GetAwaiter() => new(cancellationToken);

        public struct Awaiter : INotifyCompletion
        {
            private readonly CancellationToken cancellationToken;
            
            public Awaiter(CancellationToken cancellationToken)
            {
                this.cancellationToken = cancellationToken;
            }
            
            public bool IsCompleted
            {
                get
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return MainThreadSwitcher.IsMainThread;
                }
            }

            public void GetResult()
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            public void OnCompleted(Action continuation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (MainThreadSwitcher.IsMainThread)
                {
                    continuation();
                    return;
                }

                MainThreadSwitchContinuation queuedContinuation = new MainThreadSwitchContinuation(continuation);
                queuedContinuation.RegisterCancellation(cancellationToken);
                MainThreadSwitcher.AddContinuation(queuedContinuation.InvokeFromEditorQueue);
            }
        }
    }

    // Cancellation can happen while Unity is stalled, so the editor queue and cancellation share one resume path.
    internal sealed class MainThreadSwitchContinuation
    {
        private readonly Action _continuation;
        private CancellationTokenRegistration _cancellationRegistration;
        private int _hasInvoked;

        internal MainThreadSwitchContinuation(Action continuation)
        {
            Debug.Assert(continuation != null, "continuation must not be null");

            _continuation = continuation ?? throw new ArgumentNullException(nameof(continuation));
        }

        internal void RegisterCancellation(CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                return;
            }

            CancellationTokenRegistration registration = ct.Register(InvokeFromCancellation);
            _cancellationRegistration = registration;
            if (Interlocked.CompareExchange(ref _hasInvoked, 0, 0) != 0)
            {
                registration.Dispose();
            }
        }

        internal void InvokeFromEditorQueue()
        {
            Invoke(true);
        }

        private void InvokeFromCancellation()
        {
            Invoke(false);
        }

        private void Invoke(bool disposeCancellationRegistration)
        {
            if (Interlocked.Exchange(ref _hasInvoked, 1) != 0)
            {
                return;
            }

            if (disposeCancellationRegistration)
            {
                _cancellationRegistration.Dispose();
            }

            _continuation();
        }
    }

}
