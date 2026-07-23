using System;
using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Editor glue that pumps SignalTick while CLI work is in scope (plus a trailing window).
    /// Why: a always-on full-rate tick would keep an unfocused editor as expensive as a focused one;
    /// scoping to in-flight requests and a short trailing window restores normal throttling when idle.
    /// </summary>
    internal static class AutoTickPumpService
    {
        private static AutoTickPumpController _controller;
        private static Stopwatch _clock;
        private static Stopwatch _throttle;

        internal static void RegisterForEditorStartup()
        {
            _controller = new AutoTickPumpController(AutoTickPumpConstants.TRAILING_WINDOW_SECONDS);
            _clock = Stopwatch.StartNew();
            _throttle = Stopwatch.StartNew();

            // Same dual-registration pattern as EditorMainThreadDispatcher.Initialize:
            // update covers the normal editor loop; tick covers SignalTick-driven wakeups.
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            EditorApplicationTickBridge.RemoveTickHandler(Pump);
            EditorApplicationTickBridge.AddTickHandler(Pump);

            _controller.NotifyStartupCompleted(NowSeconds());
            // Why: after domain reload the editor may already be unfocused; reserve one tick so the
            // trailing-window pump (and delayCall recovery) can start without an OS focus kick.
            EditorApplicationTickBridge.SignalTick();
        }

        internal static IDisposable BeginScope()
        {
            Debug.Assert(_controller != null, "AutoTickPumpService must be registered before BeginScope");
            _controller.NotifyScopeStarted();
            // Why: wake a sleeping unfocused editor as soon as a CLI command arrives (same one-shot
            // wake pattern as EditorMainThreadDispatcher.AddContinuation).
            EditorApplicationTickBridge.SignalTick();
            return new AutoTickScope();
        }

        private static void Pump()
        {
            if (_controller == null)
            {
                return;
            }

            if (!_controller.ShouldPump(NowSeconds()))
            {
                return;
            }

            if (_throttle.ElapsedMilliseconds < AutoTickPumpConstants.PUMP_INTERVAL_MS)
            {
                return;
            }

            _throttle.Restart();
            EditorApplicationTickBridge.SignalTick();
        }

        private static double NowSeconds()
        {
            return _clock.Elapsed.TotalSeconds;
        }

        private sealed class AutoTickScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _controller.NotifyScopeEnded(NowSeconds());
            }
        }
    }
}
