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
    /// Receives the start and end of each switch that has to wait in the Editor main-thread queue.
    /// Implementations run inside the queue's resume path, so they must only update counters and
    /// never throw: a throwing observer would leave the awaiting method suspended forever.
    /// </summary>
    internal interface IMainThreadWaitObserver
    {
        void OnWaitStarted();
        void OnWaitEnded();
    }

    /// <summary>
    /// Holds the wait observer of the current async flow, so a tool request can learn when it is
    /// only waiting for the Editor main thread without tools having to report it themselves.
    /// </summary>
    internal static class MainThreadWaitObservation
    {
        private static readonly AsyncLocal<IMainThreadWaitObserver> CurrentObserver = new();

        internal static IMainThreadWaitObserver Current => CurrentObserver.Value;

        internal static void SetCurrent(IMainThreadWaitObserver observer)
        {
            CurrentObserver.Value = observer;
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
            
            // A cancelled switch completes at once so GetResult reports it without leaving the caller's thread.
            public bool IsCompleted => cancellationToken.IsCancellationRequested || MainThreadSwitcher.IsMainThread;

            // The only place a cancellation is observed: an exception thrown from OnCompleted never reaches
            // the awaiting method, whose task then never completes and never runs its finally blocks.
            public void GetResult()
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Never throws, even when cancelled after IsCompleted returned false: the cancellation
            // registration resumes the continuation, and GetResult then throws.
            public void OnCompleted(Action continuation)
            {
                if (MainThreadSwitcher.IsMainThread)
                {
                    continuation();
                    return;
                }

                // Why report only here: this is the one path where the caller really waits for the
                // Editor main thread. IsCompleted and the IsMainThread branch above resume at once.
                IMainThreadWaitObserver waitObserver = MainThreadWaitObservation.Current;
                MainThreadSwitchContinuation queuedContinuation =
                    new MainThreadSwitchContinuation(continuation, waitObserver);
                waitObserver?.OnWaitStarted();
                queuedContinuation.RegisterCancellation(cancellationToken);
                MainThreadSwitcher.AddContinuation(queuedContinuation.InvokeFromEditorQueue);
            }
        }
    }

    // Cancellation can happen while Unity is stalled, so the editor queue and cancellation share one resume path.
    internal sealed class MainThreadSwitchContinuation
    {
        private readonly Action _continuation;
        private readonly IMainThreadWaitObserver _waitObserver;
        private CancellationTokenRegistration _cancellationRegistration;
        private int _hasInvoked;

        internal MainThreadSwitchContinuation(Action continuation, IMainThreadWaitObserver waitObserver)
        {
            Debug.Assert(continuation != null, "continuation must not be null");

            _continuation = continuation ?? throw new ArgumentNullException(nameof(continuation));
            _waitObserver = waitObserver;
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

            // After the once-only guard so a wait resumed by both cancellation and the Editor
            // queue ends exactly once.
            _waitObserver?.OnWaitEnded();

            if (disposeCancellationRegistration)
            {
                _cancellationRegistration.Dispose();
            }

            _continuation();
        }
    }

}
