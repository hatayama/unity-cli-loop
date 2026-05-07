using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure dispatcher backed by Unity Editor's update loop.
    /// <summary>
    /// Provides Editor Main Thread Dispatcher behavior for Unity CLI Loop.
    /// </summary>
    public sealed class EditorMainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _continuationQueue = new ConcurrentQueue<Action>();
        private int _mainThreadId;

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public void Initialize()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            EditorApplication.update -= ProcessContinuationQueue;
            EditorApplication.update += ProcessContinuationQueue;
        }

        public void AddContinuation(Action continuation)
        {
            if (continuation == null)
            {
                return;
            }

            _continuationQueue.Enqueue(continuation);
        }

        private void ProcessContinuationQueue()
        {
            while (_continuationQueue.TryDequeue(out Action continuation))
            {
                try
                {
                    continuation?.Invoke();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }
    }
}
