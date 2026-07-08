#nullable enable
using System;
using System.Threading;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schedules mouse UI cleanup on the captured Unity main thread.
    /// </summary>
    internal sealed class MouseUiMainThreadCleanupScheduler
    {
        private SynchronizationContext? _mainThreadContext;

        internal void CaptureMainThreadContext()
        {
            _mainThreadContext = SynchronizationContext.Current;
            Debug.Assert(_mainThreadContext != null, "Main thread synchronization context must be captured.");
        }

        internal void QueueOverlayClear()
        {
            ExecuteCleanupOnMainThread(SimulateMouseUiOverlayState.Clear);
        }

        internal void ExecuteCleanupOnMainThread(Action cleanup)
        {
            Debug.Assert(cleanup != null, "cleanup must not be null");
            if (cleanup == null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }

            if (MainThreadSwitcher.IsMainThread)
            {
                cleanup();
                return;
            }

            SynchronizationContext? context = _mainThreadContext;
            Debug.Assert(context != null, "Main thread synchronization context must be captured before cleanup.");
            if (context == null)
            {
                throw new InvalidOperationException("Main thread synchronization context was not captured.");
            }

            // Why: timeout continuations can run on timer threads while Unity objects must still be cleaned up on the Editor thread.
            context.Post(_ => cleanup(), null);
        }
    }
}
