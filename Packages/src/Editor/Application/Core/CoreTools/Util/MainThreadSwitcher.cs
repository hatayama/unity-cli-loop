using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.Application
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

        public static SwitchToMainThreadAwaitable SwitchToMainThread()
        {
            return new SwitchToMainThreadAwaitable(CancellationToken.None);
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

                MainThreadSwitcher.AddContinuation(continuation);
            }
        }
    }

}
