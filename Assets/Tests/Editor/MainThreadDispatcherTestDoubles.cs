using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Reports a background thread and keeps queued continuations until the test runs them, as a
    /// stalled Editor main thread would.
    /// </summary>
    internal sealed class QueueingDispatcher : IMainThreadDispatcher
    {
        private readonly List<Action> _queued = new();

        public bool IsMainThread => false;

        public void Initialize()
        {
        }

        public void AddContinuation(Action continuation)
        {
            _queued.Add(continuation);
        }

        public void RunQueued()
        {
            foreach (Action continuation in _queued)
            {
                continuation();
            }

            _queued.Clear();
        }
    }

    /// <summary>
    /// Puts the Editor's own main-thread dispatcher back after a test registered a fake one.
    /// </summary>
    internal static class EditorMainThreadDispatcherRestorer
    {
        internal static void Restore()
        {
            EditorMainThreadDispatcher dispatcher = new EditorMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }
    }
}
