using System;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Invokes a tick signal from a timer thread at a fixed interval until disposed.
    /// Why a background thread: an unfocused Editor runs EditorApplication.update only every
    /// ~100 ms, and EditorApplication.SignalTick() re-arms a tick only when called from a thread
    /// other than the main thread (a call from inside an update handler is a no-op). Every
    /// awaited continuation in an async EditMode test resumes on the next update, so the
    /// suite otherwise idles 0.2-0.4 s per test.
    /// </summary>
    internal sealed class EditorTickPump : IDisposable
    {
        // Why bounded: Dispose must not hang the Editor if the timer thread is wedged, but a
        // normal callback finishes in microseconds, so this only guards the pathological case.
        private const int DisposeWaitMilliseconds = 1000;

        private readonly Timer _timer;
        private readonly Action _signalTick;
        private int _disposed;

        public EditorTickPump(Action signalTick, int intervalMilliseconds)
        {
            if (signalTick == null)
            {
                throw new ArgumentNullException(nameof(signalTick));
            }

            if (intervalMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervalMilliseconds),
                    "The pump interval must be positive.");
            }

            _signalTick = signalTick;
            _timer = new Timer(OnTimer, null, 0, intervalMilliseconds);
        }

        private void OnTimer(object state)
        {
            // Why re-check: Timer can deliver a callback that was already queued when Dispose
            // began; the disposed flag keeps that late callback from signaling a torn-down Editor.
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _signalTick();
        }

        /// <summary>
        /// Stops the pump. Blocks until any in-flight callback has finished, so no signal runs
        /// after this returns. Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            using ManualResetEvent callbacksDrained = new ManualResetEvent(false);
            if (_timer.Dispose(callbacksDrained))
            {
                callbacksDrained.WaitOne(DisposeWaitMilliseconds);
            }
        }
    }
}
