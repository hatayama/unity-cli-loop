using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Enum for PlayerLoopTiming (dummy implementation in Editor version).
    /// </summary>
    public enum PlayerLoopTiming
    {
        Initialization = 0,
        EarlyUpdate = 1,
        FixedUpdate = 2,
        PreUpdate = 3,
        Update = 4,
        PreLateUpdate = 5,
        PostLateUpdate = 6
    }

    /// <summary>
    /// A class that provides functionality equivalent to UniTask's SwitchToMainThread.
    /// Handles switching to the main thread using EditorApplication.update.
    /// Reference: https://github.com/Cysharp/UniTask - PlayerLoopHelper implementation
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadSwitcher
    {
        private static int _mainThreadId;
        private static readonly ConcurrentQueue<Action> _continuationQueue = new();
        
        /// <summary>
        /// Gets the ID of the main thread.
        /// </summary>
        public static int MainThreadId => _mainThreadId;
        
        /// <summary>
        /// Determines whether the current thread is the main thread.
        /// </summary>
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        static MainThreadSwitcher()
        {
            Initialize();
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            
            EditorApplication.update -= ProcessContinuationQueue;
            EditorApplication.update += ProcessContinuationQueue;
        }

        private static void ProcessContinuationQueue()
        {
            while (_continuationQueue.TryDequeue(out Action continuation))
            {
                try
                {
                    continuation?.Invoke();
                }
                catch (Exception ex)
                {
                    // Continuations are external code; catch exceptions to prevent queue disruption
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        /// <summary>
        /// Add a continuation to the queue to be executed on the main thread.
        /// </summary>
        internal static void AddContinuation(Action continuation)
        {
            if (continuation == null)
            {
                return;
            }
            _continuationQueue.Enqueue(continuation);
        }

        public static SwitchToMainThreadAwaitable SwitchToMainThread()
        {
            return new SwitchToMainThreadAwaitable(CancellationToken.None);
        }
        
        /// <summary>
        /// Switches to the main thread (with CancellationToken).
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(CancellationToken cancellationToken)
        {
            return new SwitchToMainThreadAwaitable(cancellationToken);
        }
        
        /// <summary>
        /// Switches to the main thread (with PlayerLoopTiming specified).
        /// PlayerLoopTiming is ignored in the Editor version.
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(PlayerLoopTiming timing)
        {
            return new SwitchToMainThreadAwaitable(CancellationToken.None);
        }
        
        /// <summary>
        /// Switches to the main thread (with PlayerLoopTiming and CancellationToken specified).
        /// PlayerLoopTiming is ignored in the Editor version.
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(PlayerLoopTiming timing, CancellationToken cancellationToken)
        {
            return new SwitchToMainThreadAwaitable(cancellationToken);
        }

    }

    /// <summary>
    /// An awaitable for switching to the main thread using EditorApplication.update queue.
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
